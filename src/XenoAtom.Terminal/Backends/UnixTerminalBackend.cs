// Copyright (c) Alexandre Mutel. All rights reserved.
// Licensed under the BSD-Clause 2 license.
// See license.txt file in the project root for full license information.

using System.Runtime.Versioning;
using System.Diagnostics.CodeAnalysis;
using System.Text;
using XenoAtom.Ansi;
using XenoAtom.Terminal.Internal;
using XenoAtom.Terminal.Internal.Unix;

namespace XenoAtom.Terminal.Backends;

/// <summary>
/// Unix terminal backend using POSIX APIs (termios + poll/read) and VT sequences for output features.
/// </summary>
[SupportedOSPlatform("linux")]
[SupportedOSPlatform("macos")]
internal sealed class UnixTerminalBackend : ITerminalBackend
{
    private readonly Lock _inputLock = new();
    private readonly Lock _termiosLock = new();
    private readonly TerminalEventBroadcaster _events = new();
    private readonly Lock _cursorPositionLock = new();

    private readonly Action<AnsiCursorPosition> _cursorPositionReportHandler;
    private TaskCompletionSource<TerminalPosition>? _cursorPositionRequest;

    private TerminalOptions? _options;
    private TerminalInputOptions? _inputOptions;
    private AnsiWriter _ansi = null!;
    private UnixClipboard.Provider _clipboardProvider;

    private Task? _inputTask;
    private CancellationTokenSource? _inputCts;

    private bool _hasSavedInputTermios;
    private LibC.termios_linux _savedInputTermiosLinux;
    private LibC.termios_macos _savedInputTermiosMac;

    private TerminalSize _lastPublishedResizeSize;

    private int _altScreenRefCount;
    private int _hideCursorRefCount;
    private bool _cursorVisible = true;

    private TerminalMouseMode _mouseMode;
    private int _mouseRefCount;

    private bool _bracketedPasteEnabled;
    private int _bracketedPasteRefCount;

    private string _title = string.Empty;

    private ConsoleCancelEventHandler? _cancelKeyPressHandler;
    private bool _isCancelKeyPressHooked;

    public UnixTerminalBackend()
    {
        _cursorPositionReportHandler = OnCursorPositionReport;
    }

    /// <inheritdoc />
    public TerminalCapabilities Capabilities { get; private set; } = new TerminalCapabilities
    {
        AnsiEnabled = false,
        ColorLevel = TerminalColorLevel.None,
        SupportsOsc8Links = false,
        SupportsAlternateScreen = false,
        SupportsCursorVisibility = false,
        SupportsMouse = false,
        SupportsBracketedPaste = false,
        SupportsRawMode = false,
        SupportsCursorPositionGet = false,
        SupportsCursorPositionSet = false,
        SupportsClipboard = false,
        SupportsTitleGet = true,
        SupportsTitleSet = false,
        SupportsWindowSize = false,
        SupportsWindowSizeSet = false,
        SupportsBufferSize = false,
        SupportsBufferSizeSet = false,
        SupportsBeep = false,
        IsOutputRedirected = true,
        IsInputRedirected = true,
        TerminalName = "Unix",
    };

    /// <inheritdoc />
    public TextWriter Out { get; private set; } = TextWriter.Null;

    /// <inheritdoc />
    public TextWriter Error { get; private set; } = TextWriter.Null;

