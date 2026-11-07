// Copyright (c) Alexandre Mutel. All rights reserved.
// Licensed under the BSD-Clause 2 license.
// See license.txt file in the project root for full license information.

using System;
using System.IO;
using System.Threading;
using XenoAtom.Ansi;
using XenoAtom.Terminal.Backends;

namespace XenoAtom.Terminal;

/// <summary>
/// Provides a single static entry point for terminal output, input events, and mode scopes.
/// </summary>
public static partial class Terminal
{
    private static readonly Lock InitLock = new();
    private static TerminalInstance? _instance;
    private static int _processExitHooked;

    [ThreadStatic]
    private static TerminalInstance? _ambientInstance;

    [ThreadStatic]
    private static AmbientInstanceScope? _ambientScope;

    /// <summary>
    /// Gets the terminal instance targeted by the static <see cref="Terminal"/> members on the calling thread.
    /// </summary>
    /// <remarks>
    /// Returns the ambient instance of the calling thread when one is set with <see cref="UseInstance(TerminalInstance)"/>;
    /// otherwise returns the process-global instance, initializing it lazily with a default backend and options on first use.
    /// </remarks>
    public static TerminalInstance Instance
    {
        get
        {
            var ambient = _ambientInstance;
            if (ambient is not null)
            {
                return ambient;
            }

            EnsureInitialized();
            return _instance!;
        }
    }

    /// <summary>
    /// Convenience alias for <see cref="Instance"/> for fluent usage (e.g. <c>Terminal.T.Foreground(...).Write(...)</c>).
    /// </summary>
    /// <remarks>
    /// Like <see cref="Instance"/>, returns the ambient instance of the calling thread when one is set, otherwise the process-global instance.
    /// </remarks>
    public static TerminalInstance T => Instance;

    /// <summary>
    /// Gets a value indicating whether the process-global terminal instance has been initialized.
    /// </summary>
    /// <remarks>
    /// The ambient instance set with <see cref="UseInstance(TerminalInstance)"/> is not taken into account.
    /// </remarks>
    public static bool IsInitialized => Volatile.Read(ref _instance) is { IsInitialized: true };

    /// <inheritdoc cref="TerminalInstance.Backend" />
    public static ITerminalBackend Backend => Instance.Backend;

    /// <inheritdoc cref="TerminalInstance.Options" />
    public static TerminalOptions Options => Instance.Options;

    /// <inheritdoc cref="TerminalInstance.Capabilities" />
    public static TerminalCapabilities Capabilities => Instance.Capabilities;

    /// <inheritdoc cref="TerminalInstance.IsInteractive" />
    public static bool IsInteractive => Instance.IsInteractive;

    /// <inheritdoc cref="TerminalInstance.Size" />
    public static TerminalSize Size => Instance.Size;

    /// <inheritdoc cref="TerminalInstance.In" />
    public static TextReader In => Instance.In;

    /// <inheritdoc cref="TerminalInstance.Out" />
    public static TextWriter Out => Instance.Out;

    /// <inheritdoc cref="TerminalInstance.Error" />
    public static TextWriter Error => Instance.Error;

    /// <inheritdoc cref="TerminalInstance.Cursor" />
    public static TerminalCursor Cursor => Instance.Cursor;

    /// <inheritdoc cref="TerminalInstance.Window" />
    public static TerminalWindow Window => Instance.Window;

    /// <inheritdoc cref="TerminalInstance.Clipboard" />
    public static TerminalClipboard Clipboard => Instance.Clipboard;

    /// <inheritdoc cref="TerminalInstance.Graphics" />
    public static TerminalGraphicsService Graphics => Instance.Graphics;

    /// <inheritdoc cref="TerminalInstance.Title" />
    public static string Title
    {
        get => Instance.Title;
        set => Instance.Title = value;
    }

    /// <inheritdoc cref="TerminalInstance.Style" />
    public static AnsiStyle Style
    {
        get => Instance.Style;
        set => Instance.Style = value;
    }

    /// <inheritdoc cref="TerminalInstance.ForegroundColor" />
    public static AnsiColor ForegroundColor
    {
        get => Instance.ForegroundColor;
        set => Instance.ForegroundColor = value;
    }

