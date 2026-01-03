// Copyright (c) Alexandre Mutel. All rights reserved.
// Licensed under the BSD-Clause 2 license.
// See license.txt file in the project root for full license information.

using System.Diagnostics.CodeAnalysis;
using XenoAtom.Ansi;
using XenoAtom.Terminal.Internal;

namespace XenoAtom.Terminal.Backends;

/// <summary>
/// A virtual backend for tests and headless scenarios (including CI logs that support ANSI colors).
/// </summary>
public class VirtualTerminalBackend : ITerminalBackend
{
    private readonly TerminalEventBroadcaster _events = new();
    private readonly TextWriter? _providedOut;
    private readonly TextWriter? _providedError;
    private readonly bool _disposeWriters;
    private readonly object _clipboardLock = new();
    private string? _clipboardText;

    private TextWriter _out = TextWriter.Null;
    private TextWriter _error = TextWriter.Null;
    private AnsiWriter _ansi = null!;
    private readonly TerminalCapabilities _baseCapabilities;

    private TerminalSize _size;
    private bool _isDisposed;

    private int _altScreenRefCount;
    private int _hideCursorRefCount;
    private TerminalSize _bufferSize;
    private TerminalSize _largestWindowSize;
    private TerminalPosition _cursorPosition;
    private bool _cursorVisible = true;
    private string _title = string.Empty;
    private AnsiColor _foregroundColor = AnsiColor.Default;
    private AnsiColor _backgroundColor = AnsiColor.Default;

    /// <summary>
    /// Initializes a new instance of the <see cref="VirtualTerminalBackend"/> class.
    /// </summary>
    /// <param name="outWriter">Optional output writer. When <see langword="null"/>, uses <see cref="Console.Out"/> after initialization.</param>
    /// <param name="errorWriter">Optional error writer. When <see langword="null"/>, uses <see cref="Console.Error"/> after initialization.</param>
    /// <param name="initialSize">The initial terminal size. When default, uses 80x25.</param>
    /// <param name="capabilities">Optional capabilities to report. When <see langword="null"/>, a permissive virtual set is used.</param>
    /// <param name="disposeWriters">Whether to dispose provided writers when disposing this backend.</param>
    public VirtualTerminalBackend(TextWriter? outWriter = null, TextWriter? errorWriter = null, TerminalSize initialSize = default, TerminalCapabilities? capabilities = null, bool disposeWriters = false)
    {
        _providedOut = outWriter;
        _providedError = errorWriter;
        _disposeWriters = disposeWriters;

        _size = initialSize == default ? new TerminalSize(80, 25) : initialSize;
        _bufferSize = _size;
        _largestWindowSize = new TerminalSize(512, 512);
        _baseCapabilities = capabilities ?? new TerminalCapabilities
        {
            AnsiEnabled = true,
            ColorLevel = TerminalColorLevel.TrueColor,
            SupportsOsc8Links = true,
            SupportsAlternateScreen = true,
            SupportsCursorVisibility = true,
            SupportsMouse = true,
            SupportsBracketedPaste = false,
            SupportsRawMode = true,
            SupportsCursorPositionGet = true,
            SupportsCursorPositionSet = true,
            SupportsClipboard = true,
            SupportsTitleGet = true,
            SupportsTitleSet = true,
            SupportsWindowSize = true,
            SupportsWindowSizeSet = true,
            SupportsBufferSize = true,
            SupportsBufferSizeSet = true,
            SupportsBeep = true,
            IsOutputRedirected = false,
            IsInputRedirected = false,
            TerminalName = "Virtual",
        };
        Capabilities = _baseCapabilities;
    }

    /// <inheritdoc />
    public TerminalCapabilities Capabilities { get; private set; }

    /// <inheritdoc />
    public TextWriter Out => _out;

    /// <inheritdoc />
    public TextWriter Error => _error;

    /// <inheritdoc />
    public TerminalSize GetSize() => _size;

    /// <inheritdoc />
    public TerminalSize GetWindowSize() => _size;

    /// <inheritdoc />
    public TerminalSize GetBufferSize() => _bufferSize;