    /// <inheritdoc />
    public void Initialize(TerminalOptions options)
    {
        ArgumentNullException.ThrowIfNull(options);
        _options = options;

        if (options.PreferUtf8Output)
        {
            try
            {
                var utf8NoBom = new UTF8Encoding(encoderShouldEmitUTF8Identifier: false);
                Console.OutputEncoding = utf8NoBom;
                Console.InputEncoding = utf8NoBom;
            }
            catch
            {
                // Best-effort.
            }
        }

        Out = Console.Out;
        Error = Console.Error;

        var isOutputRedirected = Console.IsOutputRedirected || LibC.isatty(LibC.STDOUT_FILENO) != 1;
        var isInputRedirected = Console.IsInputRedirected || LibC.isatty(LibC.STDIN_FILENO) != 1;

        var term = Environment.GetEnvironmentVariable("TERM") ?? string.Empty;
        var termProgram = Environment.GetEnvironmentVariable("TERM_PROGRAM") ?? string.Empty;
        var terminalName = !string.IsNullOrEmpty(termProgram) ? termProgram : (!string.IsNullOrEmpty(term) ? term : "Unix");

        var ansiEnabled = options.ForceAnsi || (!isOutputRedirected && !string.Equals(term, "dumb", StringComparison.OrdinalIgnoreCase));

        var detectedColor = ansiEnabled ? DetectColorLevel(term) : TerminalColorLevel.None;
        var colorLevel = ansiEnabled ? MinColorLevel(detectedColor, options.PreferredColorLevel) : TerminalColorLevel.None;

        if (options.RespectNoColor && !options.ForceAnsi && !string.IsNullOrEmpty(Environment.GetEnvironmentVariable("NO_COLOR")))
        {
            colorLevel = TerminalColorLevel.None;
        }

        var supportsOsc8 = ansiEnabled && !string.Equals(term, "linux", StringComparison.OrdinalIgnoreCase);

        Capabilities = new TerminalCapabilities
        {
            AnsiEnabled = ansiEnabled,
            ColorLevel = colorLevel,
            SupportsOsc8Links = supportsOsc8,
            SupportsAlternateScreen = ansiEnabled && !isOutputRedirected,
            SupportsCursorVisibility = ansiEnabled && !isOutputRedirected,
            SupportsMouse = ansiEnabled && !isInputRedirected && !isOutputRedirected,
            SupportsBracketedPaste = ansiEnabled && !isOutputRedirected,
            SupportsRawMode = !isInputRedirected,
            SupportsCursorPositionGet = ansiEnabled && !isInputRedirected && !isOutputRedirected,
            SupportsCursorPositionSet = !isOutputRedirected,
            SupportsClipboard = UnixClipboard.TryDetectProvider(out _clipboardProvider),
            SupportsTitleGet = true,
            SupportsTitleSet = ansiEnabled && !isOutputRedirected,
            SupportsWindowSize = !isOutputRedirected,
            SupportsWindowSizeSet = false,
            SupportsBufferSize = !isOutputRedirected,
            SupportsBufferSizeSet = false,
            SupportsBeep = !isOutputRedirected,
            IsOutputRedirected = isOutputRedirected,
            IsInputRedirected = isInputRedirected,
            TerminalName = terminalName,
        };

        _title = string.Empty;
        _ansi = new AnsiWriter(Out, TerminalAnsiCapabilities.Create(Capabilities, options));
    }

    /// <inheritdoc />
    public TerminalSize GetSize() => GetWindowSize();

    /// <inheritdoc />
    public TerminalSize GetWindowSize()
    {
        if (Capabilities.IsOutputRedirected)
        {
            return default;
        }

        try
        {
            return new TerminalSize(Console.WindowWidth, Console.WindowHeight);
        }
        catch
        {
            return default;
        }
    }

    /// <inheritdoc />
    public TerminalSize GetBufferSize() => GetWindowSize();

    /// <inheritdoc />
    public TerminalSize GetLargestWindowSize() => GetWindowSize();

    /// <inheritdoc />
    public void SetWindowSize(TerminalSize size)
    {
        if (_options?.StrictMode == true)
        {
            throw new NotSupportedException("Window size cannot be changed on this backend.");
        }
    }

    /// <inheritdoc />
    public void SetBufferSize(TerminalSize size)
    {
        if (_options?.StrictMode == true)
        {
            throw new NotSupportedException("Buffer size cannot be changed on this backend.");
        }
    }

    /// <inheritdoc />
    public bool TryGetCursorPosition(out TerminalPosition position)
    {
        if (Capabilities.IsOutputRedirected || Capabilities.IsInputRedirected || !Capabilities.AnsiEnabled)
        {
            position = default;
            return false;
        }

        if (IsInputRunning)
        {
            return TryRequestCursorPositionFromInputLoop(out position);
        }

        return TryQueryCursorPositionDirect(out position);
    }

    private void OnCursorPositionReport(AnsiCursorPosition position)
    {
        TaskCompletionSource<TerminalPosition>? request = null;
        lock (_cursorPositionLock)
        {
            request = _cursorPositionRequest;
            _cursorPositionRequest = null;
        }

        if (request is null)
        {
            return;
        }

        // CPR is 1-based.
        request.TrySetResult(new TerminalPosition(position.Column - 1, position.Row - 1));
    }