    /// <inheritdoc cref="TerminalInstance.BackgroundColor" />
    public static AnsiColor BackgroundColor
    {
        get => Instance.BackgroundColor;
        set => Instance.BackgroundColor = value;
    }

    /// <inheritdoc cref="TerminalInstance.Decorations" />
    public static AnsiDecorations Decorations
    {
        get => Instance.Decorations;
        set => Instance.Decorations = value;
    }

    /// <inheritdoc cref="TerminalInstance.MarkupStyles" />
    public static Dictionary<string, AnsiStyle>? MarkupStyles
    {
        get => Instance.MarkupStyles;
        set => Instance.MarkupStyles = value;
    }

    /// <inheritdoc cref="TerminalInstance.NotifyMarkupStylesChanged" />
    public static void NotifyMarkupStylesChanged() => Instance.NotifyMarkupStylesChanged();

    /// <inheritdoc cref="TerminalInstance.SetMarkupStyle(string,AnsiStyle)" />
    public static TerminalInstance SetMarkupStyle(string token, AnsiStyle style) => Instance.SetMarkupStyle(token, style);

    /// <inheritdoc cref="TerminalInstance.RemoveMarkupStyle(string)" />
    public static bool RemoveMarkupStyle(string token) => Instance.RemoveMarkupStyle(token);

    /// <inheritdoc cref="TerminalInstance.Beep" />
    public static void Beep() => Instance.Beep();

    /// <inheritdoc cref="TerminalInstance.CursorLeft" />
    public static int CursorLeft
    {
        get => Instance.CursorLeft;
        set => Instance.CursorLeft = value;
    }

    /// <inheritdoc cref="TerminalInstance.CursorTop" />
    public static int CursorTop
    {
        get => Instance.CursorTop;
        set => Instance.CursorTop = value;
    }

    /// <inheritdoc cref="TerminalInstance.SetCursorPosition(int,int)" />
    public static void SetCursorPosition(int left, int top) => Instance.SetCursorPosition(left, top);

    /// <inheritdoc cref="TerminalInstance.GetCursorPosition" />
    public static TerminalPosition GetCursorPosition() => Instance.GetCursorPosition();

    /// <inheritdoc cref="TerminalInstance.TryGetCursorPosition" />
    public static bool TryGetCursorPosition(out TerminalPosition position) => Instance.TryGetCursorPosition(out position);

    /// <inheritdoc cref="TerminalInstance.UseCursorPosition()" />
    public static TerminalScope UseCursorPosition() => Instance.UseCursorPosition();

    /// <inheritdoc cref="TerminalInstance.UseCursorPosition(TerminalPosition)" />
    public static TerminalScope UseCursorPosition(TerminalPosition position) => Instance.UseCursorPosition(position);

    /// <inheritdoc cref="TerminalInstance.KeyAvailable" />
    public static bool KeyAvailable => Instance.KeyAvailable;

    /// <inheritdoc cref="TerminalInstance.TreatControlCAsInput" />
    public static bool TreatControlCAsInput
    {
        get => Instance.TreatControlCAsInput;
        set => Instance.TreatControlCAsInput = value;
    }

    /// <inheritdoc cref="TerminalInstance.ReadKey(bool)" />
    public static TerminalKeyInfo ReadKey(bool intercept = false) => Instance.ReadKey(intercept);

    /// <inheritdoc cref="TerminalInstance.ReadKey(TerminalReadKeyOptions?)" />
    public static TerminalKeyInfo ReadKey(TerminalReadKeyOptions? options = null) => Instance.ReadKey(options);

    /// <inheritdoc cref="TerminalInstance.ReadKeyAsync(bool, CancellationToken)" />
    public static ValueTask<TerminalKeyInfo> ReadKeyAsync(bool intercept, CancellationToken cancellationToken = default)
        => Instance.ReadKeyAsync(intercept, cancellationToken);

    /// <inheritdoc cref="TerminalInstance.ReadKeyAsync(TerminalReadKeyOptions?, CancellationToken)" />
    public static ValueTask<TerminalKeyInfo> ReadKeyAsync(TerminalReadKeyOptions? options = null, CancellationToken cancellationToken = default)
        => Instance.ReadKeyAsync(options, cancellationToken);

    /// <inheritdoc cref="TerminalInstance.WindowWidth" />
    public static int WindowWidth
    {
        get => Instance.WindowWidth;
        set => Instance.WindowWidth = value;
    }