    /// <inheritdoc />
    public TerminalSize GetLargestWindowSize() => _largestWindowSize;

    /// <inheritdoc />
    public void SetWindowSize(TerminalSize size)
    {
        ThrowIfDisposed();
        SetSize(size, raiseEvent: true);
    }

    /// <inheritdoc />
    public void SetBufferSize(TerminalSize size)
    {
        ThrowIfDisposed();
        _bufferSize = size;
    }

    /// <inheritdoc />
    public bool TryGetCursorPosition(out TerminalPosition position)
    {
        ThrowIfDisposed();
        position = _cursorPosition;
        return true;
    }

    /// <inheritdoc />
    public void SetCursorPosition(TerminalPosition position)
    {
        ThrowIfDisposed();
        _cursorPosition = position;
    }

    /// <inheritdoc />
    public bool TryGetCursorVisible(out bool visible)
    {
        ThrowIfDisposed();
        visible = _cursorVisible;
        return true;
    }

    /// <inheritdoc />
    public void SetCursorVisible(bool visible)
    {
        ThrowIfDisposed();
        if (_cursorVisible == visible)
        {
            return;
        }

        _cursorVisible = visible;
        if (Capabilities.AnsiEnabled && !Capabilities.IsOutputRedirected)
        {
            _ansi.ShowCursor(visible);
        }
    }

    /// <inheritdoc />
    public bool TryGetTitle(out string title)
    {
        ThrowIfDisposed();
        title = _title;
        return true;
    }

    /// <inheritdoc />
    public void SetTitle(string title)
    {
        ThrowIfDisposed();
        ArgumentNullException.ThrowIfNull(title);
        _title = title;
    }

    /// <inheritdoc />
    public void SetForegroundColor(AnsiColor color)
    {
        ThrowIfDisposed();
        _foregroundColor = color;
    }

    /// <inheritdoc />
    public void SetBackgroundColor(AnsiColor color)
    {
        ThrowIfDisposed();
        _backgroundColor = color;
    }

    /// <inheritdoc />
    public void ResetColors()
    {
        ThrowIfDisposed();
        _foregroundColor = AnsiColor.Default;
        _backgroundColor = AnsiColor.Default;
    }

    /// <inheritdoc />
    public bool TryGetClipboardText([NotNullWhen(true)] out string? text)
    {
        ThrowIfDisposed();
        lock (_clipboardLock)
        {
            text = _clipboardText;
            return text is not null;
        }
    }

    /// <inheritdoc />
    public bool TrySetClipboardText(ReadOnlySpan<char> text)
    {
        ThrowIfDisposed();
        lock (_clipboardLock)
        {
            _clipboardText = text.Length == 0 ? string.Empty : new string(text);
            return true;
        }
    }

    /// <inheritdoc />
    public void Beep()
    {
        ThrowIfDisposed();
    }