    private bool TryRequestCursorPositionFromInputLoop(out TerminalPosition position)
    {
        position = default;

        var tcs = new TaskCompletionSource<TerminalPosition>(TaskCreationOptions.RunContinuationsAsynchronously);
        lock (_cursorPositionLock)
        {
            _cursorPositionRequest?.TrySetCanceled();
            _cursorPositionRequest = tcs;
        }

        // Device Status Report (DSR): request cursor position (CPR response).
        try
        {
            Out.Write("\x1b[6n");
            Out.Flush();
        }
        catch
        {
            lock (_cursorPositionLock)
            {
                if (ReferenceEquals(_cursorPositionRequest, tcs))
                {
                    _cursorPositionRequest = null;
                }
            }
            return false;
        }

        const int timeoutMs = 250;
        try
        {
            if (!tcs.Task.Wait(timeoutMs))
            {
                lock (_cursorPositionLock)
                {
                    if (ReferenceEquals(_cursorPositionRequest, tcs))
                    {
                        _cursorPositionRequest = null;
                    }
                }
                return false;
            }

            position = tcs.Task.Result;
            return true;
        }
        catch
        {
            lock (_cursorPositionLock)
            {
                if (ReferenceEquals(_cursorPositionRequest, tcs))
                {
                    _cursorPositionRequest = null;
                }
            }
            return false;
        }
    }

    private unsafe bool TryQueryCursorPositionDirect(out TerminalPosition position)
    {
        position = default;

        using var _raw = UseRawMode(TerminalRawModeKind.CBreak);

        try
        {
            Out.Write("\x1b[6n");
            Out.Flush();
        }
        catch
        {
            return false;
        }

        var pollFd = new LibC.PollFd { fd = LibC.STDIN_FILENO, events = LibC.POLLIN, revents = 0 };

        var state = 0;
        var row = 0;
        var col = 0;

        var deadline = Environment.TickCount64 + 250;
        Span<byte> bytes = stackalloc byte[128];
        while (Environment.TickCount64 < deadline)
        {
            var pollResult = LibC.poll(&pollFd, 1, 50);
            if (pollResult <= 0 || (pollFd.revents & LibC.POLLIN) == 0)
            {
                continue;
            }

            fixed (byte* buffer = bytes)
            {
                var read = LibC.read(LibC.STDIN_FILENO, buffer, (nuint)bytes.Length);
                if (read <= 0)
                {
                    return false;
                }

                for (var i = 0; i < (int)read; i++)
                {
                    var b = bytes[i];
                    switch (state)
                    {
                        case 0: // ESC
                            if (b == 0x1B) state = 1;
                            break;
                        case 1: // [
                            if (b == (byte)'[')
                            {
                                state = 2;
                                row = 0;
                                col = 0;
                            }
                            else
                            {
                                state = b == 0x1B ? 1 : 0;
                            }
                            break;
                        case 2: // row digits until ;
                            if (b is >= (byte)'0' and <= (byte)'9')
                            {
                                row = (row * 10) + (b - (byte)'0');
                            }
                            else if (b == (byte)';')
                            {
                                state = 3;
                            }
                            else
                            {
                                state = b == 0x1B ? 1 : 0;
                            }
                            break;
                        case 3: // col digits until R
                            if (b is >= (byte)'0' and <= (byte)'9')
                            {
                                col = (col * 10) + (b - (byte)'0');
                            }
                            else if (b == (byte)'R')
                            {
                                if (row >= 1 && col >= 1)
                                {
                                    position = new TerminalPosition(col - 1, row - 1);
                                    return true;
                                }
                                state = 0;
                            }
                            else
                            {
                                state = b == 0x1B ? 1 : 0;
                            }
                            break;
                    }
                }
            }
        }

        return false;
    }

    /// <inheritdoc />
    public void SetCursorPosition(TerminalPosition position)
    {
        if (Capabilities.IsOutputRedirected)
        {
            if (_options?.StrictMode == true)
            {
                throw new NotSupportedException("Cursor position cannot be changed when output is redirected.");
            }
            return;
        }

        try
        {
            Console.SetCursorPosition(position.Column, position.Row);
        }
        catch
        {
            if (_options?.StrictMode == true)
            {
                throw;
            }
        }
    }

    /// <inheritdoc />
    public bool TryGetCursorVisible(out bool visible)
    {
        visible = _cursorVisible;
        return !Capabilities.IsOutputRedirected;
    }

    /// <inheritdoc />
    public void SetCursorVisible(bool visible)
    {
        if (!Capabilities.SupportsCursorVisibility || Capabilities.IsOutputRedirected)
        {
            if (_options?.StrictMode == true)
            {
                throw new NotSupportedException("Cursor visibility cannot be changed on this backend.");
            }
            return;
        }

        _cursorVisible = visible;
        _ansi.ShowCursor(visible);
    }

    /// <inheritdoc />
    public bool TryGetTitle(out string title)
    {
        title = _title;
        return true;
    }

    /// <inheritdoc />
    public void SetTitle(string title)
    {
        ArgumentNullException.ThrowIfNull(title);
        _title = title;

        if (!Capabilities.SupportsTitleSet || Capabilities.IsOutputRedirected)
        {
            return;
        }

        _ansi.IconAndWindowTitle(title);
    }