    /// <inheritdoc cref="TerminalInstance.WindowHeight" />
    public static int WindowHeight
    {
        get => Instance.WindowHeight;
        set => Instance.WindowHeight = value;
    }

    /// <inheritdoc cref="TerminalInstance.BufferWidth" />
    public static int BufferWidth
    {
        get => Instance.BufferWidth;
        set => Instance.BufferWidth = value;
    }

    /// <inheritdoc cref="TerminalInstance.BufferHeight" />
    public static int BufferHeight
    {
        get => Instance.BufferHeight;
        set => Instance.BufferHeight = value;
    }

    /// <inheritdoc cref="TerminalInstance.LargestWindowWidth" />
    public static int LargestWindowWidth => Instance.LargestWindowWidth;

    /// <inheritdoc cref="TerminalInstance.LargestWindowHeight" />
    public static int LargestWindowHeight => Instance.LargestWindowHeight;

    /// <summary>
    /// Initializes the global terminal instance.
    /// </summary>
    /// <param name="backend">An optional backend to use. When <see langword="null"/>, a default platform backend is selected.</param>
    /// <param name="options">An optional options instance. When <see langword="null"/>, defaults are used.</param>
    /// <param name="force">When <see langword="true"/>, re-initializes and disposes any existing instance.</param>
    /// <returns>The initialized terminal instance.</returns>
    /// <remarks>If initialization fails, the selected backend is disposed and the global instance remains uninitialized.</remarks>
    public static TerminalInstance Initialize(ITerminalBackend? backend = null, TerminalOptions? options = null, bool force = false)
    {
        lock (InitLock)
        {
            if (_instance is { IsInitialized: true } && !force)
            {
                return _instance;
            }

            _instance?.Dispose();
            Volatile.Write(ref _instance, null);

            var instance = new TerminalInstance();
            try
            {
                instance.Initialize(backend, options);
            }
            catch
            {
                instance.Dispose();
                throw;
            }

            Volatile.Write(ref _instance, instance);
            HookProcessLifetime();
            return instance;
        }
    }

    /// <summary>
    /// Opens a terminal session that will dispose the global instance when disposed.
    /// </summary>
    /// <remarks>
    /// This is a convenience for apps that want deterministic cleanup without relying on process lifetime handlers.
    /// </remarks>
    public static TerminalSession Open(ITerminalBackend? backend = null, TerminalOptions? options = null, bool force = true)
    {
        var instance = Initialize(backend, options, force);
        return new TerminalSession(instance);
    }

    /// <summary>
    /// Disposes the global terminal instance (idempotent).
    /// </summary>
    public static void Close()
    {
        lock (InitLock)
        {
            _instance?.Dispose();
            _instance = null;
        }
    }

    /// <summary>
    /// Makes the specified instance the ambient terminal instance of the calling thread until the returned scope is disposed.
    /// </summary>
    /// <param name="instance">The instance returned by <see cref="Instance"/>, and therefore targeted by every static <see cref="Terminal"/> member, on the calling thread while the scope is active.</param>
    /// <returns>A scope that restores the previous ambient instance of the calling thread when disposed.</returns>
    /// <remarks>
    /// <para>
    /// The ambient instance lets code that uses the static <see cref="Terminal"/> facade (for example a library calling <see cref="WriteLine(string?)"/>)
    /// reach a standalone <see cref="TerminalInstance"/> without the instance being passed around.
    /// </para>
    /// <para>
    /// The ambient instance is stored per thread: other threads keep seeing their own ambient instance or the global instance,
    /// and it does not flow to tasks started from the calling thread or to <c>await</c> continuations that resume on another thread.
    /// </para>
    /// <para>
    /// The returned scope must be disposed on the thread that created it, in reverse order of creation.
    /// Disposing an active scope on another thread or out of order throws <see cref="InvalidOperationException"/> without consuming it.
    /// Disposing it more than once has no effect.
    /// </para>
    /// <para>
    /// This method does not change the process-global instance: <see cref="IsInitialized"/>, <see cref="Initialize"/>, <see cref="Open"/> and <see cref="Close"/>
    /// keep operating on the global instance.
    /// </para>
    /// </remarks>
    /// <exception cref="ArgumentNullException"><paramref name="instance"/> is <see langword="null"/>.</exception>
    public static TerminalScope UseInstance(TerminalInstance instance)
    {
        ArgumentNullException.ThrowIfNull(instance);

        var scope = new AmbientInstanceScope(_ambientInstance, _ambientScope);
        _ambientInstance = instance;
        _ambientScope = scope;
        return new TerminalScope(scope);
    }