    /// <inheritdoc />
    public void Initialize(TerminalOptions options)
    {
        ArgumentNullException.ThrowIfNull(options);
        ThrowIfDisposed();

        if (options.PreferUtf8Output)
        {
            try
            {
                var utf8NoBom = new System.Text.UTF8Encoding(encoderShouldEmitUTF8Identifier: false);
                Console.OutputEncoding = utf8NoBom;
                Console.InputEncoding = utf8NoBom;
            }
            catch
            {
                // Best-effort.
            }
        }

        _out = _providedOut ?? Console.Out;
        _error = _providedError ?? Console.Error;

        var ansiEnabled = _baseCapabilities.AnsiEnabled || options.ForceAnsi;
        var colorLevel = ansiEnabled ? _baseCapabilities.ColorLevel : TerminalColorLevel.None;
        if (ansiEnabled && colorLevel == TerminalColorLevel.None)
        {
            colorLevel = options.PreferredColorLevel;
        }

        if (options.RespectNoColor && !options.ForceAnsi && !string.IsNullOrEmpty(Environment.GetEnvironmentVariable("NO_COLOR")))
        {
            colorLevel = TerminalColorLevel.None;
        }

        Capabilities = new TerminalCapabilities
        {
            AnsiEnabled = ansiEnabled,
            ColorLevel = colorLevel,
            SupportsOsc8Links = ansiEnabled && _baseCapabilities.SupportsOsc8Links,
            SupportsAlternateScreen = _baseCapabilities.SupportsAlternateScreen,
            SupportsCursorVisibility = _baseCapabilities.SupportsCursorVisibility,
            SupportsMouse = _baseCapabilities.SupportsMouse,
            SupportsBracketedPaste = _baseCapabilities.SupportsBracketedPaste,
            SupportsRawMode = _baseCapabilities.SupportsRawMode,
            SupportsCursorPositionGet = _baseCapabilities.SupportsCursorPositionGet,
            SupportsCursorPositionSet = _baseCapabilities.SupportsCursorPositionSet,
            SupportsClipboard = _baseCapabilities.SupportsClipboard,
            SupportsTitleGet = _baseCapabilities.SupportsTitleGet,
            SupportsTitleSet = _baseCapabilities.SupportsTitleSet,
            SupportsWindowSize = _baseCapabilities.SupportsWindowSize,
            SupportsWindowSizeSet = _baseCapabilities.SupportsWindowSizeSet,
            SupportsBufferSize = _baseCapabilities.SupportsBufferSize,
            SupportsBufferSizeSet = _baseCapabilities.SupportsBufferSizeSet,
            SupportsBeep = _baseCapabilities.SupportsBeep,
            IsOutputRedirected = _baseCapabilities.IsOutputRedirected,
            IsInputRedirected = _baseCapabilities.IsInputRedirected,
            TerminalName = _baseCapabilities.TerminalName,
        };

        _ansi = new AnsiWriter(_out, TerminalAnsiCapabilities.Create(Capabilities, options));
    }

    /// <inheritdoc />
    public void Flush()
    {
        ThrowIfDisposed();
    }