    /// <inheritdoc />
    public void SetForegroundColor(AnsiColor color)
    {
        // Best-effort: only used when ANSI styling is disabled.
    }

    /// <inheritdoc />
    public void SetBackgroundColor(AnsiColor color)
    {
        // Best-effort: only used when ANSI styling is disabled.
    }

    /// <inheritdoc />
    public void ResetColors()
    {
        // Best-effort: only used when ANSI styling is disabled.
    }

    /// <inheritdoc />
    public bool TryGetClipboardText([NotNullWhen(true)] out string? text)
    {
        return UnixClipboard.TryGetText(_clipboardProvider, out text);
    }

    /// <inheritdoc />
    public bool TrySetClipboardText(ReadOnlySpan<char> text)
    {
        return UnixClipboard.TrySetText(_clipboardProvider, text);
    }

    /// <inheritdoc />
    public void Beep()
    {
        if (Capabilities.IsOutputRedirected)
        {
            return;
        }

        Out.Write('\a');
        Out.Flush();
    }

    /// <inheritdoc />
    public void Flush()
    {
        Out.Flush();
        Error.Flush();
    }

    /// <inheritdoc />
    public TerminalScope UseRawMode(TerminalRawModeKind kind)
    {
        if (!Capabilities.SupportsRawMode)
        {
            return NoOpScopeOrThrow();
        }

        lock (_termiosLock)
        {
            if (OperatingSystem.IsMacOS())
            {
                if (LibC.tcgetattr_macos(LibC.STDIN_FILENO, out var previous) != 0)
                {
                    return NoOpScopeOrThrow();
                }

                var desired = previous;
                if (kind == TerminalRawModeKind.Raw)
                {
                    LibC.cfmakeraw_macos(ref desired);
                }
                else
                {
                    desired.c_lflag &= ~(LibC.MACOS_ICANON | LibC.MACOS_ECHO);
                    SetCc(ref desired, LibC.MACOS_VMIN, 1);
                    SetCc(ref desired, LibC.MACOS_VTIME, 0);
                }

                if (LibC.tcsetattr_macos(LibC.STDIN_FILENO, LibC.TCSANOW, desired) != 0)
                {
                    return NoOpScopeOrThrow();
                }

                return TerminalScope.Create(() =>
                {
                    lock (_termiosLock)
                    {
                        LibC.tcsetattr_macos(LibC.STDIN_FILENO, LibC.TCSANOW, previous);
                    }
                });
            }

            if (LibC.tcgetattr_linux(LibC.STDIN_FILENO, out var previousLinux) != 0)
            {
                return NoOpScopeOrThrow();
            }

            var desiredLinux = previousLinux;
            if (kind == TerminalRawModeKind.Raw)
            {
                LibC.cfmakeraw_linux(ref desiredLinux);
            }
            else
            {
                desiredLinux.c_lflag &= ~(LibC.LINUX_ICANON | LibC.LINUX_ECHO);
                SetCc(ref desiredLinux, LibC.LINUX_VMIN, 1);
                SetCc(ref desiredLinux, LibC.LINUX_VTIME, 0);
            }

            if (LibC.tcsetattr_linux(LibC.STDIN_FILENO, LibC.TCSANOW, desiredLinux) != 0)
            {
                return NoOpScopeOrThrow();
            }

            return TerminalScope.Create(() =>
            {
                lock (_termiosLock)
                {
                    LibC.tcsetattr_linux(LibC.STDIN_FILENO, LibC.TCSANOW, previousLinux);
                }
            });
        }
    }

    /// <inheritdoc />
    public TerminalScope UseAlternateScreen()
    {
        if (!Capabilities.SupportsAlternateScreen || Capabilities.IsOutputRedirected)
        {
            return NoOpScopeOrThrow();
        }

        if (Interlocked.Increment(ref _altScreenRefCount) == 1)
        {
            _ansi.EnterAlternateScreen();
        }

        return TerminalScope.Create(() =>
        {
            if (Interlocked.Decrement(ref _altScreenRefCount) == 0)
            {
                _ansi.LeaveAlternateScreen();
            }
        });
    }

    /// <inheritdoc />
    public TerminalScope HideCursor()
    {
        if (!Capabilities.SupportsCursorVisibility || Capabilities.IsOutputRedirected)
        {
            return NoOpScopeOrThrow();
        }

        if (Interlocked.Increment(ref _hideCursorRefCount) == 1)
        {
            _cursorVisible = false;
            _ansi.ShowCursor(false);
        }

        return TerminalScope.Create(() =>
        {
            if (Interlocked.Decrement(ref _hideCursorRefCount) == 0)
            {
                _cursorVisible = true;
                _ansi.ShowCursor(true);
            }
        });
    }

