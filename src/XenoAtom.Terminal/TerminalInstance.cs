// Copyright (c) Alexandre Mutel. All rights reserved.
// Licensed under the BSD-Clause 2 license.
// See license.txt file in the project root for full license information.

using System.Runtime.CompilerServices;
using System.Runtime.InteropServices;
using XenoAtom.Ansi;
using XenoAtom.Terminal.Backends;
using XenoAtom.Terminal.Internal;

namespace XenoAtom.Terminal;

/// <summary>
/// Stateful, instance-based terminal API. The static <see cref="Terminal"/> facade forwards to a single global instance.
/// </summary>
public sealed partial class TerminalInstance : IDisposable
{
    private readonly Lock _outputLock = new();
    private readonly Lock _defaultInputQueueLock = new();
    private readonly Queue<TerminalEvent> _defaultInputQueue = new();

    private bool _isDisposed;
    private bool _isInitialized;

    private ITerminalBackend? _backend;
    private TerminalOptions? _options;

    private TextWriter? _rawOut;
    private TextWriter? _rawError;

    private TextWriter? _out;
    private TextWriter? _error;

    private AnsiCapabilities? _ansiCapabilities;
    private AnsiWriter? _writer;
    private AnsiWriter? _writerUnsafe;
    private AnsiMarkup? _markupUnsafe;

    private TerminalInputOptions? _inputOptions;
    private readonly TerminalCursor _cursor;
    private readonly TerminalWindow _window;
    private readonly TextReader _in;

    private AnsiStyle _style = AnsiStyle.Default;
    private string? _readLineClipboard;

    internal TerminalInstance()
    {
        _cursor = new TerminalCursor(this);
        _window = new TerminalWindow(this);
        _in = new TerminalTextReader(this);
    }

    /// <summary>
    /// Gets a value indicating whether this instance has been initialized with a backend.
    /// </summary>
    public bool IsInitialized => _isInitialized;

    /// <summary>
    /// Gets the backend used by this terminal instance.
    /// </summary>
    /// <exception cref="InvalidOperationException">Thrown if the instance is not initialized.</exception>
    public ITerminalBackend Backend => _backend ?? throw new InvalidOperationException("Terminal is not initialized.");

    /// <summary>
    /// Gets the options used by this terminal instance.
    /// </summary>
    /// <exception cref="InvalidOperationException">Thrown if the instance is not initialized.</exception>
    public TerminalOptions Options => _options ?? throw new InvalidOperationException("Terminal is not initialized.");

    /// <summary>
    /// Gets the detected terminal capabilities as reported by the backend.
    /// </summary>
    public TerminalCapabilities Capabilities => Backend.Capabilities;

    /// <summary>
    /// Gets a value indicating whether the terminal appears interactive (not redirected).
    /// </summary>
    public bool IsInteractive
    {
        get
        {
            var caps = Capabilities;
            return !caps.IsOutputRedirected && !caps.IsInputRedirected;
        }
    }

    /// <summary>
    /// Gets the current terminal size.
    /// </summary>
    public TerminalSize Size => Backend.GetSize();

    /// <summary>
    /// Gets a <see cref="TextReader"/> that reads from terminal input.
    /// </summary>
    public TextReader In => _in;

    /// <summary>
    /// Gets cursor-related operations.
    /// </summary>
    public TerminalCursor Cursor => _cursor;

    /// <summary>
    /// Gets window and buffer size operations.
    /// </summary>
    public TerminalWindow Window => _window;

    /// <summary>
    /// Gets or sets the current text style (best effort).
    /// </summary>
    /// <remarks>
    /// Terminal state is not reliably queryable. This property reflects style changes performed via Terminal APIs.
    /// If raw ANSI is written to the output outside of Terminal APIs, this state may become inaccurate.
    /// </remarks>
    public AnsiStyle Style
    {
        get => _style;
        set => SetStyleCore(value);
    }

    /// <summary>
    /// Gets or sets the current foreground color (best effort).
    /// </summary>
    public AnsiColor ForegroundColor
    {
        get => _style.Foreground ?? AnsiColor.Default;
        set => SetStyleCore(_style.WithForeground(value));
    }

    /// <summary>
    /// Gets or sets the current background color (best effort).
    /// </summary>
    public AnsiColor BackgroundColor
    {
        get => _style.Background ?? AnsiColor.Default;
        set => SetStyleCore(_style.WithBackground(value));
    }

    /// <summary>
    /// Gets or sets the current decoration flags (best effort).
    /// </summary>
    public AnsiDecorations Decorations
    {
        get => _style.Decorations;
        set => SetStyleCore(_style.WithDecorations(value));
    }

    /// <summary>
    /// Gets or sets the terminal title (best effort).
    /// </summary>
    public string Title
    {
        get
        {
            if (Backend.TryGetTitle(out var title))
            {
                return title;
            }

            if (Options.StrictMode)
            {
                throw new NotSupportedException("Terminal title is not supported by this backend.");
            }

            return string.Empty;
        }
        set
        {
            ArgumentNullException.ThrowIfNull(value);
            lock (_outputLock)
            {
                Backend.SetTitle(value);
            }
        }
    }

