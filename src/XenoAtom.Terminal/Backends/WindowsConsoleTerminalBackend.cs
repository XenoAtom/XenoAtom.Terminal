// Copyright (c) Alexandre Mutel. All rights reserved.
// Licensed under the BSD-Clause 2 license.
// See license.txt file in the project root for full license information.

using System.Diagnostics.CodeAnalysis;
using System.Runtime.InteropServices;
using System.Runtime.Versioning;
using System.Text;
using XenoAtom.Ansi;
using XenoAtom.Terminal.Internal;
using XenoAtom.Terminal.Internal.Windows;
using static XenoAtom.Terminal.Internal.Windows.Win32Console;

namespace XenoAtom.Terminal.Backends;

/// <summary>
/// Windows Console backend using Win32 console APIs (ReadConsoleInputW).
/// </summary>
[SupportedOSPlatform("windows")]
internal sealed class WindowsConsoleTerminalBackend : ITerminalBackend
{
    private readonly Lock _inputLock = new();
    private readonly TerminalEventBroadcaster _events = new();
    private readonly bool[] _keyDown = new bool[256];

    private TerminalOptions? _options;
    private TerminalInputOptions? _inputOptions;
    private AnsiWriter _ansi = null!;

    private nint _inputHandle;
    private nint _outputHandle;

    private Task? _inputTask;
    private CancellationTokenSource? _inputCts;

    private bool _useVtInputDecoder;
    private readonly int[] _vtMouseModeCounts = new int[4];
    private TerminalMouseMode _vtMouseMode;

    private uint _savedInputMode;
    private bool _hasSavedInputMode;

    private uint _lastButtonState;

    private TerminalSize _lastPublishedResizeSize;

    private int _altScreenRefCount;

    private int _bracketedPasteRefCount;

    private ConsoleCancelEventHandler? _cancelKeyPressHandler;
    private bool _isCancelKeyPressHooked;

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
        SupportsClipboardGet = false,
        SupportsClipboardSet = false,
        SupportsOsc52Clipboard = false,
        SupportsTitleGet = false,
        SupportsTitleSet = false,
        SupportsWindowSize = false,
        SupportsWindowSizeSet = false,
        SupportsBufferSize = false,
        SupportsBufferSizeSet = false,
        SupportsBeep = false,
        IsOutputRedirected = true,
        IsInputRedirected = true,
        TerminalName = "Windows",
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

        // Capture writers after encoding is configured so the TextWriter uses the intended encoding.
        Out = Console.Out;
        Error = Console.Error;

        _inputHandle = Win32Console.GetStdHandle(Win32Console.STD_INPUT_HANDLE);
        _outputHandle = Win32Console.GetStdHandle(Win32Console.STD_OUTPUT_HANDLE);

        var isOutputRedirected = Console.IsOutputRedirected;
        var isInputRedirected = Console.IsInputRedirected;

        var ansiEnabled = options.ForceAnsi || (!isOutputRedirected && TryEnableVirtualTerminalOutput());

        _useVtInputDecoder = false;
        if (!isInputRedirected && !IsInvalidHandle(_inputHandle))
        {
            switch (options.WindowsVtInputDecoder)
            {
                case TerminalWindowsVtInputDecoderMode.Enabled:
                    _useVtInputDecoder = TryEnableVirtualTerminalInput();
                    break;
                case TerminalWindowsVtInputDecoderMode.Auto:
                    if (Win32Console.GetConsoleMode(_inputHandle, out var inputMode) && (inputMode & Win32Console.ENABLE_VIRTUAL_TERMINAL_INPUT) != 0)
                    {
                        _useVtInputDecoder = true;
                    }
                    break;
            }
        }

        var colorLevel = ansiEnabled ? options.PreferredColorLevel : TerminalColorLevel.None;
        if (options.RespectNoColor && !options.ForceAnsi && !string.IsNullOrEmpty(Environment.GetEnvironmentVariable("NO_COLOR")))
        {
            colorLevel = TerminalColorLevel.None;
        }

        Capabilities = new TerminalCapabilities
        {
            AnsiEnabled = ansiEnabled,
            ColorLevel = colorLevel,
            SupportsOsc8Links = ansiEnabled,
            SupportsAlternateScreen = ansiEnabled && !isOutputRedirected,
            SupportsCursorVisibility = !isOutputRedirected && !IsInvalidHandle(_outputHandle),
            SupportsMouse = !isInputRedirected,
            SupportsBracketedPaste = ansiEnabled && _useVtInputDecoder && !isOutputRedirected && !isInputRedirected,
            SupportsRawMode = !isInputRedirected,
            SupportsCursorPositionGet = !isOutputRedirected && !IsInvalidHandle(_outputHandle),
            SupportsCursorPositionSet = !isOutputRedirected && !IsInvalidHandle(_outputHandle),
            SupportsClipboardGet = true,
            SupportsClipboardSet = true,
            SupportsClipboard = true,
            SupportsTitleGet = true,
            SupportsTitleSet = true,
            SupportsWindowSize = !isOutputRedirected,
            SupportsWindowSizeSet = !isOutputRedirected,
            SupportsBufferSize = !isOutputRedirected,
            SupportsBufferSizeSet = !isOutputRedirected,
            SupportsBeep = true,
            IsOutputRedirected = isOutputRedirected,
            IsInputRedirected = isInputRedirected,
            TerminalName = DetectTerminalName(),
        };