    /// <inheritdoc />
    public TerminalScope EnableMouse(TerminalMouseMode mode)
    {
        if (!Capabilities.SupportsMouse || Capabilities.IsOutputRedirected)
        {
            return NoOpScopeOrThrow();
        }

        lock (_inputLock)
        {
            if (mode == TerminalMouseMode.Off)
            {
                return TerminalScope.Empty;
            }

            var shouldEnable = Interlocked.Increment(ref _mouseRefCount) == 1;
            if (shouldEnable || ModeRank(mode) > ModeRank(_mouseMode))
            {
                SetMouseMode(mode);
            }

            return TerminalScope.Create(() =>
            {
                lock (_inputLock)
                {
                    if (Interlocked.Decrement(ref _mouseRefCount) == 0)
                    {
                        SetMouseMode(TerminalMouseMode.Off);
                    }
                }
            });
        }
    }

    /// <inheritdoc />
    public TerminalScope EnableBracketedPaste()
    {
        if (!Capabilities.SupportsBracketedPaste || Capabilities.IsOutputRedirected)
        {
            return NoOpScopeOrThrow();
        }

        lock (_inputLock)
        {
            if (Interlocked.Increment(ref _bracketedPasteRefCount) == 1)
            {
                SetBracketedPasteEnabled(true);
            }

            return TerminalScope.Create(() =>
            {
                lock (_inputLock)
                {
                    if (Interlocked.Decrement(ref _bracketedPasteRefCount) == 0)
                    {
                        SetBracketedPasteEnabled(false);
                    }
                }
            });
        }
    }

    /// <inheritdoc />
    public TerminalScope UseTitle(string title)
    {
        ArgumentNullException.ThrowIfNull(title);
        if (!Capabilities.SupportsTitleSet || Capabilities.IsOutputRedirected)
        {
            return NoOpScopeOrThrow();
        }

        var previous = _title;
        SetTitle(title);
        return TerminalScope.Create(() => SetTitle(previous));
    }

    /// <inheritdoc />
    public TerminalScope SetInputEcho(bool enabled)
    {
        if (!Capabilities.SupportsRawMode)
        {
            return NoOpScopeOrThrow();
        }

        lock (_termiosLock)
        {
            if (OperatingSystem.IsMacOS())
            {
                if (LibC.tcgetattr_macos(LibC.STDIN_FILENO, out var previous) != 0)
                {
                    return NoOpScopeOrThrow();
                }

                var wasEnabled = (previous.c_lflag & LibC.MACOS_ECHO) != 0;
                if (wasEnabled == enabled)
                {
                    return TerminalScope.Empty;
                }

                var desired = previous;
                if (enabled) desired.c_lflag |= LibC.MACOS_ECHO;
                else desired.c_lflag &= ~LibC.MACOS_ECHO;

                if (LibC.tcsetattr_macos(LibC.STDIN_FILENO, LibC.TCSANOW, desired) != 0)
                {
                    return NoOpScopeOrThrow();
                }

                return TerminalScope.Create(() =>
                {
                    lock (_termiosLock)
                    {
                        // Only restore the ECHO flag to avoid undoing raw/cbreak mode set by the input loop.
                        if (LibC.tcgetattr_macos(LibC.STDIN_FILENO, out var current) != 0)
                        {
                            return;
                        }

                        var restore = current;
                        if (wasEnabled) restore.c_lflag |= LibC.MACOS_ECHO;
                        else restore.c_lflag &= ~LibC.MACOS_ECHO;
                        LibC.tcsetattr_macos(LibC.STDIN_FILENO, LibC.TCSANOW, restore);
                    }
                });
            }

            if (LibC.tcgetattr_linux(LibC.STDIN_FILENO, out var previousLinux) != 0)
            {
                return NoOpScopeOrThrow();
            }

            var wasEnabledLinux = (previousLinux.c_lflag & LibC.LINUX_ECHO) != 0;
            if (wasEnabledLinux == enabled)
            {
                return TerminalScope.Empty;
            }

            var desiredLinux = previousLinux;
            if (enabled) desiredLinux.c_lflag |= LibC.LINUX_ECHO;
            else desiredLinux.c_lflag &= ~LibC.LINUX_ECHO;

            if (LibC.tcsetattr_linux(LibC.STDIN_FILENO, LibC.TCSANOW, desiredLinux) != 0)
            {
                return NoOpScopeOrThrow();
            }

            return TerminalScope.Create(() =>
            {
                lock (_termiosLock)
                {
                    // Only restore the ECHO flag to avoid undoing raw/cbreak mode set by the input loop.
                    if (LibC.tcgetattr_linux(LibC.STDIN_FILENO, out var currentLinux) != 0)
                    {
                        return;
                    }

                    var restoreLinux = currentLinux;
                    if (wasEnabledLinux) restoreLinux.c_lflag |= LibC.LINUX_ECHO;
                    else restoreLinux.c_lflag &= ~LibC.LINUX_ECHO;
                    LibC.tcsetattr_linux(LibC.STDIN_FILENO, LibC.TCSANOW, restoreLinux);
                }
            });
        }
    }