    /// <inheritdoc />
    public void Clear(TerminalClearKind kind)
    {
        ThrowIfDisposed();
        if (!Capabilities.AnsiEnabled || Capabilities.IsOutputRedirected)
        {
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
        ThrowIfDisposed();
        return TerminalScope.Empty;
    }

    /// <inheritdoc />
    public TerminalScope UseAlternateScreen()
    {
        ThrowIfDisposed();
        if (!Capabilities.SupportsAlternateScreen || !Capabilities.AnsiEnabled || Capabilities.IsOutputRedirected)
        {
            return TerminalScope.Empty;
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
        ThrowIfDisposed();
        if (!Capabilities.SupportsCursorVisibility || !Capabilities.AnsiEnabled || Capabilities.IsOutputRedirected)
        {
            return TerminalScope.Empty;
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
        ThrowIfDisposed();
        return TerminalScope.Empty;
    }

    /// <inheritdoc />
    public TerminalScope EnableBracketedPaste()
    {
        ThrowIfDisposed();
        return TerminalScope.Empty;
    }

    /// <inheritdoc />
    public TerminalScope UseTitle(string title)
    {
        ThrowIfDisposed();
        ArgumentNullException.ThrowIfNull(title);
        return TerminalScope.Empty;
    }

    /// <inheritdoc />
    public TerminalScope SetInputEcho(bool enabled)
    {
        ThrowIfDisposed();
        return TerminalScope.Empty;
    }

    /// <inheritdoc />
    public bool IsInputRunning { get; private set; }

    /// <inheritdoc />
    public void StartInput(TerminalInputOptions options)
    {
        ThrowIfDisposed();
        ArgumentNullException.ThrowIfNull(options);
        IsInputRunning = true;
    }

    /// <inheritdoc />
    public Task StopInputAsync(CancellationToken cancellationToken)
    {
        ThrowIfDisposed();
        IsInputRunning = false;
        return Task.CompletedTask;
    }

    /// <inheritdoc />
    public bool TryReadEvent(out TerminalEvent ev) => _events.TryReadEvent(out ev);

    /// <inheritdoc />
    public ValueTask<TerminalEvent> ReadEventAsync(CancellationToken cancellationToken) => _events.ReadEventAsync(cancellationToken);

    /// <inheritdoc />
    public IAsyncEnumerable<TerminalEvent> ReadEventsAsync(CancellationToken cancellationToken) => _events.ReadEventsAsync(cancellationToken);

    /// <summary>
    /// Pushes a synthetic input event into the backend event stream.
    /// </summary>
    /// <param name="ev">The event to push.</param>
    public void PushEvent(TerminalEvent ev)
    {
        ThrowIfDisposed();
        ArgumentNullException.ThrowIfNull(ev);
        _events.Publish(ev);
    }

    /// <summary>
    /// Sets the current size and optionally publishes a <see cref="TerminalResizeEvent"/> if the size changed.
    /// </summary>
    /// <param name="size">The new size.</param>
    /// <param name="raiseEvent">Whether to publish a resize event when the size changes.</param>
    public void SetSize(TerminalSize size, bool raiseEvent = true)
    {
        ThrowIfDisposed();
        if (size.Equals(_size))
        {
            return;
        }

        _size = size;
        if (raiseEvent)
        {
            _events.Publish(new TerminalResizeEvent { Size = size });
        }
    }

    private void ThrowIfDisposed()
    {
        if (_isDisposed)
        {
            throw new ObjectDisposedException(nameof(VirtualTerminalBackend));
        }
    }

    /// <inheritdoc />
    public virtual void Dispose()
    {
        if (_isDisposed)
        {
            return;
        }

        _isDisposed = true;
        _events.Complete();
        if (_disposeWriters)
        {
            try { _out.Dispose(); } catch { }
            try { _error.Dispose(); } catch { }
        }
    }

    /// <summary>
    /// Tries to detect whether the current process is running in a known CI terminal that supports ANSI/VT colors.
    /// </summary>
    /// <param name="capabilities">The detected capabilities.</param>
    /// <returns><see langword="true"/> if a known terminal was detected; otherwise <see langword="false"/>.</returns>
    public static bool TryDetectKnownCapabilities([NotNullWhen(true)] out TerminalCapabilities? capabilities)
    {
        var ci = DetectKnownCiName();
        if (ci is null)
        {
            capabilities = null!;
            return false;
        }

        capabilities = new TerminalCapabilities
        {
            AnsiEnabled = true,
            ColorLevel = DetectColorLevelFromEnvironment(),
            SupportsOsc8Links = false,
            SupportsAlternateScreen = false,
            SupportsCursorVisibility = false,
            SupportsMouse = false,
            SupportsBracketedPaste = false,
            SupportsRawMode = false,
            SupportsCursorPositionGet = false,
            SupportsCursorPositionSet = false,
            SupportsTitleGet = false,
            SupportsTitleSet = false,
            SupportsWindowSize = false,
            SupportsWindowSizeSet = false,
            SupportsBufferSize = false,
            SupportsBufferSizeSet = false,
            SupportsBeep = false,
            IsOutputRedirected = true,
            IsInputRedirected = true,
            TerminalName = ci,
        };
        return true;
    }

    private static string? DetectKnownCiName()
    {
        static bool Has(string name) => !string.IsNullOrEmpty(Environment.GetEnvironmentVariable(name));
        static bool IsTrue(string name) => string.Equals(Environment.GetEnvironmentVariable(name), "true", StringComparison.OrdinalIgnoreCase);

        if (IsTrue("GITHUB_ACTIONS")) return "GitHubActions";
        if (Has("TF_BUILD")) return "AzurePipelines";
        if (Has("GITLAB_CI")) return "GitLab";
        if (Has("BITBUCKET_BUILD_NUMBER")) return "BitbucketPipelines";
        if (Has("CIRCLECI")) return "CircleCI";
        if (Has("TRAVIS")) return "TravisCI";
        if (Has("APPVEYOR")) return "AppVeyor";
        if (Has("BUILDKITE")) return "Buildkite";
        if (Has("TEAMCITY_VERSION")) return "TeamCity";
        if (Has("JENKINS_URL")) return "Jenkins";
        return null;
    }

    private static TerminalColorLevel DetectColorLevelFromEnvironment()
    {
        var term = Environment.GetEnvironmentVariable("TERM") ?? string.Empty;
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
}