    private static void HookProcessLifetime()
    {
        if (Interlocked.Exchange(ref _processExitHooked, 1) != 0)
        {
            return;
        }

        void Cleanup()
        {
            try
            {
                Volatile.Read(ref _instance)?.Dispose();
            }
            catch
            {
                // Best-effort.
            }
        }

        AppDomain.CurrentDomain.ProcessExit += (_, _) => Cleanup();
        AppDomain.CurrentDomain.UnhandledException += (_, _) => Cleanup();
    }

    /// <summary>
    /// Resets static state for tests and disposes the current backend instance (best effort).
    /// </summary>
    /// <remarks>
    /// This method is intended for test processes only.
    /// </remarks>
    internal static void ResetForTests()
    {
        _ambientInstance = null;
        _ambientScope = null;
        Close();
    }

    /// <inheritdoc cref="TerminalInstance.Write(string)" />
    public static TerminalInstance Write(string text) => Instance.Write(text);

    /// <inheritdoc cref="TerminalInstance.Write(System.ReadOnlySpan{char})" />
    public static TerminalInstance Write(ReadOnlySpan<char> text) => Instance.Write(text);

    /// <inheritdoc cref="TerminalInstance.WriteLine(string?)" />
    public static TerminalInstance WriteLine(string? text = null) => Instance.WriteLine(text);

    /// <inheritdoc cref="TerminalInstance.WriteLine(System.ReadOnlySpan{char})" />
    public static TerminalInstance WriteLine(ReadOnlySpan<char> text) => Instance.WriteLine(text);

    /// <inheritdoc cref="TerminalInstance.WriteMarkup(string)" />
    public static TerminalInstance WriteMarkup(string markup) => Instance.WriteMarkup(markup);

    /// <inheritdoc cref="TerminalInstance.WriteMarkup(ref AnsiMarkupInterpolatedStringHandler)" />
    public static TerminalInstance WriteMarkup(ref AnsiMarkupInterpolatedStringHandler markup) => Instance.WriteMarkup(ref markup);

    /// <inheritdoc cref="TerminalInstance.WriteMarkupLine(string)" />
    public static TerminalInstance WriteMarkupLine(string markup) => Instance.WriteMarkupLine(markup);

    /// <inheritdoc cref="TerminalInstance.WriteMarkupLine(ref AnsiMarkupInterpolatedStringHandler)" />
    public static TerminalInstance WriteMarkupLine(ref AnsiMarkupInterpolatedStringHandler markup) => Instance.WriteMarkupLine(ref markup);

    /// <inheritdoc cref="TerminalInstance.MeasureStyledWidth(string)" />
    public static int MeasureStyledWidth(string textWithAnsiOrMarkup) => Instance.MeasureStyledWidth(textWithAnsiOrMarkup);

    /// <inheritdoc cref="TerminalInstance.WriteAtomic(System.Action{XenoAtom.Ansi.AnsiWriter})" />
    public static TerminalInstance WriteAtomic(Action<AnsiWriter> write)
    {
        Instance.WriteAtomic(write);
        return Instance;
    }

    /// <inheritdoc cref="TerminalInstance.WriteAtomic(System.Action{System.IO.TextWriter})" />
    public static TerminalInstance WriteAtomic(Action<TextWriter> write)
    {
        Instance.WriteAtomic(write);
        return Instance;
    }

    /// <inheritdoc cref="TerminalInstance.WriteErrorAtomic(System.Action{System.IO.TextWriter})" />
    public static TerminalInstance WriteErrorAtomic(Action<TextWriter> write)
    {
        Instance.WriteErrorAtomic(write);
        return Instance;
    }

    /// <inheritdoc cref="TerminalInstance.Flush" />
    public static TerminalInstance Flush() => Instance.Flush();

    /// <inheritdoc cref="TerminalInstance.Clear" />
    public static TerminalInstance Clear(TerminalClearKind kind = TerminalClearKind.Screen) => Instance.Clear(kind);