    /// <inheritdoc />
    public void Clear(TerminalClearKind kind)
    {
        if (!Capabilities.AnsiEnabled || Capabilities.IsOutputRedirected)
        {
            if (!Capabilities.IsOutputRedirected && kind == TerminalClearKind.Screen)
            {
                try
                {
                    Console.Clear();
                }
                catch
                {
                    // Best-effort.
                }
            }
            return;
        }

        switch (kind)
        {
            case TerminalClearKind.Line:
                _ansi.EraseLine(2).Write("\r");
                break;
            case TerminalClearKind.Screen:
                _ansi.EraseDisplay(2).CursorPosition(1, 1);
                break;
            case TerminalClearKind.ScreenAndScrollback:
                _ansi.EraseDisplay(3).EraseDisplay(2).CursorPosition(1, 1);
                break;
            default:
                throw new ArgumentOutOfRangeException(nameof(kind));
        }
    }

    /// <inheritdoc />
    public bool IsInputRunning
    {
        get
        {
            lock (_inputLock)
            {
                return _inputTask is not null;
            }
        }
    }

    /// <inheritdoc />
    public void StartInput(TerminalInputOptions options)
    {
        ArgumentNullException.ThrowIfNull(options);
        var applyMode = false;

        lock (_inputLock)
        {
            if (_inputTask is not null)
            {
                _inputOptions = options;
                UpdateCancelKeyPressHook();
                applyMode = true;
            }
            else
            {
                _inputOptions = options;
                UpdateCancelKeyPressHook();
                _inputCts = new CancellationTokenSource();
                _inputTask = Task.Run(() => InputLoop(_inputCts.Token), _inputCts.Token);
            }
        }

        if (applyMode)
        {
            PrepareInputMode();
        }
    }

    /// <inheritdoc />
    public async Task StopInputAsync(CancellationToken cancellationToken)
    {
        Task? task;
        CancellationTokenSource? cts;

        lock (_inputLock)
        {
            task = _inputTask;
            cts = _inputCts;
            _inputTask = null;
            _inputCts = null;
        }

        RestoreSavedInputTermios();

        if (task is null)
        {
            return;
        }

        cts!.Cancel();
        try
        {
            await task.WaitAsync(cancellationToken).ConfigureAwait(false);
        }
        catch (OperationCanceledException)
        {
        }
        finally
        {
            cts.Dispose();
        }

        RemoveCancelKeyPressHook();
    }

    /// <inheritdoc />
    public bool TryReadEvent(out TerminalEvent ev) => _events.TryReadEvent(out ev);

    /// <inheritdoc />
    public ValueTask<TerminalEvent> ReadEventAsync(CancellationToken cancellationToken) => _events.ReadEventAsync(cancellationToken);

    /// <inheritdoc />
    public IAsyncEnumerable<TerminalEvent> ReadEventsAsync(CancellationToken cancellationToken) => _events.ReadEventsAsync(cancellationToken);

    private unsafe void InputLoop(CancellationToken cancellationToken)
    {
        PrepareInputMode();

        using var decoder = new VtInputDecoder();

        var bytes = new byte[4096];
        var chars = new char[4096];
        var utf8Decoder = Encoding.UTF8.GetDecoder();

        var pollFd = new LibC.PollFd { fd = LibC.STDIN_FILENO, events = LibC.POLLIN, revents = 0 };

        while (!cancellationToken.IsCancellationRequested)
        {
            if (_inputOptions?.EnableResizeEvents == true)
            {
                PublishResizeIfChanged();
            }

            var pollResult = LibC.poll(&pollFd, 1, 50);
            if (pollResult == 0)
            {
                // Flush pending partial sequences (notably ESC-as-a-key) after an idle period.
                decoder.Decode(ReadOnlySpan<char>.Empty, isFinalChunk: true, _inputOptions, _events, _cursorPositionReportHandler);
                continue;
            }

            if (pollResult < 0)
            {
                Thread.Sleep(1);
                continue;
            }

            if ((pollFd.revents & LibC.POLLIN) == 0)
            {
                continue;
            }

            fixed (byte* buffer = bytes)
            {
                var read = LibC.read(LibC.STDIN_FILENO, buffer, (nuint)bytes.Length);
                if (read <= 0)
                {
                    break;
                }

                var byteCount = (int)read;
                var byteOffset = 0;
                while (byteOffset < byteCount)
                {
                    utf8Decoder.Convert(bytes, byteOffset, byteCount - byteOffset, chars, 0, chars.Length, flush: false, out var bytesUsed, out var charsUsed, out _);
                    if (charsUsed > 0)
                    {
                        decoder.Decode(chars.AsSpan(0, charsUsed), isFinalChunk: false, _inputOptions, _events, _cursorPositionReportHandler);
                    }

                    if (bytesUsed <= 0)
                    {
                        break;
                    }
                    byteOffset += bytesUsed;
                }
            }
        }

        decoder.Decode(ReadOnlySpan<char>.Empty, isFinalChunk: true, _inputOptions, _events, _cursorPositionReportHandler);
    }