    /// <summary>
    /// Emits an audible beep (best effort).
    /// </summary>
    public void Beep()
    {
        lock (_outputLock)
        {
            Backend.Beep();
        }
    }

    /// <summary>
    /// Gets or sets the 0-based cursor column (best effort).
    /// </summary>
    public int CursorLeft
    {
        get => Cursor.Position.Column;
        set => Cursor.Position = new TerminalPosition(value, Cursor.Position.Row);
    }

    /// <summary>
    /// Gets or sets the 0-based cursor row (best effort).
    /// </summary>
    public int CursorTop
    {
        get => Cursor.Position.Row;
        set => Cursor.Position = new TerminalPosition(Cursor.Position.Column, value);
    }

    /// <summary>
    /// Sets the cursor position (0-based).
    /// </summary>
    public void SetCursorPosition(int left, int top) => SetCursorPosition(new TerminalPosition(left, top));

    /// <summary>
    /// Gets the cursor position (0-based).
    /// </summary>
    public TerminalPosition GetCursorPosition()
    {
        if (TryGetCursorPosition(out var position))
        {
            return position;
        }

        if (Options.StrictMode)
        {
            throw new NotSupportedException("Cursor position is not supported by this backend.");
        }

        return default;
    }

    /// <summary>
    /// Tries to get the cursor position (0-based).
    /// </summary>
    public bool TryGetCursorPosition(out TerminalPosition position) => Backend.TryGetCursorPosition(out position);

    /// <summary>
    /// Sets the cursor position (0-based, best effort).
    /// </summary>
    public void SetCursorPosition(TerminalPosition position)
    {
        if (position.Column < 0) throw new ArgumentOutOfRangeException(nameof(position), "Column must be >= 0.");
        if (position.Row < 0) throw new ArgumentOutOfRangeException(nameof(position), "Row must be >= 0.");

        if (Capabilities.SupportsCursorPositionSet)
        {
            lock (_outputLock)
            {
                Backend.SetCursorPosition(position);
            }
            return;
        }

        if (Capabilities.AnsiEnabled && !Capabilities.IsOutputRedirected)
        {
            lock (_outputLock)
            {
                _writerUnsafe!.CursorPosition(position.Row + 1, position.Column + 1);
            }
            return;
        }

        if (Options.StrictMode)
        {
            throw new NotSupportedException("Cursor position cannot be set by this backend.");
        }
    }

    /// <summary>
    /// Gets the cursor visibility (best effort).
    /// </summary>
    public bool GetCursorVisible()
    {
        if (Backend.TryGetCursorVisible(out var visible))
        {
            return visible;
        }

        if (Options.StrictMode)
        {
            throw new NotSupportedException("Cursor visibility is not supported by this backend.");
        }

        return true;
    }

    /// <summary>
    /// Sets the cursor visibility (best effort).
    /// </summary>
    public void SetCursorVisible(bool visible)
    {
        if (Capabilities.SupportsCursorVisibility)
        {
            lock (_outputLock)
            {
                Backend.SetCursorVisible(visible);
            }
            return;
        }

        if (Capabilities.AnsiEnabled && !Capabilities.IsOutputRedirected)
        {
            lock (_outputLock)
            {
                _writerUnsafe!.ShowCursor(visible);
            }
            return;
        }

        if (Options.StrictMode)
        {
            throw new NotSupportedException("Cursor visibility cannot be changed by this backend.");
        }
    }

    /// <summary>
    /// Saves the current cursor position and restores it when disposed (best effort).
    /// </summary>
    public TerminalScope UseCursorPosition()
    {
        if (Capabilities.SupportsCursorPositionGet && Capabilities.SupportsCursorPositionSet && Backend.TryGetCursorPosition(out var previous))
        {
            return TerminalScope.Create(() =>
            {
                lock (_outputLock)
                {
                    Backend.SetCursorPosition(previous);
                }
            });
        }

        if (Capabilities.AnsiEnabled && !Capabilities.IsOutputRedirected)
        {
            lock (_outputLock)
            {
                _writerUnsafe!.SaveCursorPosition();
            }

            return TerminalScope.Create(() =>
            {
                lock (_outputLock)
                {
                    _writerUnsafe!.RestoreCursorPosition();
                }
            });
        }

        if (Options.StrictMode)
        {
            throw new NotSupportedException("Saving/restoring cursor position is not supported by this backend.");
        }

        return TerminalScope.Empty;
    }

    /// <summary>
    /// Sets the cursor position and restores the previous position when disposed (best effort).
    /// </summary>
    public TerminalScope UseCursorPosition(TerminalPosition position)
    {
        var restore = UseCursorPosition();
        if (restore.IsEmpty)
        {
            SetCursorPosition(position);
            return restore;
        }

        SetCursorPosition(position);
        return restore;
    }

    /// <summary>
    /// Gets or sets the window width in character cells (best effort).
    /// </summary>
    public int WindowWidth
    {
        get => GetWindowSize().Columns;
        set => SetWindowSize(new TerminalSize(value, WindowHeight));
    }