    /// <inheritdoc cref="TerminalInstance.UseRawMode" />
    public static TerminalScope UseRawMode(TerminalRawModeKind kind = TerminalRawModeKind.CBreak) => Instance.UseRawMode(kind);

    /// <inheritdoc cref="TerminalInstance.UseAlternateScreen" />
    public static TerminalScope UseAlternateScreen() => Instance.UseAlternateScreen();

    /// <inheritdoc cref="TerminalInstance.HideCursor" />
    public static TerminalScope HideCursor() => Instance.HideCursor();

    /// <inheritdoc cref="TerminalInstance.EnableMouse" />
    public static TerminalScope EnableMouse(TerminalMouseMode mode = TerminalMouseMode.Drag) => Instance.EnableMouse(mode);

    /// <inheritdoc cref="TerminalInstance.EnableMouseInput" />
    public static TerminalScope EnableMouseInput(TerminalMouseMode mode = TerminalMouseMode.Drag) => Instance.EnableMouseInput(mode);


    /// <inheritdoc cref="TerminalInstance.EnableBracketedPaste" />
    public static TerminalScope EnableBracketedPaste() => Instance.EnableBracketedPaste();

    /// <inheritdoc cref="TerminalInstance.EnableBracketedPasteInput" />
    public static TerminalScope EnableBracketedPasteInput() => Instance.EnableBracketedPasteInput();

    /// <inheritdoc cref="TerminalInstance.UseTitle" />
    public static TerminalScope UseTitle(string title) => Instance.UseTitle(title);

    /// <inheritdoc cref="TerminalInstance.SetInputEcho" />
    public static TerminalScope SetInputEcho(bool enabled) => Instance.SetInputEcho(enabled);

    /// <inheritdoc cref="TerminalInstance.IsInputRunning" />
    public static bool IsInputRunning => Instance.IsInputRunning;

    /// <inheritdoc cref="TerminalInstance.StartInput" />
    public static TerminalInstance StartInput(TerminalInputOptions? options = null)
    {
        Instance.StartInput(options);
        return Instance;
    }

    /// <inheritdoc cref="TerminalInstance.StopInputAsync" />
    public static Task StopInputAsync(CancellationToken cancellationToken = default) => Instance.StopInputAsync(cancellationToken);

    /// <inheritdoc cref="TerminalInstance.ReadEventsAsync" />
    public static IAsyncEnumerable<TerminalEvent> ReadEventsAsync(CancellationToken cancellationToken = default) => Instance.ReadEventsAsync(cancellationToken);

    /// <inheritdoc cref="TerminalInstance.TryReadEvent" />
    public static bool TryReadEvent(out TerminalEvent ev) => Instance.TryReadEvent(out ev);

    /// <inheritdoc cref="TerminalInstance.ReadEventAsync" />
    public static ValueTask<TerminalEvent> ReadEventAsync(CancellationToken cancellationToken = default) => Instance.ReadEventAsync(cancellationToken);

    private static void EnsureInitialized()
    {
        if (Volatile.Read(ref _instance) is { IsInitialized: true })
        {
            return;
        }

        Initialize();
    }

    private sealed class AmbientInstanceScope : IDisposable
    {
        private readonly TerminalInstance? _previous;
        private readonly AmbientInstanceScope? _previousScope;
        private readonly int _threadId;
        private bool _isDisposed;

        public AmbientInstanceScope(TerminalInstance? previous, AmbientInstanceScope? previousScope)
        {
            _previous = previous;
            _previousScope = previousScope;
            _threadId = Environment.CurrentManagedThreadId;
        }

        public void Dispose()
        {
            if (_isDisposed)
            {
                return;
            }

            if (Environment.CurrentManagedThreadId != _threadId)
            {
                throw new InvalidOperationException("The ambient terminal scope must be disposed on the thread that created it.");
            }

            // Track scope identity, not instance identity: nested scopes may select the same terminal.
            if (!ReferenceEquals(_ambientScope, this))
            {
                throw new InvalidOperationException("Ambient terminal scopes must be disposed in reverse order of creation.");
            }

            _isDisposed = true;
            _ambientInstance = _previous;
            _ambientScope = _previousScope;
        }
    }
}