    private void PublishResizeIfChanged()
    {
        var size = GetWindowSize();
        if (size.Equals(_lastPublishedResizeSize))
        {
            return;
        }

        _lastPublishedResizeSize = size;
        _events.Publish(new TerminalResizeEvent { Size = size });
    }

    private void PrepareInputMode()
    {
        if (!Capabilities.SupportsRawMode)
        {
            return;
        }

        lock (_termiosLock)
        {
            if (OperatingSystem.IsMacOS())
            {
                if (LibC.tcgetattr_macos(LibC.STDIN_FILENO, out var current) != 0)
                {
                    return;
                }

                if (!_hasSavedInputTermios)
                {
                    _savedInputTermiosMac = current;
                    _hasSavedInputTermios = true;
                }

                var desired = current;
                desired.c_lflag &= ~(LibC.MACOS_ICANON | LibC.MACOS_ECHO);

                if (_inputOptions?.TreatControlCAsInput == true)
                {
                    desired.c_lflag &= ~LibC.MACOS_ISIG;
                }
                else
                {
                    desired.c_lflag |= LibC.MACOS_ISIG;
                }

                SetCc(ref desired, LibC.MACOS_VMIN, 1);
                SetCc(ref desired, LibC.MACOS_VTIME, 0);

                LibC.tcsetattr_macos(LibC.STDIN_FILENO, LibC.TCSANOW, desired);
                return;
            }

            if (LibC.tcgetattr_linux(LibC.STDIN_FILENO, out var currentLinux) != 0)
            {
                return;
            }

            if (!_hasSavedInputTermios)
            {
                _savedInputTermiosLinux = currentLinux;
                _hasSavedInputTermios = true;
            }

            var desiredLinux = currentLinux;
            desiredLinux.c_lflag &= ~(LibC.LINUX_ICANON | LibC.LINUX_ECHO);

            if (_inputOptions?.TreatControlCAsInput == true)
            {
                desiredLinux.c_lflag &= ~LibC.LINUX_ISIG;
            }
            else
            {
                desiredLinux.c_lflag |= LibC.LINUX_ISIG;
            }

            SetCc(ref desiredLinux, LibC.LINUX_VMIN, 1);
            SetCc(ref desiredLinux, LibC.LINUX_VTIME, 0);

            LibC.tcsetattr_linux(LibC.STDIN_FILENO, LibC.TCSANOW, desiredLinux);
        }
    }

    private void RestoreSavedInputTermios()
    {
        if (!_hasSavedInputTermios)
        {
            return;
        }

        lock (_termiosLock)
        {
            if (OperatingSystem.IsMacOS())
            {
                LibC.tcsetattr_macos(LibC.STDIN_FILENO, LibC.TCSANOW, _savedInputTermiosMac);
            }
            else
            {
                LibC.tcsetattr_linux(LibC.STDIN_FILENO, LibC.TCSANOW, _savedInputTermiosLinux);
            }

            _hasSavedInputTermios = false;
        }
    }

    private void UpdateCancelKeyPressHook()
    {
        var captureCtrlC = _inputOptions?.CaptureCtrlC == true;
        var captureCtrlBreak = _inputOptions?.CaptureCtrlBreak == true;
        var shouldHook = (captureCtrlC || captureCtrlBreak) && _inputOptions?.TreatControlCAsInput != true;

        if (!shouldHook)
        {
            RemoveCancelKeyPressHook();
            return;
        }

        if (_isCancelKeyPressHooked)
        {
            return;
        }

        _cancelKeyPressHandler = (_, e) =>
        {
            var options = _inputOptions;
            if (options is null)
            {
                return;
            }

            if (e.SpecialKey == ConsoleSpecialKey.ControlC && options.CaptureCtrlC)
            {
                e.Cancel = true;
                _events.Publish(new TerminalSignalEvent { Kind = TerminalSignalKind.Interrupt });
            }
            else if (e.SpecialKey == ConsoleSpecialKey.ControlBreak && options.CaptureCtrlBreak)
            {
                e.Cancel = true;
                _events.Publish(new TerminalSignalEvent { Kind = TerminalSignalKind.Break });
            }
        };

        Console.CancelKeyPress += _cancelKeyPressHandler;
        _isCancelKeyPressHooked = true;
    }