    /// <summary>
    /// Gets or sets the window height in character cells (best effort).
    /// </summary>
    public int WindowHeight
    {
        get => GetWindowSize().Rows;
        set => SetWindowSize(new TerminalSize(WindowWidth, value));
    }

    /// <summary>
    /// Gets or sets the buffer width in character cells (best effort).
    /// </summary>
    public int BufferWidth
    {
        get => GetBufferSize().Columns;
        set => SetBufferSize(new TerminalSize(value, BufferHeight));
    }

    /// <summary>
    /// Gets or sets the buffer height in character cells (best effort).
    /// </summary>
    public int BufferHeight
    {
        get => GetBufferSize().Rows;
        set => SetBufferSize(new TerminalSize(BufferWidth, value));
    }

    /// <summary>
    /// Gets the largest supported window width in character cells (best effort).
    /// </summary>
    public int LargestWindowWidth => GetLargestWindowSize().Columns;

    /// <summary>
    /// Gets the largest supported window height in character cells (best effort).
    /// </summary>
    public int LargestWindowHeight => GetLargestWindowSize().Rows;

    /// <summary>
    /// Gets the current window size in character cells (best effort).
    /// </summary>
    public TerminalSize GetWindowSize() => Backend.GetWindowSize();

    /// <summary>
    /// Gets the current buffer size in character cells (best effort).
    /// </summary>
    public TerminalSize GetBufferSize() => Backend.GetBufferSize();

    /// <summary>
    /// Gets the largest supported window size in character cells (best effort).
    /// </summary>
    public TerminalSize GetLargestWindowSize() => Backend.GetLargestWindowSize();

    /// <summary>
    /// Sets the window size in character cells (best effort).
    /// </summary>
    public void SetWindowSize(TerminalSize size)
    {
        lock (_outputLock)
        {
            Backend.SetWindowSize(size);
        }
    }

    /// <summary>
    /// Sets the buffer size in character cells (best effort).
    /// </summary>
    public void SetBufferSize(TerminalSize size)
    {
        lock (_outputLock)
        {
            Backend.SetBufferSize(size);
        }
    }

    /// <summary>
    /// Gets a serialized <see cref="TextWriter"/> for terminal standard output.
    /// </summary>
    /// <remarks>
    /// Use this writer to avoid interleaving escape sequences between threads.
    /// </remarks>
    /// <exception cref="InvalidOperationException">Thrown if the instance is not initialized.</exception>
    public TextWriter Out => _out ?? throw new InvalidOperationException("Terminal is not initialized.");

    /// <summary>
    /// Gets a serialized <see cref="TextWriter"/> for terminal standard error.
    /// </summary>
    /// <remarks>
    /// Use this writer to avoid interleaving escape sequences between threads.
    /// </remarks>
    /// <exception cref="InvalidOperationException">Thrown if the instance is not initialized.</exception>
    public TextWriter Error => _error ?? throw new InvalidOperationException("Terminal is not initialized.");

    /// <summary>
    /// Gets a shared <see cref="AnsiWriter"/> bound to <see cref="Out"/>.
    /// </summary>
    /// <exception cref="InvalidOperationException">Thrown if the instance is not initialized.</exception>
    public AnsiWriter Writer => _writer ?? throw new InvalidOperationException("Terminal is not initialized.");

    internal void Initialize(ITerminalBackend? backend, TerminalOptions? options)
    {
        ThrowIfDisposed();

        if (_isInitialized)
        {
            throw new InvalidOperationException("TerminalInstance is already initialized.");
        }

        _options = options ?? new TerminalOptions();
        _backend = backend ?? CreateDefaultBackend();
        _backend.Initialize(_options);

        _rawOut = _backend.Out;
        _rawError = _backend.Error;

        _out = new SynchronizedTextWriter(_rawOut, _outputLock);
        _error = new SynchronizedTextWriter(_rawError, _outputLock);

        _ansiCapabilities = Internal.TerminalAnsiCapabilities.Create(_backend.Capabilities, _options);
        _writer = new AnsiWriter(_out, _ansiCapabilities);
        _writerUnsafe = new AnsiWriter(_rawOut, _ansiCapabilities);
        _markupUnsafe = new AnsiMarkup(_writerUnsafe);

        _style = AnsiStyle.Default;

        _isInitialized = true;
    }

    /// <summary>
    /// Resets this instance for tests by disposing the backend (best effort) and clearing internal state.
    /// </summary>
    /// <remarks>
    /// This method is intended for test processes only.
    /// </remarks>
    public void ResetForTests()
    {
        if (!_isInitialized)
        {
            return;
        }

        try
        {
            _backend?.Dispose();
        }
        catch
        {
            // Best-effort; test reset must not throw.
        }

        _isInitialized = false;
        _backend = null;
        _options = null;
        _rawOut = null;
        _rawError = null;
        _out = null;
        _error = null;
        _ansiCapabilities = null;
        _writer = null;
        _writerUnsafe = null;
        _markupUnsafe = null;
        _style = AnsiStyle.Default;
        lock (_defaultInputQueueLock)
        {
            _defaultInputQueue.Clear();
        }
        Volatile.Write(ref _inputOptions, null);
    }

