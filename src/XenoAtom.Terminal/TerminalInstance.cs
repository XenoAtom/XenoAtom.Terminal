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
    [ThreadStatic]
    private static OutputCaptureContext? _outputCapture;

    private readonly Lock _outputLock = new();
    private readonly Lock _defaultInputQueueLock = new();
    private readonly Queue<TerminalEvent> _defaultInputQueue = new();

    private long _lastMouseDownTick = -1;
    private int _lastMouseDownX = -1;
    private int _lastMouseDownY = -1;
    private TerminalMouseButton _lastMouseDownButton = TerminalMouseButton.None;

    private bool _isDisposed;
    private bool _isInitialized;

    private ITerminalBackend? _backend;
    private TerminalOptions? _options;

    private TextWriter? _rawOut;
    private TextWriter? _rawError;

    private TextWriter? _out;
    private TextWriter? _error;

    private AnsiCapabilities? _ansiCapabilities;
    private AnsiWriter? _writerUnsafe;
    private AnsiMarkup? _markupUnsafe;
    private int _markupUnsafeCustomStylesRevision = -1;
    private Dictionary<string, AnsiStyle>? _markupUnsafeCustomStyles;
    private int _markupUnsafeCustomStylesCount;

    private Dictionary<string, AnsiStyle>? _customMarkupStyles;
    private int _customMarkupStylesRevision;

    private TerminalInputOptions? _inputOptions;
    private readonly TerminalCursor _cursor;
    private readonly TerminalWindow _window;
    private readonly TerminalClipboard _clipboard;
    private readonly TextReader _in;

    private AnsiStyle _style = AnsiStyle.Default;
    private string? _readLineKillBuffer;

    internal TerminalInstance()
    {
        _cursor = new TerminalCursor(this);
        _window = new TerminalWindow(this);
        _clipboard = new TerminalClipboard(this);
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
    /// Gets clipboard operations (best effort).
    /// </summary>
    public TerminalClipboard Clipboard => _clipboard;

    /// <summary>
    /// Gets or sets the current text style (best effort).
    /// </summary>
    /// <remarks>
    /// Terminal state is not reliably queryable. This property reflects style changes performed via Terminal APIs.
    /// If raw ANSI is written to the output outside of Terminal APIs, this state may become inaccurate.
    /// </remarks>
    public AnsiStyle Style
    {
        get => _outputCapture?.Style ?? _style;
        set => SetStyleCore(value);
    }

    /// <summary>
    /// Gets or sets a dictionary mapping custom markup style tokens (e.g. <c>primary</c>, <c>success</c>) to styles.
    /// </summary>
    /// <remarks>
    /// <para>
    /// This dictionary is used by <see cref="WriteMarkup(string)"/> and related methods to resolve custom markup tokens.
    /// </para>
    /// <para>
    /// If you mutate the dictionary in-place, call <see cref="NotifyMarkupStylesChanged"/> so cached markup renderers can be rebuilt.
    /// Alternatively, update styles via <see cref="SetMarkupStyle(string,AnsiStyle)"/> which invalidates automatically.
    /// </para>
    /// </remarks>
    public Dictionary<string, AnsiStyle>? MarkupStyles
    {
        get => _customMarkupStyles;
        set
        {
            ThrowIfDisposed();
            _customMarkupStyles = value;
            Interlocked.Increment(ref _customMarkupStylesRevision);
        }
    }

    /// <summary>
    /// Gets or sets the current foreground color (best effort).
    /// </summary>
    public AnsiColor ForegroundColor
    {
        get => (Style.Foreground ?? AnsiColor.Default);
        set => SetStyleCore(Style.WithForeground(value));
    }

    /// <summary>
    /// Gets or sets the current background color (best effort).
    /// </summary>
    public AnsiColor BackgroundColor
    {
        get => (Style.Background ?? AnsiColor.Default);
        set => SetStyleCore(Style.WithBackground(value));
    }

    /// <summary>
    /// Gets or sets the current decoration flags (best effort).
    /// </summary>
    public AnsiDecorations Decorations
    {
        get => Style.Decorations;
        set => SetStyleCore(Style.WithDecorations(value));
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
    /// Gets the cursor position asynchronously (0-based).
    /// </summary>
    public async ValueTask<TerminalPosition> GetCursorPositionAsync(CancellationToken cancellationToken = default)
    {
        if (await TryGetCursorPositionAsync(timeout: null, cancellationToken).ConfigureAwait(false) is { } position)
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
    /// Tries to get the cursor position asynchronously (0-based).
    /// </summary>
    public ValueTask<TerminalPosition?> TryGetCursorPositionAsync(TimeSpan? timeout = null, CancellationToken cancellationToken = default)
    {
        if ((OperatingSystem.IsLinux() || OperatingSystem.IsMacOS()) && Backend is Backends.UnixTerminalBackend unix)
        {
            var timeoutMs = timeout.HasValue ? (int)Math.Clamp(timeout.Value.TotalMilliseconds, 0, int.MaxValue) : 250;
            return unix.TryGetCursorPositionAsync(timeoutMs, cancellationToken);
        }

        return Backend.TryGetCursorPosition(out var position)
            ? ValueTask.FromResult<TerminalPosition?>(position)
            : ValueTask.FromResult<TerminalPosition?>(null);
    }

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
                GetAnsiWriterUnsafe().CursorPosition(position.Row + 1, position.Column + 1);
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
                GetAnsiWriterUnsafe().ShowCursor(visible);
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
                GetAnsiWriterUnsafe().SaveCursorPosition();
            }

            return TerminalScope.Create(() =>
            {
                lock (_outputLock)
                {
                    GetAnsiWriterUnsafe().RestoreCursorPosition();
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
        _writerUnsafe = new AnsiWriter(_rawOut, _ansiCapabilities);
        _markupUnsafe = CreateMarkupUnsafe(_writerUnsafe, _customMarkupStyles);
        _markupUnsafeCustomStylesRevision = Volatile.Read(ref _customMarkupStylesRevision);
        _markupUnsafeCustomStyles = _customMarkupStyles;
        _markupUnsafeCustomStylesCount = _customMarkupStyles?.Count ?? 0;

        _style = AnsiStyle.Default;

        _isInitialized = true;
    }

    /// <summary>
    /// Resets this instance for tests by disposing the backend (best effort) and clearing internal state.
    /// </summary>
    /// <remarks>
    /// This method is intended for test processes only.
    /// </remarks>
    internal void ResetForTests()
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
        _writerUnsafe = null;
        _markupUnsafe = null;
        _markupUnsafeCustomStylesRevision = -1;
        _markupUnsafeCustomStyles = null;
        _markupUnsafeCustomStylesCount = 0;
        _customMarkupStyles = null;
        _customMarkupStylesRevision = 0;
        _style = AnsiStyle.Default;
        lock (_defaultInputQueueLock)
        {
            _defaultInputQueue.Clear();
            ResetDoubleClickState();
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
            GetRawOutUnsafe().Write(text);
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
            GetRawOutUnsafe().Write(text);
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
            GetRawOutUnsafe().WriteLine(text);
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
            var outWriter = GetRawOutUnsafe();
            outWriter.Write(text);
            outWriter.WriteLine();
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
            GetMarkupUnsafe().Write(markup.AsSpan());
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
            GetMarkupUnsafe().Write(markup);
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
            GetMarkupUnsafe().Write(ref markup);
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
            GetMarkupUnsafe().Write(ref markup);
            GetRawOutUnsafe().WriteLine();
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

        if (!_isInitialized || _ansiCapabilities is null)
        {
            throw new InvalidOperationException("Terminal is not initialized.");
        }

        using var builder = new AnsiBuilder(textWithAnsiOrMarkup.Length + 16);
        var writer = new AnsiWriter(builder, _ansiCapabilities);
        var customStyles = _customMarkupStyles;
        var formatter = customStyles is null || customStyles.Count == 0
            ? new AnsiMarkup(writer)
            : new AnsiMarkup(writer, customStyles);
        formatter.Write(textWithAnsiOrMarkup.AsSpan());
        return AnsiText.GetVisibleWidth(builder.UnsafeAsSpan());
    }

    /// <summary>
    /// Updates or adds a custom markup style token and invalidates cached markup renderers.
    /// </summary>
    /// <param name="token">The token name (e.g. <c>primary</c>).</param>
    /// <param name="style">The style associated with the token.</param>
    /// <returns>This instance for fluent usage.</returns>
    public TerminalInstance SetMarkupStyle(string token, AnsiStyle style)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(token);

        var customStyles = _customMarkupStyles;
        if (customStyles is null)
        {
            customStyles = new Dictionary<string, AnsiStyle>(StringComparer.OrdinalIgnoreCase);
            _customMarkupStyles = customStyles;
        }

        customStyles[token] = style;
        Interlocked.Increment(ref _customMarkupStylesRevision);
        return this;
    }

    /// <summary>
    /// Removes a custom markup style token and invalidates cached markup renderers.
    /// </summary>
    /// <param name="token">The token name to remove.</param>
    /// <returns><see langword="true"/> if the token was removed; otherwise <see langword="false"/>.</returns>
    public bool RemoveMarkupStyle(string token)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(token);

        var customStyles = _customMarkupStyles;
        if (customStyles is null || !customStyles.Remove(token))
        {
            return false;
        }

        Interlocked.Increment(ref _customMarkupStylesRevision);
        return true;
    }

    /// <summary>
    /// Notifies this instance that <see cref="MarkupStyles"/> was mutated in-place and cached markup renderers should be rebuilt.
    /// </summary>
    public void NotifyMarkupStylesChanged()
    {
        ThrowIfDisposed();
        Interlocked.Increment(ref _customMarkupStylesRevision);
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
            write(GetAnsiWriterUnsafe());
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
            write(GetRawOutUnsafe());
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
    /// Enables raw/cbreak mode within a scope and restores the previous mode when disposed.
    /// </summary>
    /// <param name="kind">The raw mode kind (use <see cref="TerminalRawModeKind.CBreak"/> as the portable default for TUIs).</param>
    /// <returns>A scope that restores the previous mode on dispose.</returns>
    /// <remarks>
    /// <see cref="TerminalRawModeKind.CBreak"/> disables canonical input and echo. On Unix it also disables software flow control
    /// (so Ctrl+S/Ctrl+Q are delivered as keys) and disables CR-to-NL translation (so Enter typically yields <c>'\r'</c>).
    /// <see cref="TerminalRawModeKind.Raw"/> is more invasive (Unix <c>cfmakeraw</c>) and can change how newlines are handled.
    /// </remarks>
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
    /// Enables mouse input end-to-end: ensures mouse events are published by the input loop and enables mouse reporting on the backend.
    /// </summary>
    /// <param name="mode">The mouse reporting mode.</param>
    /// <returns>A scope that restores the previous state on dispose.</returns>
    public TerminalScope EnableMouseInput(TerminalMouseMode mode = TerminalMouseMode.Drag)
    {
        var inputScope = UseInputOptionsScope(options =>
        {
            options.EnableMouseEvents = true;
            if (ModeRank(options.MouseMode) < ModeRank(mode))
            {
                options.MouseMode = mode;
            }
        });

        var backendScope = EnableMouse(mode);

        if (inputScope.IsEmpty)
        {
            return backendScope;
        }

        if (backendScope.IsEmpty)
        {
            return inputScope;
        }

        return TerminalScope.Create(() =>
        {
            backendScope.Dispose();
            inputScope.Dispose();
        });
    }

    /// <summary>
    /// Enables bracketed paste within a scope and restores the previous state when disposed (best effort).
    /// </summary>
    /// <returns>A scope that restores the previous state on dispose.</returns>
    public TerminalScope EnableBracketedPaste() => UseBackendScope(b => b.EnableBracketedPaste());

    /// <summary>
    /// Enables bracketed paste end-to-end: ensures the input loop is running and enables bracketed paste on the backend.
    /// </summary>
    /// <returns>A scope that restores the previous state on dispose.</returns>
    public TerminalScope EnableBracketedPasteInput()
    {
        var inputScope = EnsureInputRunningScope();
        var backendScope = EnableBracketedPaste();

        if (inputScope.IsEmpty)
        {
            return backendScope;
        }

        if (backendScope.IsEmpty)
        {
            return inputScope;
        }

        return TerminalScope.Create(() =>
        {
            backendScope.Dispose();
            inputScope.Dispose();
        });
    }

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
            ResetDoubleClickState();
        }

        // Drain any pending events from the backend default stream so subsequent ReadKey/ReadLine
        // calls don't replay already-processed events (e.g. after consuming via ReadEventsAsync).
        while (Backend.TryReadEvent(out _))
        {
        }
    }

    private TerminalScope EnsureInputRunningScope()
    {
        if (Backend.IsInputRunning)
        {
            return TerminalScope.Empty;
        }

        StartInput();
        return TerminalScope.Create(() => StopInputAsync(CancellationToken.None).GetAwaiter().GetResult());
    }

    private TerminalScope UseInputOptionsScope(Action<TerminalInputOptions> configure)
    {
        ArgumentNullException.ThrowIfNull(configure);

        var wasRunning = Backend.IsInputRunning;
        var previous = Volatile.Read(ref _inputOptions);
        var updated = previous is null ? CreateDefaultInputOptions() : CopyInputOptions(previous);

        configure(updated);
        NormalizeInputOptions(updated);

        if (!wasRunning)
        {
            StartInput(updated);
            return TerminalScope.Create(() => StopInputAsync(CancellationToken.None).GetAwaiter().GetResult());
        }

        StartInput(updated);

        return TerminalScope.Create(() =>
        {
            if (Backend.IsInputRunning && previous is not null)
            {
                StartInput(previous);
            }
        });
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

        using var _inputOptionsScope = EnsureInputOptionsForReadLine(options);
        using var _noEchoScope = SetInputEcho(enabled: false);
        using var _pasteScope = options.EnableBracketedPaste ? EnableBracketedPaste() : TerminalScope.Empty;
        using var _mouseScope = options.EnableMouseEditing && Capabilities.SupportsMouse && !Capabilities.IsOutputRedirected
            ? EnableMouse(TerminalMouseMode.Move)
            : TerminalScope.Empty;
        if ((!_pasteScope.IsEmpty || !_mouseScope.IsEmpty) && !Capabilities.IsOutputRedirected)
        {
            Flush();
        }

        if (!options.EnableEditing || Capabilities.IsOutputRedirected || !Capabilities.SupportsCursorPositionSet)
        {
            return await ReadLineSimpleAsync(options, cancellationToken).ConfigureAwait(false);
        }

        return await ReadLineEditorAsync(options, cancellationToken).ConfigureAwait(false);
    }

    private TerminalScope EnsureInputOptionsForReadLine(TerminalReadLineOptions options)
    {
        if (!Backend.IsInputRunning)
        {
            return TerminalScope.Empty;
        }

        var needsMouse = (options.EnableMouseEditing || options.MouseHandler is not null) && Capabilities.SupportsMouse && !Capabilities.IsOutputRedirected;
        var needsCtrlAsInput = Capabilities.SupportsRawMode && !Capabilities.IsInputRedirected;

        if (!needsMouse && !needsCtrlAsInput)
        {
            return TerminalScope.Empty;
        }

        var current = Volatile.Read(ref _inputOptions);
        if (current is null)
        {
            return TerminalScope.Empty;
        }

        var updated = CopyInputOptions(current);
        var changed = false;

        if (needsMouse)
        {
            if (!updated.EnableMouseEvents)
            {
                updated.EnableMouseEvents = true;
                changed = true;
            }

            if (ModeRank(updated.MouseMode) < ModeRank(TerminalMouseMode.Move))
            {
                updated.MouseMode = TerminalMouseMode.Move;
                changed = true;
            }
        }

        if (needsCtrlAsInput && !updated.TreatControlCAsInput)
        {
            updated.TreatControlCAsInput = true;
            changed = true;
        }

        if (!changed)
        {
            return TerminalScope.Empty;
        }

        StartInput(updated);

        return TerminalScope.Create(() =>
        {
            if (Backend.IsInputRunning)
            {
                StartInput(current);
            }
        });
    }

    private static int ModeRank(TerminalMouseMode mode) => mode switch
    {
        TerminalMouseMode.Off => 0,
        TerminalMouseMode.Clicks => 1,
        TerminalMouseMode.Drag => 2,
        _ => 3,
    };

    private static TerminalInputOptions CopyInputOptions(TerminalInputOptions options)
    {
        return new TerminalInputOptions
        {
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
            EnableMouseEvents = false,
            MouseMode = TerminalMouseMode.Off,
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
            TerminalMouseMode.Clicks => mouse.Kind is TerminalMouseKind.Down or TerminalMouseKind.Up or TerminalMouseKind.DoubleClick or TerminalMouseKind.Wheel,
            TerminalMouseMode.Drag => mouse.Kind is TerminalMouseKind.Down or TerminalMouseKind.Up or TerminalMouseKind.DoubleClick or TerminalMouseKind.Wheel or TerminalMouseKind.Drag,
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

        if (!Backend.TryReadEvent(out ev))
        {
            return false;
        }

        EnqueueSynthesizedMouseEventsAfterReturn(ev);
        return true;
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
                EnqueueSynthesizedMouseEventsUnsafe(ev);
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

        var ev = await Backend.ReadEventAsync(cancellationToken).ConfigureAwait(false);
        EnqueueSynthesizedMouseEventsAfterReturn(ev);
        return ev;
    }

    private void EnqueueSynthesizedMouseEventsAfterReturn(TerminalEvent ev)
    {
        lock (_defaultInputQueueLock)
        {
            EnqueueSynthesizedMouseEventsUnsafe(ev);
        }
    }

    private void EnqueueSynthesizedMouseEventsUnsafe(TerminalEvent ev)
    {
        if (ev is not TerminalMouseEvent { Kind: TerminalMouseKind.Down } mouse)
        {
            return;
        }

        var options = Volatile.Read(ref _inputOptions);
        if (options is null || !options.EnableMouseEvents || options.MouseMode == TerminalMouseMode.Off)
        {
            return;
        }

        if (mouse.Button == TerminalMouseButton.None || mouse.Button == TerminalMouseButton.Wheel)
        {
            return;
        }

        const int doubleClickThresholdMs = 500;
        const int doubleClickMaxDistanceCells = 1;

        var now = Environment.TickCount64;
        var isDoubleClick =
            _lastMouseDownTick >= 0
            && unchecked(now - _lastMouseDownTick) <= doubleClickThresholdMs
            && mouse.Button == _lastMouseDownButton
            && Math.Abs(mouse.X - _lastMouseDownX) <= doubleClickMaxDistanceCells
            && Math.Abs(mouse.Y - _lastMouseDownY) <= doubleClickMaxDistanceCells;

        _lastMouseDownTick = now;
        _lastMouseDownX = mouse.X;
        _lastMouseDownY = mouse.Y;
        _lastMouseDownButton = mouse.Button;

        if (!isDoubleClick)
        {
            return;
        }

        _defaultInputQueue.Enqueue(new TerminalMouseEvent
        {
            X = mouse.X,
            Y = mouse.Y,
            Button = mouse.Button,
            Kind = TerminalMouseKind.DoubleClick,
            Modifiers = mouse.Modifiers,
            WheelDelta = 0,
        });
    }

    private void ResetDoubleClickState()
    {
        _lastMouseDownTick = -1;
        _lastMouseDownX = -1;
        _lastMouseDownY = -1;
        _lastMouseDownButton = TerminalMouseButton.None;
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
        var capture = _outputCapture;
        var previous = capture?.Style ?? _style;
        var next = style.ResolveMissingFrom(previous);
        lock (_outputLock)
        {
            if (_writerUnsafe is null || _ansiCapabilities is null)
            {
                throw new InvalidOperationException("Terminal is not initialized.");
            }

            if (Capabilities.AnsiEnabled)
            {
                GetAnsiWriterUnsafe().StyleTransition(previous, next);
            }
            else
            {
                ApplyColorFallback(next);
            }
        }

        if (capture is not null)
        {
            capture.Style = next;
        }
        else
        {
            _style = next;
        }
    }

    /// <summary>
    /// Captures all terminal output performed on the current thread into the provided <see cref="AnsiBuilder"/>.
    /// </summary>
    /// <remarks>
    /// This is primarily useful for scenarios that need to interleave regular terminal output with an inline
    /// "live" region (e.g. a terminal UI host), so that writes can be replayed above the live region in a single
    /// atomic operation.
    /// </remarks>
    /// <param name="builder">The builder receiving the captured ANSI/text output.</param>
    /// <returns>A scope that ends the capture when disposed.</returns>
    public TerminalOutputCaptureScope CaptureOutput(AnsiBuilder builder)
    {
        ArgumentNullException.ThrowIfNull(builder);
        if (!_isInitialized || _ansiCapabilities is null)
        {
            throw new InvalidOperationException("Terminal is not initialized.");
        }

        return new TerminalOutputCaptureScope(this, builder);
    }

    private TextWriter GetRawOutUnsafe()
        => _outputCapture?.RawOut ?? _rawOut ?? throw new InvalidOperationException("Terminal is not initialized.");

    private AnsiWriter GetAnsiWriterUnsafe()
        => _outputCapture?.Writer ?? _writerUnsafe ?? throw new InvalidOperationException("Terminal is not initialized.");

    private AnsiMarkup GetMarkupUnsafe()
    {
        if (!_isInitialized || _writerUnsafe is null)
        {
            throw new InvalidOperationException("Terminal is not initialized.");
        }

        var customStyles = _customMarkupStyles;
        var customStylesRevision = Volatile.Read(ref _customMarkupStylesRevision);
        var customStylesCount = customStyles?.Count ?? 0;

        var capture = _outputCapture;
        if (capture is not null)
        {
            return capture.GetMarkup(customStylesRevision, customStyles, customStylesCount);
        }

        if (_markupUnsafe is null
            || _markupUnsafeCustomStylesRevision != customStylesRevision
            || !ReferenceEquals(_markupUnsafeCustomStyles, customStyles)
            || _markupUnsafeCustomStylesCount != customStylesCount)
        {
            _markupUnsafe = CreateMarkupUnsafe(_writerUnsafe, customStyles);
            _markupUnsafeCustomStylesRevision = customStylesRevision;
            _markupUnsafeCustomStyles = customStyles;
            _markupUnsafeCustomStylesCount = customStylesCount;
        }

        return _markupUnsafe;
    }

    private static AnsiMarkup CreateMarkupUnsafe(AnsiWriter writer, Dictionary<string, AnsiStyle>? customStyles)
    {
        if (customStyles is null || customStyles.Count == 0)
        {
            return new AnsiMarkup(writer);
        }

        return new AnsiMarkup(writer, customStyles);
    }

    private sealed class OutputCaptureContext
    {
        private AnsiMarkup _markup;
        private int _customStylesRevision;
        private Dictionary<string, AnsiStyle>? _customStyles;
        private int _customStylesCount;

        public OutputCaptureContext(AnsiBuilder builder, AnsiCapabilities capabilities, AnsiStyle initialStyle, int customStylesRevision, Dictionary<string, AnsiStyle>? customStyles, int customStylesCount)
        {
            Builder = builder;
            RawOut = new AnsiBuilderTextWriter(builder);
            Writer = new AnsiWriter(builder, capabilities);

            _markup = CreateMarkupUnsafe(Writer, customStyles);
            _customStylesRevision = customStylesRevision;
            _customStyles = customStyles;
            _customStylesCount = customStylesCount;
            Style = initialStyle;
        }

        public AnsiBuilder Builder { get; }
        public TextWriter RawOut { get; }
        public AnsiWriter Writer { get; }
        public AnsiStyle Style { get; set; }

        public AnsiMarkup GetMarkup(int customStylesRevision, Dictionary<string, AnsiStyle>? customStyles, int customStylesCount)
        {
            if (_customStylesRevision != customStylesRevision
                || !ReferenceEquals(_customStyles, customStyles)
                || _customStylesCount != customStylesCount)
            {
                _markup = CreateMarkupUnsafe(Writer, customStyles);
                _customStylesRevision = customStylesRevision;
                _customStyles = customStyles;
                _customStylesCount = customStylesCount;
            }

            return _markup;
        }
    }

    /// <summary>
    /// A scope that captures terminal output for the current thread.
    /// </summary>
    public readonly struct TerminalOutputCaptureScope : IDisposable
    {
        private readonly OutputCaptureContext? _previous;
        private readonly TerminalInstance _terminal;

        internal TerminalOutputCaptureScope(TerminalInstance terminal, AnsiBuilder builder)
        {
            _terminal = terminal;
            _previous = _outputCapture;
            builder.Clear();

            var customStyles = terminal._customMarkupStyles;
            _outputCapture = new OutputCaptureContext(
                builder,
                terminal._ansiCapabilities!,
                terminal._style,
                Volatile.Read(ref terminal._customMarkupStylesRevision),
                customStyles,
                customStyles?.Count ?? 0);
        }

        /// <summary>
        /// Gets the builder receiving the captured output.
        /// </summary>
        public AnsiBuilder Builder => _outputCapture?.Builder ?? throw new InvalidOperationException("No active capture.");

        /// <summary>
        /// Gets a span over the captured content.
        /// </summary>
        public ReadOnlySpan<char> GetCapturedSpan() => Builder.UnsafeAsSpan();

        /// <summary>
        /// Gets a value indicating whether any output has been captured.
        /// </summary>
        public bool HasOutput => Builder.Length > 0;

        /// <summary>
        /// Ends the capture.
        /// </summary>
        public void Dispose()
        {
            _outputCapture = _previous;
        }
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