    private void RemoveCancelKeyPressHook()
    {
        if (!_isCancelKeyPressHooked || _cancelKeyPressHandler is null)
        {
            return;
        }

        try
        {
            Console.CancelKeyPress -= _cancelKeyPressHandler;
        }
        catch
        {
            // Best-effort.
        }
        finally
        {
            _isCancelKeyPressHooked = false;
            _cancelKeyPressHandler = null;
        }
    }

    private void SetMouseMode(TerminalMouseMode mode)
    {
        if (_mouseMode == mode)
        {
            return;
        }

        _mouseMode = mode;

        if (mode == TerminalMouseMode.Off)
        {
            _ansi.PrivateMode(1000, enabled: false)
                .PrivateMode(1002, enabled: false)
                .PrivateMode(1003, enabled: false)
                .PrivateMode(1006, enabled: false);
            try { Out.Flush(); } catch { }
            return;
        }

        _ansi.PrivateMode(1006, enabled: true);
        switch (mode)
        {
            case TerminalMouseMode.Clicks:
                _ansi.PrivateMode(1000, enabled: true)
                    .PrivateMode(1002, enabled: false)
                    .PrivateMode(1003, enabled: false);
                break;
            case TerminalMouseMode.Drag:
                _ansi.PrivateMode(1000, enabled: true)
                    .PrivateMode(1002, enabled: true)
                    .PrivateMode(1003, enabled: false);
                break;
            case TerminalMouseMode.Move:
                _ansi.PrivateMode(1000, enabled: true)
                    .PrivateMode(1002, enabled: false)
                    .PrivateMode(1003, enabled: true);
                break;
        }

        try { Out.Flush(); } catch { }
    }

    private static int ModeRank(TerminalMouseMode mode) => mode switch
    {
        TerminalMouseMode.Off => 0,
        TerminalMouseMode.Clicks => 1,
        TerminalMouseMode.Drag => 2,
        _ => 3,
    };

    private void SetBracketedPasteEnabled(bool enabled)
    {
        if (_bracketedPasteEnabled == enabled)
        {
            return;
        }

        _bracketedPasteEnabled = enabled;
        _ansi.PrivateMode(2004, enabled);
        try { Out.Flush(); } catch { }
    }

    private TerminalScope NoOpScopeOrThrow()
    {
        if (_options?.StrictMode == true)
        {
            throw new NotSupportedException("The requested operation is not supported by this terminal backend.");
        }

        return TerminalScope.Empty;
    }

    private static TerminalColorLevel DetectColorLevel(string term)
    {
        var colorTerm = Environment.GetEnvironmentVariable("COLORTERM") ?? string.Empty;
        if (colorTerm.Contains("truecolor", StringComparison.OrdinalIgnoreCase) || colorTerm.Contains("24bit", StringComparison.OrdinalIgnoreCase))
        {
            return TerminalColorLevel.TrueColor;
        }

        if (term.Contains("256color", StringComparison.OrdinalIgnoreCase))
        {
            return TerminalColorLevel.Color256;
        }

        return TerminalColorLevel.Color16;
    }

    private static TerminalColorLevel MinColorLevel(TerminalColorLevel a, TerminalColorLevel b)
    {
        static int Rank(TerminalColorLevel v) => v switch
        {
            TerminalColorLevel.None => 0,
            TerminalColorLevel.Color16 => 1,
            TerminalColorLevel.Color256 => 2,
            _ => 3,
        };

        return Rank(a) <= Rank(b) ? a : b;
    }

    private static unsafe void SetCc(ref LibC.termios_linux termios, int index, byte value)
    {
        if ((uint)index >= 32)
        {
            return;
        }

        fixed (byte* cc = termios.c_cc)
        {
            cc[index] = value;
        }
    }

    private static unsafe void SetCc(ref LibC.termios_macos termios, int index, byte value)
    {
        if ((uint)index >= 20)
        {
            return;
        }

        fixed (byte* cc = termios.c_cc)
        {
            cc[index] = value;
        }
    }

    /// <inheritdoc />
    public void Dispose()
    {
        StopInputAsync(CancellationToken.None).GetAwaiter().GetResult();
        RemoveCancelKeyPressHook();
        _events.Dispose();
    }
}