    /// <summary>
    /// Writes text to the terminal output.
    /// </summary>
    /// <param name="text">The text to write.</param>
    /// <returns>This instance for fluent usage.</returns>
    public TerminalInstance Write(string text)
    {
        ArgumentNullException.ThrowIfNull(text);
        lock (_outputLock)
        {
            _rawOut!.Write(text);
        }
        return this;
    }

    /// <summary>
    /// Writes text to the terminal output.
    /// </summary>
    /// <param name="text">The text to write.</param>
    /// <returns>This instance for fluent usage.</returns>
    public TerminalInstance Write(ReadOnlySpan<char> text)
    {
        lock (_outputLock)
        {
            _rawOut!.Write(text);
        }
        return this;
    }

    /// <summary>
    /// Writes a line terminator, or writes text followed by a line terminator.
    /// </summary>
    /// <param name="text">Optional text to write before the line terminator.</param>
    /// <returns>This instance for fluent usage.</returns>
    public TerminalInstance WriteLine(string? text = null)
    {
        lock (_outputLock)
        {
            _rawOut!.WriteLine(text);
        }
        return this;
    }

    /// <summary>
    /// Writes text followed by a line terminator.
    /// </summary>
    /// <param name="text">The text to write.</param>
    /// <returns>This instance for fluent usage.</returns>
    public TerminalInstance WriteLine(ReadOnlySpan<char> text)
    {
        lock (_outputLock)
        {
            _rawOut!.Write(text);
            _rawOut!.WriteLine();
        }
        return this;
    }

    /// <summary>
    /// Writes markup text using <see cref="AnsiMarkup"/>.
    /// </summary>
    /// <param name="markup">The markup to write.</param>
    /// <returns>This instance for fluent usage.</returns>
    public TerminalInstance WriteMarkup(string markup)
    {
        ArgumentNullException.ThrowIfNull(markup);
        SetStyleCore(AnsiStyle.Default);
        lock (_outputLock)
        {
            _markupUnsafe!.Write(markup.AsSpan());
        }
        return this;
    }

    /// <summary>
    /// Writes markup text using <see cref="AnsiMarkup"/>.
    /// </summary>
    /// <param name="markup">The markup to write.</param>
    /// <returns>This instance for fluent usage.</returns>
    public TerminalInstance WriteMarkup(ReadOnlySpan<char> markup)
    {
        SetStyleCore(AnsiStyle.Default);
        lock (_outputLock)
        {
            _markupUnsafe!.Write(markup);
        }
        return this;
    }

    /// <summary>
    /// Writes markup text built using an interpolated string handler.
    /// </summary>
    /// <param name="markup">The interpolated handler containing the markup.</param>
    /// <returns>This instance for fluent usage.</returns>
    public TerminalInstance WriteMarkup(ref AnsiMarkupInterpolatedStringHandler markup)
    {
        SetStyleCore(AnsiStyle.Default);
        lock (_outputLock)
        {
            _markupUnsafe!.Write(ref markup);
        }
        return this;
    }

    /// <summary>
    /// Writes markup text followed by a line terminator.
    /// </summary>
    /// <param name="markup">The markup to write.</param>
    /// <returns>This instance for fluent usage.</returns>
    public TerminalInstance WriteMarkupLine(string markup)
    {
        WriteMarkup(markup);
        WriteLine();
        return this;
    }

    /// <summary>
    /// Writes markup text built using an interpolated string handler followed by a line terminator.
    /// </summary>
    /// <param name="markup">The interpolated handler containing the markup.</param>
    /// <returns>This instance for fluent usage.</returns>
    public TerminalInstance WriteMarkupLine(ref AnsiMarkupInterpolatedStringHandler markup)
    {
        SetStyleCore(AnsiStyle.Default);
        lock (_outputLock)
        {
            _markupUnsafe!.Write(ref markup);
            _rawOut!.WriteLine();
        }
        return this;
    }

    /// <summary>
    /// Measures the visible width of a string that contains ANSI escape sequences or XenoAtom markup.
    /// </summary>
    /// <param name="textWithAnsiOrMarkup">The string to measure.</param>
    /// <returns>The visible cell width.</returns>
    public int MeasureStyledWidth(string textWithAnsiOrMarkup)
    {
        ArgumentNullException.ThrowIfNull(textWithAnsiOrMarkup);

        if (textWithAnsiOrMarkup.AsSpan().Contains('\x1b'))
        {
            return AnsiText.GetVisibleWidth(textWithAnsiOrMarkup.AsSpan());
        }

        var rendered = AnsiMarkup.Render(textWithAnsiOrMarkup.AsSpan(), _ansiCapabilities);
        return AnsiText.GetVisibleWidth(rendered.AsSpan());
    }

    /// <summary>
    /// Ensures multiple ANSI writes are not interleaved with other terminal output.
    /// </summary>
    /// <param name="write">The write callback that receives an <see cref="AnsiWriter"/>.</param>
    public void WriteAtomic(Action<AnsiWriter> write)
    {
        ArgumentNullException.ThrowIfNull(write);
        lock (_outputLock)
        {
            write(_writerUnsafe!);
        }
    }