        _ansi = new AnsiWriter(Out, TerminalAnsiCapabilities.Create(Capabilities, options));
    }

    private bool TryEnableVirtualTerminalInput()
    {
        if (IsInvalidHandle(_inputHandle))
        {
            return false;
        }

        if (!Win32Console.GetConsoleMode(_inputHandle, out var mode))
        {
            return false;
        }

        if ((mode & Win32Console.ENABLE_VIRTUAL_TERMINAL_INPUT) != 0)
        {
            return true;
        }

        var desired = mode | Win32Console.ENABLE_VIRTUAL_TERMINAL_INPUT;
        if (!Win32Console.SetConsoleMode(_inputHandle, desired))
        {
            return false;
        }

        return Win32Console.GetConsoleMode(_inputHandle, out var after) && (after & Win32Console.ENABLE_VIRTUAL_TERMINAL_INPUT) != 0;
    }

    private static string DetectTerminalName()
    {
        if (!string.IsNullOrEmpty(Environment.GetEnvironmentVariable("WT_SESSION")))
        {
            return "WindowsTerminal";
        }

        var termProgram = Environment.GetEnvironmentVariable("TERM_PROGRAM");
        if (!string.IsNullOrEmpty(termProgram))
        {
            if (string.Equals(termProgram, "vscode", StringComparison.OrdinalIgnoreCase))
            {
                return "VSCode";
            }

            return termProgram;
        }

        return "Windows";
    }

    /// <inheritdoc />
    public TerminalSize GetSize()
    {
        if (Capabilities.IsOutputRedirected || IsInvalidHandle(_outputHandle))
        {
            return default;
        }

        if (!Win32Console.GetConsoleScreenBufferInfo(_outputHandle, out var info))
        {
            return default;
        }

        var cols = info.srWindow.Right - info.srWindow.Left + 1;
        var rows = info.srWindow.Bottom - info.srWindow.Top + 1;
        return new TerminalSize(cols, rows);
    }

    /// <inheritdoc />
    public TerminalSize GetWindowSize() => GetSize();

    /// <inheritdoc />
    public TerminalSize GetBufferSize()
    {
        if (Capabilities.IsOutputRedirected)
        {
            return default;
        }

        try
        {
            return new TerminalSize(Console.BufferWidth, Console.BufferHeight);
        }
        catch
        {
            return default;
        }
    }

    /// <inheritdoc />
    public TerminalSize GetLargestWindowSize()
    {
        try
        {
            return new TerminalSize(Console.LargestWindowWidth, Console.LargestWindowHeight);
        }
        catch
        {
            return default;
        }
    }

    /// <inheritdoc />
    public void SetWindowSize(TerminalSize size)
    {
        if (Capabilities.IsOutputRedirected)
        {
            if (_options?.StrictMode == true)
            {
                throw new NotSupportedException("Window size cannot be changed when output is redirected.");
            }
            return;
        }

        try
        {
            Console.SetWindowSize(size.Columns, size.Rows);
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
    public void SetBufferSize(TerminalSize size)
    {
        if (Capabilities.IsOutputRedirected)
        {
            if (_options?.StrictMode == true)
            {
                throw new NotSupportedException("Buffer size cannot be changed when output is redirected.");
            }
            return;
        }

        try
        {
            Console.SetBufferSize(size.Columns, size.Rows);
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
    public bool TryGetCursorPosition(out TerminalPosition position)
    {
        if (Capabilities.IsOutputRedirected || IsInvalidHandle(_outputHandle))
        {
            position = default;
            return false;
        }

        if (!Win32Console.GetConsoleScreenBufferInfo(_outputHandle, out var info))
        {
            position = default;
            return false;
        }

        position = new TerminalPosition(info.dwCursorPosition.X, info.dwCursorPosition.Y);
        return true;
    }

    /// <inheritdoc />
    public void SetCursorPosition(TerminalPosition position)
    {
        if (Capabilities.IsOutputRedirected || IsInvalidHandle(_outputHandle))
        {
            if (_options?.StrictMode == true)
            {
                throw new NotSupportedException("Cursor position cannot be set when output is redirected.");
            }
            return;
        }

        var coord = new Win32Console.COORD((short)position.Column, (short)position.Row);
        if (!Win32Console.SetConsoleCursorPosition(_outputHandle, coord) && _options?.StrictMode == true)
        {
            throw new InvalidOperationException("Unable to set console cursor position.");
        }
    }

    /// <inheritdoc />
    public bool TryGetCursorVisible(out bool visible)
    {
        if (Capabilities.IsOutputRedirected || IsInvalidHandle(_outputHandle))
        {
            visible = default;
            return false;
        }

        if (!Win32Console.GetConsoleCursorInfo(_outputHandle, out var info))
        {
            visible = default;
            return false;
        }

        visible = info.bVisible;
        return true;
    }

    /// <inheritdoc />
    public void SetCursorVisible(bool visible)
    {
        if (Capabilities.IsOutputRedirected || IsInvalidHandle(_outputHandle))
        {
            if (_options?.StrictMode == true)
            {
                throw new NotSupportedException("Cursor visibility cannot be changed when output is redirected.");
            }
            return;
        }

        if (!Win32Console.GetConsoleCursorInfo(_outputHandle, out var previous))
        {
            if (_options?.StrictMode == true)
            {
                throw new InvalidOperationException("Unable to query cursor info.");
            }
            return;
        }

        var updated = new Win32Console.CONSOLE_CURSOR_INFO { dwSize = previous.dwSize, bVisible = visible };
        if (!Win32Console.SetConsoleCursorInfo(_outputHandle, updated) && _options?.StrictMode == true)
        {
            throw new InvalidOperationException("Unable to set cursor info.");
        }
    }

    /// <inheritdoc />
    public bool TryGetTitle(out string title)
    {
        try
        {
            title = Console.Title;
            return true;
        }
        catch
        {
            title = string.Empty;
            return false;
        }
    }

    /// <inheritdoc />
    public void SetTitle(string title)
    {
        ArgumentNullException.ThrowIfNull(title);
        try
        {
            Console.Title = title;
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
    public void SetForegroundColor(AnsiColor color)
    {
        if (Capabilities.IsOutputRedirected)
        {
            if (_options?.StrictMode == true)
            {
                throw new NotSupportedException("Foreground color cannot be changed when output is redirected.");
            }
            return;
        }

        try
        {
            if (color.Kind == AnsiColorKind.Default)
            {
                Console.ResetColor();
            }
            else if (TryMapAnsiColorToConsoleColor(color, out var mapped))
            {
                Console.ForegroundColor = mapped;
            }
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
    public void SetBackgroundColor(AnsiColor color)
    {
        if (Capabilities.IsOutputRedirected)
        {
            if (_options?.StrictMode == true)
            {
                throw new NotSupportedException("Background color cannot be changed when output is redirected.");
            }
            return;
        }

        try
        {
            if (color.Kind == AnsiColorKind.Default)
            {
                Console.ResetColor();
            }
            else if (TryMapAnsiColorToConsoleColor(color, out var mapped))
            {
                Console.BackgroundColor = mapped;
            }
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
    public void ResetColors()
    {
        if (Capabilities.IsOutputRedirected)
        {
            return;
        }

        try
        {
            Console.ResetColor();
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
    public bool TryGetClipboardText([NotNullWhen(true)] out string? text)
    {
        text = null;

        if (!TryOpenClipboard())
        {
            return false;
        }

        try
        {
            if (!Win32Clipboard.IsClipboardFormatAvailable(Win32Clipboard.CF_UNICODETEXT))
            {
                return false;
            }

            var handle = Win32Clipboard.GetClipboardData(Win32Clipboard.CF_UNICODETEXT);
            if (handle == IntPtr.Zero)
            {
                return false;
            }

            var ptr = Win32Clipboard.GlobalLock(handle);
            if (ptr == IntPtr.Zero)
            {
                return false;
            }

            try
            {
                text = Marshal.PtrToStringUni(ptr);
                return text is not null;
            }
            finally
            {
                Win32Clipboard.GlobalUnlock(handle);
            }
        }
        finally
        {
            Win32Clipboard.CloseClipboard();
        }
    }

    /// <inheritdoc />
    public unsafe bool TrySetClipboardText(ReadOnlySpan<char> text)
    {
        if (!TryOpenClipboard())
        {
            return false;
        }

        IntPtr hGlobal = IntPtr.Zero;
        try
        {
            if (!Win32Clipboard.EmptyClipboard())
            {
                return false;
            }

            var bytes = checked((nuint)(text.Length + 1) * 2);
            hGlobal = Win32Clipboard.GlobalAlloc(Win32Clipboard.GMEM_MOVEABLE | Win32Clipboard.GMEM_ZEROINIT, (UIntPtr)bytes);
            if (hGlobal == IntPtr.Zero)
            {
                return false;
            }

            var ptr = Win32Clipboard.GlobalLock(hGlobal);
            if (ptr == IntPtr.Zero)
            {
                Win32Clipboard.GlobalFree(hGlobal);
                hGlobal = IntPtr.Zero;
                return false;
            }

            try
            {
                fixed (char* src = text)
                {
                    Buffer.MemoryCopy(src, (void*)ptr, bytes, (nuint)text.Length * 2);
                }

                ((char*)ptr)[text.Length] = '\0';
            }
            finally
            {
                Win32Clipboard.GlobalUnlock(hGlobal);
            }

            var result = Win32Clipboard.SetClipboardData(Win32Clipboard.CF_UNICODETEXT, hGlobal);
            if (result == IntPtr.Zero)
            {
                Win32Clipboard.GlobalFree(hGlobal);
                hGlobal = IntPtr.Zero;
                return false;
            }

            // Ownership transferred to the system.
            hGlobal = IntPtr.Zero;
            return true;
        }
        finally
        {
            if (hGlobal != IntPtr.Zero)
            {
                Win32Clipboard.GlobalFree(hGlobal);
            }

            Win32Clipboard.CloseClipboard();
        }
    }

    private static bool TryOpenClipboard()
    {
        const int retries = 5;
        for (var i = 0; i < retries; i++)
        {
            if (Win32Clipboard.OpenClipboard(IntPtr.Zero))
            {
                return true;
            }
            Thread.Sleep(5);
        }

        return false;
    }

    private static bool TryMapAnsiColorToConsoleColor(AnsiColor color, out ConsoleColor consoleColor)
    {
        consoleColor = default;

        if (!color.TryDowngrade(AnsiColorLevel.Colors16, out var downgraded))
        {
            return false;
        }

        if (downgraded.Kind != AnsiColorKind.Basic16)
        {
            return false;
        }

        consoleColor = downgraded.Index switch
        {
            0 => ConsoleColor.Black,
            1 => ConsoleColor.DarkRed,
            2 => ConsoleColor.DarkGreen,
            3 => ConsoleColor.DarkYellow,
            4 => ConsoleColor.DarkBlue,
            5 => ConsoleColor.DarkMagenta,
            6 => ConsoleColor.DarkCyan,
            7 => ConsoleColor.Gray,
            8 => ConsoleColor.DarkGray,
            9 => ConsoleColor.Red,
            10 => ConsoleColor.Green,
            11 => ConsoleColor.Yellow,
            12 => ConsoleColor.Blue,
            13 => ConsoleColor.Magenta,
            14 => ConsoleColor.Cyan,
            _ => ConsoleColor.White,
        };

        return true;
    }

    /// <inheritdoc />
    public void Beep()
    {
        try
        {
            Console.Beep();
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
    public void Flush()
    {
        Out.Flush();
        Error.Flush();
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
    public TerminalScope UseRawMode(TerminalRawModeKind kind)
    {
        if (!Capabilities.SupportsRawMode || IsInvalidHandle(_inputHandle))
        {
            return NoOpScopeOrThrow();
        }

        if (!Win32Console.GetConsoleMode(_inputHandle, out var previousMode))
        {
            return NoOpScopeOrThrow();
        }

        var newMode = previousMode;
        newMode &= ~Win32Console.ENABLE_LINE_INPUT;
        newMode &= ~Win32Console.ENABLE_ECHO_INPUT;
        if (kind == TerminalRawModeKind.Raw)
        {
            newMode &= ~Win32Console.ENABLE_PROCESSED_INPUT;
        }

        if (!Win32Console.SetConsoleMode(_inputHandle, newMode))
        {
            return NoOpScopeOrThrow();
        }

        return TerminalScope.Create(() => Win32Console.SetConsoleMode(_inputHandle, previousMode));
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
        if (Capabilities.IsOutputRedirected || IsInvalidHandle(_outputHandle))
        {
            return NoOpScopeOrThrow();
        }

        if (!Win32Console.GetConsoleCursorInfo(_outputHandle, out var previous))
        {
            return NoOpScopeOrThrow();
        }

        var hidden = new Win32Console.CONSOLE_CURSOR_INFO { dwSize = previous.dwSize, bVisible = false };
        if (!Win32Console.SetConsoleCursorInfo(_outputHandle, hidden))
        {
            return NoOpScopeOrThrow();
        }

        return TerminalScope.Create(() => Win32Console.SetConsoleCursorInfo(_outputHandle, previous));
    }

    /// <inheritdoc />
    public TerminalScope EnableMouse(TerminalMouseMode mode)
    {
        if (IsInvalidHandle(_inputHandle))
        {
            return NoOpScopeOrThrow();
        }

        if (!Win32Console.GetConsoleMode(_inputHandle, out var previousMode))
        {
            return NoOpScopeOrThrow();
        }

        var newMode = previousMode;
        if (mode == TerminalMouseMode.Off)
        {
            newMode &= ~Win32Console.ENABLE_MOUSE_INPUT;
        }
        else
        {
            newMode |= Win32Console.ENABLE_MOUSE_INPUT;
            newMode |= Win32Console.ENABLE_EXTENDED_FLAGS;
            newMode &= ~Win32Console.ENABLE_QUICK_EDIT_MODE;
        }

        if (!Win32Console.SetConsoleMode(_inputHandle, newMode))
        {
            return NoOpScopeOrThrow();
        }

        if (!_useVtInputDecoder || Capabilities.IsOutputRedirected || !Capabilities.AnsiEnabled)
        {
            return TerminalScope.Create(() => Win32Console.SetConsoleMode(_inputHandle, previousMode));
        }

        var rank = ModeRank(mode);
        if (rank > 0)
        {
            lock (_inputLock)
            {
                _vtMouseModeCounts[rank]++;
                UpdateVtMouseMode();
            }
        }

        return TerminalScope.Create(() =>
        {
            try { Win32Console.SetConsoleMode(_inputHandle, previousMode); } catch { }
            if (rank > 0)
            {
                lock (_inputLock)
                {
                    if (_vtMouseModeCounts[rank] > 0)
                    {
                        _vtMouseModeCounts[rank]--;
                        UpdateVtMouseMode();
                    }
                }
            }
        });
    }

    private static int ModeRank(TerminalMouseMode mode) => mode switch
    {
        TerminalMouseMode.Off => 0,
        TerminalMouseMode.Clicks => 1,
        TerminalMouseMode.Drag => 2,
        TerminalMouseMode.Move => 3,
        _ => 0,
    };

    private void UpdateVtMouseMode()
    {
        var desired = TerminalMouseMode.Off;
        if (_vtMouseModeCounts[3] > 0) desired = TerminalMouseMode.Move;
        else if (_vtMouseModeCounts[2] > 0) desired = TerminalMouseMode.Drag;
        else if (_vtMouseModeCounts[1] > 0) desired = TerminalMouseMode.Clicks;

        if (desired == _vtMouseMode)
        {
            return;
        }

        _vtMouseMode = desired;
        SetVtMouseMode(desired);
    }

    private void SetVtMouseMode(TerminalMouseMode mode)
    {
        if (Capabilities.IsOutputRedirected || !Capabilities.AnsiEnabled)
        {
            return;
        }

        if (mode == TerminalMouseMode.Off)
        {
            _ansi.PrivateMode(1000, enabled: false)
                .PrivateMode(1002, enabled: false)
                .PrivateMode(1003, enabled: false)
                .PrivateMode(1006, enabled: false);
            return;
        }

        // Use SGR mouse events (1006) and select the reporting level.
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
                SetBracketedPasteEnabled(enabled: true);
            }

            return TerminalScope.Create(() =>
            {
                lock (_inputLock)
                {
                    if (Interlocked.Decrement(ref _bracketedPasteRefCount) == 0)
                    {
                        SetBracketedPasteEnabled(enabled: false);
                    }
                }
            });
        }
    }

    private void SetBracketedPasteEnabled(bool enabled)
    {
        if (Capabilities.IsOutputRedirected || !Capabilities.AnsiEnabled)
        {
            return;
        }

        _ansi.PrivateMode(2004, enabled);
        try { Out.Flush(); } catch { }
    }

    /// <inheritdoc />
    public TerminalScope UseTitle(string title)
    {
        ArgumentNullException.ThrowIfNull(title);
        try
        {
            var previous = Console.Title;
            Console.Title = title;
            return TerminalScope.Create(() =>
            {
                try
                {
                    Console.Title = previous;
                }
                catch
                {
                    // Best-effort.
                }
            });
        }
        catch
        {
            return NoOpScopeOrThrow();
        }
    }

    /// <inheritdoc />
    public TerminalScope SetInputEcho(bool enabled)
    {
        if (IsInvalidHandle(_inputHandle))
        {
            return NoOpScopeOrThrow();
        }

        if (!Win32Console.GetConsoleMode(_inputHandle, out var previousMode))
        {
            return NoOpScopeOrThrow();
        }

        var newMode = previousMode;
        if (enabled)
        {
            newMode |= Win32Console.ENABLE_ECHO_INPUT;
        }
        else
        {
            newMode &= ~Win32Console.ENABLE_ECHO_INPUT;
        }

        if (!Win32Console.SetConsoleMode(_inputHandle, newMode))
        {
            return NoOpScopeOrThrow();
        }

        return TerminalScope.Create(() => Win32Console.SetConsoleMode(_inputHandle, previousMode));
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
            else if (IsInvalidHandle(_inputHandle))
            {
                if (_options?.StrictMode == true)
                {
                    throw new NotSupportedException("Console input handle is not available.");
                }
                return;
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
        uint? restoreMode = null;

        lock (_inputLock)
        {
            task = _inputTask;
            cts = _inputCts;
            _inputTask = null;
            _inputCts = null;
            if (_hasSavedInputMode)
            {
                restoreMode = _savedInputMode;
                _hasSavedInputMode = false;
            }
        }

        if (restoreMode.HasValue && !IsInvalidHandle(_inputHandle))
        {
            Win32Console.SetConsoleMode(_inputHandle, restoreMode.Value);
        }

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
        if (IsInvalidHandle(_inputHandle))
        {
            return;
        }

        PrepareInputMode();

        using var vtDecoder = _useVtInputDecoder ? new VtInputDecoder() : null;

        const uint bufferLength = 32;
        var buffer = stackalloc Win32Console.INPUT_RECORD[(int)bufferLength];
        while (!cancellationToken.IsCancellationRequested)
        {
            var wait = Win32Console.WaitForSingleObject(_inputHandle, 50);
            if (wait == Win32Console.WAIT_TIMEOUT)
            {
                vtDecoder?.Decode(ReadOnlySpan<char>.Empty, isFinalChunk: true, _inputOptions, _events);
                continue;
            }

            if (wait == Win32Console.WAIT_FAILED)
            {
                Thread.Sleep(1);
                continue;
            }

            if (!Win32Console.ReadConsoleInputW(_inputHandle, buffer, bufferLength, out var read) || read == 0)
            {
                continue;
            }

            var readCount = (int)read;
            for (var i = 0; i < readCount; i++)
            {
                HandleInputRecord(buffer[i], vtDecoder);
            }
        }

        vtDecoder?.Decode(ReadOnlySpan<char>.Empty, isFinalChunk: true, _inputOptions, _events);
    }

    private void PrepareInputMode()
    {
        if (IsInvalidHandle(_inputHandle))
        {
            return;
        }

        if (!Win32Console.GetConsoleMode(_inputHandle, out var previousMode))
        {
            return;
        }

        lock (_inputLock)
        {
            if (!_hasSavedInputMode)
            {
                _savedInputMode = previousMode;
                _hasSavedInputMode = true;
            }
        }

        var desiredMode = previousMode;
        desiredMode &= ~Win32Console.ENABLE_LINE_INPUT;
        desiredMode &= ~Win32Console.ENABLE_ECHO_INPUT;
        if (_inputOptions?.TreatControlCAsInput == true)
        {
            desiredMode &= ~Win32Console.ENABLE_PROCESSED_INPUT;
        }
        desiredMode |= Win32Console.ENABLE_EXTENDED_FLAGS;
        desiredMode &= ~Win32Console.ENABLE_QUICK_EDIT_MODE;

        desiredMode |= Win32Console.ENABLE_WINDOW_INPUT;

        if (_inputOptions?.EnableMouseEvents == true)
        {
            if (_inputOptions.MouseMode != TerminalMouseMode.Off)
            {
                desiredMode |= Win32Console.ENABLE_MOUSE_INPUT;
            }
            else
            {
                desiredMode &= ~Win32Console.ENABLE_MOUSE_INPUT;
            }
        }
        else
        {
            desiredMode &= ~Win32Console.ENABLE_MOUSE_INPUT;
        }

        if (_useVtInputDecoder)
        {
            desiredMode |= Win32Console.ENABLE_VIRTUAL_TERMINAL_INPUT;
        }
        else
        {
            desiredMode &= ~Win32Console.ENABLE_VIRTUAL_TERMINAL_INPUT;
        }

        Win32Console.SetConsoleMode(_inputHandle, desiredMode);
    }

    private void HandleInputRecord(Win32Console.INPUT_RECORD record, VtInputDecoder? vtDecoder)
    {
        //Console.Write($"Event {record.EventType}");
        switch (record.EventType)
        {
            case InputEventType.KeyEvent:
                //Console.Write($" VirtualKeyCode: {record.Event.KeyEvent.wVirtualKeyCode} VirtualScanCode: {record.Event.KeyEvent.wVirtualScanCode}");
                HandleKeyEvent(record.Event.KeyEvent, vtDecoder);
                break;
            case InputEventType.MouseEvent:
                if (_inputOptions?.EnableMouseEvents == true)
                {
                    HandleMouseEvent(record.Event.MouseEvent);
                }
                break;
            case InputEventType.WindowBufferSizeEvent:
                HandleResize(record.Event.WindowBufferSizeEvent);
                break;
            case InputEventType.FocusEvent:
                // Not handled.
                break;
            case InputEventType.MenuEvent:
                // Not handled. (as not portable)
                break;
            default:
                // Unknown event type; ignore.
                break;
        }
        //Console.WriteLine();
    }

    private void HandleKeyEvent(Win32Console.KEY_EVENT_RECORD key, VtInputDecoder? vtDecoder)
    {
        var modifiers = DecodeModifiers(key.dwControlKeyState);
        var terminalKey = MapKey(key.wVirtualKeyCode);

        char? ch = key.UnicodeChar == '\0' ? null : key.UnicodeChar;
        modifiers = TerminalKeyModifierNormalization.NormalizeModifiersForPortableTextKeys(terminalKey, ch, modifiers);
        var repeat = Math.Max(1, (int)key.wRepeatCount);
        var vkIndex = key.wVirtualKeyCode & 0xFF;

        if (!key.bKeyDown)
        {
            _keyDown[vkIndex] = false;
            return;
        }

        if (TerminalWindowsKeyFiltering.IsStandaloneModifierKey(key.wVirtualKeyCode, ch))
        {
            // Unix terminals typically do not emit standalone modifier events (only the resulting modified key/text),
            // so skip these for portability.
            return;
        }

        var wasDown = _keyDown[vkIndex];
        _keyDown[vkIndex] = true;

        Span<char> vtOne = stackalloc char[1];
        var useVt = _useVtInputDecoder && vtDecoder is not null && ch.HasValue;
        if (useVt)
        {
            vtOne[0] = ch!.Value;
        }

        for (var i = 0; i < repeat; i++)
        {
            if (!wasDown && i == 0 && ch is not null)
            {
                // When ENABLE_PROCESSED_INPUT is disabled (raw), Ctrl+C may come through as a key event.
                if (_inputOptions?.TreatControlCAsInput != true && _inputOptions?.CaptureCtrlC == true && ch == '\x03' && modifiers.HasFlag(TerminalModifiers.Ctrl))
                {
                    _events.Publish(new TerminalSignalEvent { Kind = TerminalSignalKind.Interrupt });
                }

                // Ctrl+Break may surface as VK_CANCEL (0x03) depending on host.
                if (_inputOptions?.TreatControlCAsInput != true && _inputOptions?.CaptureCtrlBreak == true && key.wVirtualKeyCode == 0x03)
                {
                    _events.Publish(new TerminalSignalEvent { Kind = TerminalSignalKind.Break });
                }
            }

            if (useVt)
            {
                vtDecoder!.Decode(vtOne, isFinalChunk: false, _inputOptions, _events);
                wasDown = true;
                continue;
            }

            // Check for diacritic dead key combinations. (e.g. ' before an e to form é)
            // It can happen that a key event has no associated character and is not mapped to a known key
            // (e.g. ' and ` on a US international keyboard, first character will be combined with next, so we skip the first one)
            // As on other platforms these characters are not emitted in VT sequences,
            // we have to skip them on Windows (even if we could handle a preview for them).
            if (ch is not null || terminalKey != TerminalKey.Unknown)
            {
                _events.Publish(new TerminalKeyEvent
                {
                    Key = terminalKey,
                    Char = ch,
                    Modifiers = modifiers,
                });
                wasDown = true;
            }

            // Emit text events for printable characters. This is useful for UI-oriented consumption
            // (text input separate from physical keys).
            if (ch is { } c && !char.IsControl(c) && !char.IsSurrogate(c))
            {
                _events.Publish(new TerminalTextEvent { Text = c.ToString() });
            }
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
        if (!_isCancelKeyPressHooked)
        {
            return;
        }

        if (_cancelKeyPressHandler is not null)
        {
            Console.CancelKeyPress -= _cancelKeyPressHandler;
            _cancelKeyPressHandler = null;
        }

        _isCancelKeyPressHooked = false;
    }

    private void HandleMouseEvent(Win32Console.MOUSE_EVENT_RECORD mouse)
    {
        if (_useVtInputDecoder && _vtMouseMode != TerminalMouseMode.Off)
        {
            // When VT mouse reporting is enabled (Windows Terminal), prefer decoding VT mouse sequences.
            return;
        }

        var x = mouse.dwMousePosition.X;
        var y = mouse.dwMousePosition.Y;
        var modifiers = DecodeModifiers(mouse.dwControlKeyState);
        var mode = _inputOptions?.MouseMode ?? TerminalMouseMode.Drag;

        if (mode == TerminalMouseMode.Off)
        {
            return;
        }

        if ((mouse.dwEventFlags & (Win32Console.MOUSE_WHEELED | Win32Console.MOUSE_HWHEELED)) != 0)
        {
            var delta = unchecked((short)((mouse.dwButtonState >> 16) & 0xFFFF));
            _events.Publish(new TerminalMouseEvent
            {
                X = x,
                Y = y,
                Button = TerminalMouseButton.Wheel,
                Kind = TerminalMouseKind.Wheel,
                Modifiers = modifiers,
                WheelDelta = delta,
            });
            return;
        }

        if ((mouse.dwEventFlags & Win32Console.MOUSE_MOVED) != 0)
        {
            if (mode == TerminalMouseMode.Clicks)
            {
                return;
            }

            var pressedButton = DecodePressedButton(mouse.dwButtonState);
            var kind = pressedButton == TerminalMouseButton.None ? TerminalMouseKind.Move : TerminalMouseKind.Drag;
            if (mode == TerminalMouseMode.Drag && kind == TerminalMouseKind.Move)
            {
                return;
            }

            _events.Publish(new TerminalMouseEvent
            {
                X = x,
                Y = y,
                Button = pressedButton,
                Kind = kind,
                Modifiers = modifiers,
            });
            return;
        }

        var changed = _lastButtonState ^ mouse.dwButtonState;
        _lastButtonState = mouse.dwButtonState;
        if (changed == 0)
        {
            return;
        }

        var button = DecodeChangedButton(changed);
        if (button == TerminalMouseButton.None)
        {
            return;
        }

        var isDown = (mouse.dwButtonState & MapButtonMask(button)) != 0;
        _events.Publish(new TerminalMouseEvent
        {
            X = x,
            Y = y,
            Button = button,
            Kind = isDown ? TerminalMouseKind.Down : TerminalMouseKind.Up,
            Modifiers = modifiers,
        });
    }

    private void HandleResize(Win32Console.WINDOW_BUFFER_SIZE_RECORD resize)
    {
        var size = new TerminalSize(resize.dwSize.X, resize.dwSize.Y);
        if (size.Equals(_lastPublishedResizeSize))
        {
            return;
        }

        _lastPublishedResizeSize = size;
        _events.Publish(new TerminalResizeEvent { Size = size });
    }

    private TerminalScope NoOpScopeOrThrow()
    {
        if (_options?.StrictMode == true)
        {
            throw new NotSupportedException("The requested operation is not supported by this terminal backend.");
        }

        return TerminalScope.Empty;
    }

    private static TerminalKey MapKey(ushort vk) => vk switch
    {
        0x0D => TerminalKey.Enter,
        0x1B => TerminalKey.Escape,
        0x08 => TerminalKey.Backspace,
        0x09 => TerminalKey.Tab,
        0x20 => TerminalKey.Space,
        0x26 => TerminalKey.Up,
        0x28 => TerminalKey.Down,
        0x25 => TerminalKey.Left,
        0x27 => TerminalKey.Right,
        0x24 => TerminalKey.Home,
        0x23 => TerminalKey.End,
        0x21 => TerminalKey.PageUp,
        0x22 => TerminalKey.PageDown,
        0x2D => TerminalKey.Insert,
        0x2E => TerminalKey.Delete,
        0x70 => TerminalKey.F1,
        0x71 => TerminalKey.F2,
        0x72 => TerminalKey.F3,
        0x73 => TerminalKey.F4,
        0x74 => TerminalKey.F5,
        0x75 => TerminalKey.F6,
        0x76 => TerminalKey.F7,
        0x77 => TerminalKey.F8,
        0x78 => TerminalKey.F9,
        0x79 => TerminalKey.F10,
        0x7A => TerminalKey.F11,
        0x7B => TerminalKey.F12,
        _ => TerminalKey.Unknown,
    };

    private static TerminalModifiers DecodeModifiers(uint state)
    {
        TerminalModifiers mods = TerminalModifiers.None;
        const uint SHIFT_PRESSED = 0x0010;
        const uint LEFT_CTRL_PRESSED = 0x0008;
        const uint RIGHT_CTRL_PRESSED = 0x0004;
        const uint LEFT_ALT_PRESSED = 0x0002;
        const uint RIGHT_ALT_PRESSED = 0x0001;

        if ((state & SHIFT_PRESSED) != 0) mods |= TerminalModifiers.Shift;
        if ((state & (LEFT_CTRL_PRESSED | RIGHT_CTRL_PRESSED)) != 0) mods |= TerminalModifiers.Ctrl;
        if ((state & (LEFT_ALT_PRESSED | RIGHT_ALT_PRESSED)) != 0) mods |= TerminalModifiers.Alt;
        return mods;
    }


    private static TerminalMouseButton DecodeChangedButton(uint changedMask)
    {
        if ((changedMask & Win32Console.FROM_LEFT_1ST_BUTTON_PRESSED) != 0) return TerminalMouseButton.Left;
        if ((changedMask & Win32Console.RIGHTMOST_BUTTON_PRESSED) != 0) return TerminalMouseButton.Right;
        if ((changedMask & Win32Console.FROM_LEFT_2ND_BUTTON_PRESSED) != 0) return TerminalMouseButton.Middle;
        if ((changedMask & Win32Console.FROM_LEFT_3RD_BUTTON_PRESSED) != 0) return TerminalMouseButton.X1;
        if ((changedMask & Win32Console.FROM_LEFT_4TH_BUTTON_PRESSED) != 0) return TerminalMouseButton.X2;
        return TerminalMouseButton.None;
    }

    private static TerminalMouseButton DecodePressedButton(uint state)
    {
        if ((state & Win32Console.FROM_LEFT_1ST_BUTTON_PRESSED) != 0) return TerminalMouseButton.Left;
        if ((state & Win32Console.RIGHTMOST_BUTTON_PRESSED) != 0) return TerminalMouseButton.Right;
        if ((state & Win32Console.FROM_LEFT_2ND_BUTTON_PRESSED) != 0) return TerminalMouseButton.Middle;
        if ((state & Win32Console.FROM_LEFT_3RD_BUTTON_PRESSED) != 0) return TerminalMouseButton.X1;
        if ((state & Win32Console.FROM_LEFT_4TH_BUTTON_PRESSED) != 0) return TerminalMouseButton.X2;
        return TerminalMouseButton.None;
    }

    private static uint MapButtonMask(TerminalMouseButton button) => button switch
    {
        TerminalMouseButton.Left => Win32Console.FROM_LEFT_1ST_BUTTON_PRESSED,
        TerminalMouseButton.Right => Win32Console.RIGHTMOST_BUTTON_PRESSED,
        TerminalMouseButton.Middle => Win32Console.FROM_LEFT_2ND_BUTTON_PRESSED,
        TerminalMouseButton.X1 => Win32Console.FROM_LEFT_3RD_BUTTON_PRESSED,
        TerminalMouseButton.X2 => Win32Console.FROM_LEFT_4TH_BUTTON_PRESSED,
        _ => 0,
    };

    private static bool IsInvalidHandle(nint handle) => handle == 0 || handle == -1;

    private bool TryEnableVirtualTerminalOutput()
    {
        if (IsInvalidHandle(_outputHandle))
        {
            return false;
        }

        if (!Win32Console.GetConsoleMode(_outputHandle, out var mode))
        {
            return false;
        }

        var newMode = mode | Win32Console.ENABLE_VIRTUAL_TERMINAL_PROCESSING;
        return Win32Console.SetConsoleMode(_outputHandle, newMode);
    }

    /// <inheritdoc />
    public void Dispose()
    {
        StopInputAsync(CancellationToken.None).GetAwaiter().GetResult();
        RemoveCancelKeyPressHook();

        _events.Dispose();
    }
}