    /// <summary>
    /// Ensures multiple text writes are not interleaved with other terminal output.
    /// </summary>
    /// <param name="write">The write callback that receives a <see cref="TextWriter"/>.</param>
    public void WriteAtomic(Action<TextWriter> write)
    {
        ArgumentNullException.ThrowIfNull(write);
        lock (_outputLock)
        {
            write(_rawOut!);
        }
    }

    /// <summary>
    /// Ensures multiple text writes to standard error are not interleaved with other terminal output.
    /// </summary>
    /// <param name="write">The write callback that receives a <see cref="TextWriter"/>.</param>
    public void WriteErrorAtomic(Action<TextWriter> write)
    {
        ArgumentNullException.ThrowIfNull(write);
        lock (_outputLock)
        {
            write(_rawError!);
        }
    }

    /// <summary>
    /// Flushes the underlying backend output (best effort).
    /// </summary>
    /// <returns>This instance for fluent usage.</returns>
    public TerminalInstance Flush()
    {
        lock (_outputLock)
        {
            _backend!.Flush();
        }
        return this;
    }

    /// <summary>
    /// Clears the terminal (best effort).
    /// </summary>
    /// <param name="kind">The clear mode.</param>
    /// <returns>This instance for fluent usage.</returns>
    public TerminalInstance Clear(TerminalClearKind kind = TerminalClearKind.Screen)
    {
        lock (_outputLock)
        {
            _backend!.Clear(kind);
        }
        return this;
    }

    /// <summary>
    /// Enables raw/cbreak mode within a scope and restores the previous mode when disposed (best effort).
    /// </summary>
    /// <param name="kind">The raw mode kind.</param>
    /// <returns>A scope that restores the previous mode on dispose.</returns>
    public TerminalScope UseRawMode(TerminalRawModeKind kind = TerminalRawModeKind.CBreak) => UseBackendScope(b => b.UseRawMode(kind));

    /// <summary>
    /// Enables the alternate screen buffer within a scope and restores the previous state when disposed (best effort).
    /// </summary>
    /// <returns>A scope that restores the previous state on dispose.</returns>
    public TerminalScope UseAlternateScreen() => UseBackendScope(b => b.UseAlternateScreen());

    /// <summary>
    /// Hides the cursor within a scope and restores the previous state when disposed (best effort).
    /// </summary>
    /// <returns>A scope that restores the previous state on dispose.</returns>
    public TerminalScope HideCursor() => UseBackendScope(b => b.HideCursor());

    /// <summary>
    /// Enables mouse reporting within a scope and restores the previous state when disposed (best effort).
    /// </summary>
    /// <param name="mode">The mouse reporting mode.</param>
    /// <returns>A scope that restores the previous state on dispose.</returns>
    public TerminalScope EnableMouse(TerminalMouseMode mode = TerminalMouseMode.Drag) => UseBackendScope(b => b.EnableMouse(mode));

    /// <summary>
    /// Enables bracketed paste within a scope and restores the previous state when disposed (best effort).
    /// </summary>
    /// <returns>A scope that restores the previous state on dispose.</returns>
    public TerminalScope EnableBracketedPaste() => UseBackendScope(b => b.EnableBracketedPaste());

    /// <summary>
    /// Sets the terminal title within a scope and restores the previous title when disposed (best effort).
    /// </summary>
    /// <param name="title">The title to set.</param>
    /// <returns>A scope that restores the previous title on dispose.</returns>
    public TerminalScope UseTitle(string title)
    {
        ArgumentNullException.ThrowIfNull(title);
        return UseBackendScope(b => b.UseTitle(title));
    }

    /// <summary>
    /// Enables or disables input echo within a scope and restores the previous state when disposed (best effort).
    /// </summary>
    /// <param name="enabled">Whether echo is enabled.</param>
    /// <returns>A scope that restores the previous state on dispose.</returns>
    public TerminalScope SetInputEcho(bool enabled) => UseBackendScope(b => b.SetInputEcho(enabled));

    private TerminalScope UseBackendScope(Func<ITerminalBackend, TerminalScope> createScope)
    {
        TerminalScope backendScope;
        lock (_outputLock)
        {
            backendScope = createScope(_backend!);
        }

        if (backendScope.IsEmpty)
        {
            return backendScope;
        }

        return TerminalScope.Create(() =>
        {
            lock (_outputLock)
            {
                backendScope.Dispose();
            }
        });
    }

    /// <summary>
    /// Gets a value indicating whether the input loop is currently running.
    /// </summary>
    public bool IsInputRunning => Backend.IsInputRunning;

    /// <summary>
    /// Starts the input loop (idempotent).
    /// </summary>
    /// <param name="options">Optional input options.</param>
    public void StartInput(TerminalInputOptions? options = null)
    {
        var effectiveOptions = options is null ? CreateDefaultInputOptions() : CopyInputOptions(options);
        NormalizeInputOptions(effectiveOptions);
        Volatile.Write(ref _inputOptions, CopyInputOptions(effectiveOptions));
        Backend.StartInput(effectiveOptions);
    }

    /// <summary>
    /// Stops the input loop and disposes any input resources (idempotent).
    /// </summary>
    /// <param name="cancellationToken">A cancellation token.</param>
    public async Task StopInputAsync(CancellationToken cancellationToken = default)
    {
        await Backend.StopInputAsync(cancellationToken).ConfigureAwait(false);
        Volatile.Write(ref _inputOptions, null);
        lock (_defaultInputQueueLock)
        {
            _defaultInputQueue.Clear();
        }

        // Drain any pending events from the backend default stream so subsequent ReadKey/ReadLine
        // calls don't replay already-processed events (e.g. after consuming via ReadEventsAsync).
        while (Backend.TryReadEvent(out _))
        {
        }
    }

    /// <summary>
    /// Reads terminal input events as an async stream.
    /// </summary>
    /// <param name="cancellationToken">A cancellation token.</param>
    /// <returns>An async stream of terminal events.</returns>
    /// <remarks>
    /// When <see cref="TerminalOptions.ImplicitStartInput"/> is enabled, this method starts the input loop automatically.
    /// </remarks>
    public IAsyncEnumerable<TerminalEvent> ReadEventsAsync(CancellationToken cancellationToken = default)
    {
        if (!Backend.IsInputRunning && Options.ImplicitStartInput)
        {
            StartInput();
        }

        return ReadEventsFromDefaultAsync(cancellationToken);
    }

    /// <summary>
    /// Tries to read a single event without awaiting.
    /// </summary>
    /// <param name="ev">When this method returns <see langword="true"/>, contains the event.</param>
    /// <returns><see langword="true"/> when an event was available; otherwise <see langword="false"/>.</returns>
    /// <remarks>
    /// When <see cref="TerminalOptions.ImplicitStartInput"/> is enabled, this method starts the input loop automatically.
    /// </remarks>
    public bool TryReadEvent(out TerminalEvent ev)
    {
        if (!Backend.IsInputRunning && Options.ImplicitStartInput)
        {
            StartInput();
        }

        while (TryReadDefaultEvent(out ev))
        {
            if (ShouldPublishEvent(ev))
            {
                return true;
            }
        }

        ev = null!;
        return false;
    }

    /// <summary>
    /// Reads the next event asynchronously.
    /// </summary>
    /// <param name="cancellationToken">A cancellation token.</param>
    /// <returns>The next terminal event.</returns>
    /// <remarks>
    /// When <see cref="TerminalOptions.ImplicitStartInput"/> is enabled, this method starts the input loop automatically.
    /// </remarks>
    public ValueTask<TerminalEvent> ReadEventAsync(CancellationToken cancellationToken = default)
    {
        if (!Backend.IsInputRunning && Options.ImplicitStartInput)
        {
            StartInput();
        }

        return ReadEventFilteredAsync(cancellationToken);
    }

    /// <summary>
    /// Gets a value indicating whether a key event is available on the default input stream.
    /// </summary>
    public bool KeyAvailable
    {
        get
        {
            if (!Backend.IsInputRunning && Options.ImplicitStartInput)
            {
                StartInput();
            }

            if (!TryPeekDefaultEvent(out var ev))
            {
                return false;
            }

            return ev is TerminalKeyEvent;
        }
    }

    /// <summary>
    /// Gets or sets a value indicating whether Ctrl+C / Ctrl+Break are treated as regular key input (best effort).
    /// </summary>
    /// <remarks>
    /// This is similar to <see cref="Console.TreatControlCAsInput"/>.
    /// </remarks>
    public bool TreatControlCAsInput
    {
        get => Options.TreatControlCAsInput;
        set
        {
            Options.TreatControlCAsInput = value;
            if (Backend.IsInputRunning)
            {
                var current = Volatile.Read(ref _inputOptions) ?? CreateDefaultInputOptions();
                var updated = CopyInputOptions(current);
                updated.TreatControlCAsInput = value;
                NormalizeInputOptions(updated);
                StartInput(updated);
            }
        }
    }

    /// <summary>
    /// Reads the next key from the default input stream.
    /// </summary>
    public TerminalKeyInfo ReadKey(bool intercept = false) => ReadKey(new TerminalReadKeyOptions { Intercept = intercept });

    /// <summary>
    /// Reads the next key from the default input stream.
    /// </summary>
    public TerminalKeyInfo ReadKey(TerminalReadKeyOptions? options = null)
        => ReadKeyAsync(options, CancellationToken.None).AsTask().GetAwaiter().GetResult();

    /// <summary>
    /// Reads the next key from the default input stream.
    /// </summary>
    public ValueTask<TerminalKeyInfo> ReadKeyAsync(bool intercept, CancellationToken cancellationToken = default)
        => ReadKeyAsync(new TerminalReadKeyOptions { Intercept = intercept }, cancellationToken);

    /// <summary>
    /// Reads the next key from the default input stream.
    /// </summary>
    public async ValueTask<TerminalKeyInfo> ReadKeyAsync(TerminalReadKeyOptions? options = null, CancellationToken cancellationToken = default)
    {
        options ??= new TerminalReadKeyOptions();

        if (!Backend.IsInputRunning)
        {
            if (Options.ImplicitStartInput)
            {
                StartInput();
            }
            else
            {
                throw new InvalidOperationException("Terminal input is not running. Call Terminal.StartInput(...) first, or enable TerminalOptions.ImplicitStartInput.");
            }
        }

        using var _noEchoScope = SetInputEcho(enabled: false);

        while (true)
        {
            TerminalEvent ev;
            try
            {
                ev = await ReadEventAsync(cancellationToken).ConfigureAwait(false);
            }
            catch (System.Threading.Channels.ChannelClosedException)
            {
                throw new InvalidOperationException("Terminal input completed.");
            }
            cancellationToken.ThrowIfCancellationRequested();

            switch (ev)
            {
                case TerminalSignalEvent:
                    if (!TreatControlCAsInput)
                    {
                        throw new OperationCanceledException("ReadKey interrupted by terminal signal.");
                    }
                    break;

                case TerminalKeyEvent key:
                    if (!options.Intercept)
                    {
                        EchoKey(key);
                    }

                    return new TerminalKeyInfo(key.Key, key.Char ?? '\0', key.Modifiers);
            }
        }
    }

    /// <summary>
    /// Reads a line of text from the terminal using terminal input events.
    /// </summary>
    /// <param name="options">Optional read options.</param>
    /// <returns>The line read from the terminal, or <see langword="null"/> if input completed.</returns>
    public string? ReadLine(TerminalReadLineOptions? options = null)
        => ReadLineAsync(options, CancellationToken.None).AsTask().GetAwaiter().GetResult();

    /// <summary>
    /// Reads a line of text from the terminal using terminal input events.
    /// </summary>
    /// <param name="options">Optional read options.</param>
    /// <param name="cancellationToken">A cancellation token.</param>
    /// <returns>The line read from the terminal, or <see langword="null"/> if input completed.</returns>
    public async ValueTask<string?> ReadLineAsync(TerminalReadLineOptions? options = null, CancellationToken cancellationToken = default)
    {
        options ??= new TerminalReadLineOptions();

        if (!Backend.IsInputRunning)
        {
            if (Options.ImplicitStartInput)
            {
                StartInput();
            }
            else
            {
                throw new InvalidOperationException("Terminal input is not running. Call Terminal.StartInput(...) first, or enable TerminalOptions.ImplicitStartInput.");
            }
        }

        using var _noEchoScope = SetInputEcho(enabled: false);
        using var _pasteScope = options.EnableBracketedPaste ? EnableBracketedPaste() : TerminalScope.Empty;
        using var _mouseScope = options.EnableMouseEditing && Capabilities.SupportsMouse && !Capabilities.IsOutputRedirected
            ? EnableMouse(TerminalMouseMode.Drag)
            : TerminalScope.Empty;

        if (!options.EnableEditing || Capabilities.IsOutputRedirected || !Capabilities.SupportsCursorPositionSet)
        {
            return await ReadLineSimpleAsync(options, cancellationToken).ConfigureAwait(false);
        }

        return await ReadLineEditorAsync(options, cancellationToken).ConfigureAwait(false);
    }

    private static TerminalInputOptions CopyInputOptions(TerminalInputOptions options)
    {
        return new TerminalInputOptions
        {
            EnableResizeEvents = options.EnableResizeEvents,
            EnableMouseEvents = options.EnableMouseEvents,
            MouseMode = options.MouseMode,
            TreatControlCAsInput = options.TreatControlCAsInput,
            CaptureCtrlC = options.CaptureCtrlC,
            CaptureCtrlBreak = options.CaptureCtrlBreak,
        };
    }

    private TerminalInputOptions CreateDefaultInputOptions()
    {
        var options = new TerminalInputOptions
        {
            EnableResizeEvents = true,
            EnableMouseEvents = true,
            MouseMode = TerminalMouseMode.Drag,
            TreatControlCAsInput = Options.TreatControlCAsInput,
            CaptureCtrlC = true,
            CaptureCtrlBreak = true,
        };

        NormalizeInputOptions(options);
        return options;
    }

    private static void NormalizeInputOptions(TerminalInputOptions options)
    {
        if (options.TreatControlCAsInput)
        {
            options.CaptureCtrlC = false;
            options.CaptureCtrlBreak = false;
        }
    }

    private bool ShouldPublishEvent(TerminalEvent ev)
    {
        var options = Volatile.Read(ref _inputOptions);
        if (options is null)
        {
            return true;
        }

        return ev switch
        {
            TerminalResizeEvent => options.EnableResizeEvents,
            TerminalMouseEvent mouse => ShouldPublishMouseEvent(mouse, options),
            _ => true,
        };
    }

    private static bool ShouldPublishMouseEvent(TerminalMouseEvent mouse, TerminalInputOptions options)
    {
        if (!options.EnableMouseEvents)
        {
            return false;
        }

        return options.MouseMode switch
        {
            TerminalMouseMode.Off => false,
            TerminalMouseMode.Clicks => mouse.Kind is TerminalMouseKind.Down or TerminalMouseKind.Up or TerminalMouseKind.Wheel,
            TerminalMouseMode.Drag => mouse.Kind is TerminalMouseKind.Down or TerminalMouseKind.Up or TerminalMouseKind.Wheel or TerminalMouseKind.Drag,
            _ => true,
        };
    }

    private async IAsyncEnumerable<TerminalEvent> ReadEventsFromDefaultAsync([EnumeratorCancellation] CancellationToken cancellationToken)
    {
        while (!cancellationToken.IsCancellationRequested)
        {
            TerminalEvent ev;
            try
            {
                ev = await ReadEventAsync(cancellationToken).ConfigureAwait(false);
            }
            catch (System.Threading.Channels.ChannelClosedException)
            {
                yield break;
            }

            yield return ev;
        }
    }

    private async ValueTask<TerminalEvent> ReadEventFilteredAsync(CancellationToken cancellationToken)
    {
        while (true)
        {
            var ev = await ReadDefaultEventAsync(cancellationToken).ConfigureAwait(false);
            if (ShouldPublishEvent(ev))
            {
                return ev;
            }
        }
    }

    private bool TryReadDefaultEvent(out TerminalEvent ev)
    {
        lock (_defaultInputQueueLock)
        {
            if (_defaultInputQueue.Count > 0)
            {
                ev = _defaultInputQueue.Dequeue();
                return true;
            }
        }

        return Backend.TryReadEvent(out ev);
    }

    private bool TryPeekDefaultEvent(out TerminalEvent ev)
    {
        while (true)
        {
            lock (_defaultInputQueueLock)
            {
                if (_defaultInputQueue.Count > 0)
                {
                    ev = _defaultInputQueue.Peek();
                    return true;
                }
            }

            if (!Backend.TryReadEvent(out ev))
            {
                ev = null!;
                return false;
            }

            if (!ShouldPublishEvent(ev))
            {
                continue;
            }

            lock (_defaultInputQueueLock)
            {
                _defaultInputQueue.Enqueue(ev);
            }
        }
    }

    private async ValueTask<TerminalEvent> ReadDefaultEventAsync(CancellationToken cancellationToken)
    {
        lock (_defaultInputQueueLock)
        {
            if (_defaultInputQueue.Count > 0)
            {
                return _defaultInputQueue.Dequeue();
            }
        }

        return await Backend.ReadEventAsync(cancellationToken).ConfigureAwait(false);
    }

    private void EchoKey(TerminalKeyEvent key)
    {
        if (key.Key == TerminalKey.Enter)
        {
            WriteLine();
            return;
        }

        if (key.Key == TerminalKey.Backspace)
        {
            WriteAtomic(static (TextWriter w) => w.Write("\b \b"));
            return;
        }

        if (key.Char is { } c && !char.IsControl(c))
        {
            WriteAtomic(w => w.Write(c));
        }
    }

    private void SetStyleCore(AnsiStyle style)
    {
        var next = style.ResolveMissingFrom(_style);
        lock (_outputLock)
        {
            if (_writerUnsafe is null)
            {
                throw new InvalidOperationException("Terminal is not initialized.");
            }

            if (Capabilities.AnsiEnabled)
            {
                _writerUnsafe.StyleTransition(_style, next);
            }
            else
            {
                ApplyColorFallback(next);
            }
        }

        _style = next;
    }

    private void ApplyColorFallback(AnsiStyle style)
    {
        // Best-effort fallback for environments where ANSI styling is disabled.
        // Only foreground/background colors are applied (decorations are ignored).
        if (style.Foreground is { } fg)
        {
            if (fg.Kind == AnsiColorKind.Default)
            {
                Backend.ResetColors();
            }
            else
            {
                Backend.SetForegroundColor(fg);
            }
        }

        if (style.Background is { } bg)
        {
            if (bg.Kind == AnsiColorKind.Default)
            {
                Backend.ResetColors();
            }
            else
            {
                Backend.SetBackgroundColor(bg);
            }
        }
    }

    private static ITerminalBackend CreateDefaultBackend()
    {
        if (Console.IsOutputRedirected && VirtualTerminalBackend.TryDetectKnownCapabilities(out var known))
        {
            return new VirtualTerminalBackend(capabilities: known);
        }

        if (OperatingSystem.IsWindows())
        {
            return new WindowsConsoleTerminalBackend();
        }

        if (OperatingSystem.IsLinux() || OperatingSystem.IsMacOS())
        {
            return new Backends.UnixTerminalBackend();
        }

        throw new PlatformNotSupportedException("This platform is not supported by XenoAtom.Terminal.");
    }

    [MethodImpl(MethodImplOptions.AggressiveInlining)]
    private void ThrowIfDisposed()
    {
        if (_isDisposed)
        {
            throw new ObjectDisposedException(nameof(TerminalInstance));
        }
    }

    /// <summary>
    /// Disposes this terminal instance and its backend (best effort).
    /// </summary>
    public void Dispose()
    {
        if (_isDisposed)
        {
            return;
        }

        _isDisposed = true;
        try
        {
            _backend?.Dispose();
        }
        catch
        {
            // Best-effort
        }
    }
}
