// Copyright (c) Alexandre Mutel. All rights reserved.
// Licensed under the BSD-Clause 2 license.
// See license.txt file in the project root for full license information.

using System.Diagnostics.CodeAnalysis;
using System.IO;
using XenoAtom.Ansi;

namespace XenoAtom.Terminal.Backends;

/// <summary>
/// Defines the platform-specific backend used by <see cref="Terminal"/> and <see cref="TerminalInstance"/>.
/// </summary>
/// <remarks>
/// Implementations should be thread-safe with respect to output serialization and should publish input events without blocking output.
/// </remarks>
public interface ITerminalBackend : IDisposable
{
    /// <summary>
    /// Gets the detected capabilities for this backend.
    /// </summary>
    TerminalCapabilities Capabilities { get; }

    /// <summary>
    /// Gets the writer used for standard output.
    /// </summary>
    TextWriter Out { get; }

    /// <summary>
    /// Gets the writer used for standard error.
    /// </summary>
    TextWriter Error { get; }

    /// <summary>
    /// Gets the current terminal size.
    /// </summary>
    TerminalSize GetSize();

    /// <summary>
    /// Tries to get the current cursor position (0-based).
    /// </summary>
    /// <param name="position">The current cursor position.</param>
    /// <returns><see langword="true"/> when cursor position is available; otherwise <see langword="false"/>.</returns>
    bool TryGetCursorPosition(out TerminalPosition position);

    /// <summary>
    /// Sets the cursor position (0-based).
    /// </summary>
    /// <param name="position">The new cursor position.</param>
    void SetCursorPosition(TerminalPosition position);

    /// <summary>
    /// Tries to get the current cursor visibility.
    /// </summary>
    /// <param name="visible">Whether the cursor is visible.</param>
    /// <returns><see langword="true"/> when cursor visibility is available; otherwise <see langword="false"/>.</returns>
    bool TryGetCursorVisible(out bool visible);

    /// <summary>
    /// Sets the cursor visibility (best effort).
    /// </summary>
    /// <param name="visible">Whether the cursor should be visible.</param>
    void SetCursorVisible(bool visible);

    /// <summary>
    /// Tries to get the current terminal title.
    /// </summary>
    /// <param name="title">The current title.</param>
    /// <returns><see langword="true"/> when title is available; otherwise <see langword="false"/>.</returns>
    bool TryGetTitle(out string title);

    /// <summary>
    /// Sets the terminal title (best effort).
    /// </summary>
    /// <param name="title">The new title.</param>
    void SetTitle(string title);

    /// <summary>
    /// Sets the foreground color (best effort).
    /// </summary>
    /// <param name="color">The desired color.</param>
    void SetForegroundColor(AnsiColor color);

    /// <summary>
    /// Sets the background color (best effort).
    /// </summary>
    /// <param name="color">The desired color.</param>
    void SetBackgroundColor(AnsiColor color);

    /// <summary>
    /// Resets foreground/background colors to defaults (best effort).
    /// </summary>
    void ResetColors();

    /// <summary>
    /// Tries to get the current clipboard text (best effort).
    /// </summary>
    bool TryGetClipboardText([NotNullWhen(true)] out string? text);

    /// <summary>
    /// Tries to set the clipboard text (best effort).
    /// </summary>
    bool TrySetClipboardText(ReadOnlySpan<char> text);

    /// <summary>
    /// Gets the current window size in character cells.
    /// </summary>
    TerminalSize GetWindowSize();

    /// <summary>
    /// Gets the current buffer size in character cells.
    /// </summary>
    TerminalSize GetBufferSize();

    /// <summary>
    /// Gets the largest possible window size in character cells (best effort).
    /// </summary>
    TerminalSize GetLargestWindowSize();

    /// <summary>
    /// Sets the window size (best effort).
    /// </summary>
    void SetWindowSize(TerminalSize size);

    /// <summary>
    /// Sets the buffer size (best effort).
    /// </summary>
    void SetBufferSize(TerminalSize size);

    /// <summary>
    /// Emits an audible beep (best effort).
    /// </summary>
    void Beep();

    /// <summary>
    /// Initializes the backend.
    /// </summary>
    /// <param name="options">The terminal options.</param>
    void Initialize(TerminalOptions options);

    /// <summary>
    /// Flushes any buffered output (best effort).
    /// </summary>
    void Flush();

    /// <summary>
    /// Enables raw/cbreak input mode within a scope (best effort).
    /// </summary>
    /// <param name="kind">The raw mode kind.</param>
    /// <returns>A scope that restores the previous state on dispose.</returns>
    TerminalScope UseRawMode(TerminalRawModeKind kind);

    /// <summary>
    /// Enables the alternate screen buffer within a scope (best effort).
    /// </summary>
    /// <returns>A scope that restores the previous state on dispose.</returns>
    TerminalScope UseAlternateScreen();

    /// <summary>
    /// Hides the cursor within a scope (best effort).
    /// </summary>
    /// <returns>A scope that restores the previous state on dispose.</returns>
    TerminalScope HideCursor();

    /// <summary>
    /// Enables mouse reporting within a scope (best effort).
    /// </summary>
    /// <param name="mode">The mouse reporting mode.</param>
    /// <returns>A scope that restores the previous state on dispose.</returns>
    TerminalScope EnableMouse(TerminalMouseMode mode);

    /// <summary>
    /// Enables bracketed paste within a scope (best effort).
    /// </summary>
    /// <returns>A scope that restores the previous state on dispose.</returns>
    TerminalScope EnableBracketedPaste();

    /// <summary>
    /// Sets the terminal title within a scope (best effort).
    /// </summary>
    /// <param name="title">The title to set.</param>
    /// <returns>A scope that restores the previous state on dispose.</returns>
    TerminalScope UseTitle(string title);

    /// <summary>
    /// Enables or disables input echo within a scope (best effort).
    /// </summary>
    /// <param name="enabled">Whether echo is enabled.</param>
    /// <returns>A scope that restores the previous state on dispose.</returns>
    TerminalScope SetInputEcho(bool enabled);

    /// <summary>
    /// Clears the terminal (best effort).
    /// </summary>
    /// <param name="kind">The clear mode.</param>
    void Clear(TerminalClearKind kind);

    /// <summary>
    /// Gets a value indicating whether the input loop is currently running.
    /// </summary>
    bool IsInputRunning { get; }

    /// <summary>
    /// Starts the input loop (idempotent).
    /// </summary>
    /// <param name="options">Input options.</param>
    void StartInput(TerminalInputOptions options);

    /// <summary>
    /// Stops the input loop and disposes any input resources (idempotent).
    /// </summary>
    /// <param name="cancellationToken">A cancellation token.</param>
    Task StopInputAsync(CancellationToken cancellationToken);

    /// <summary>
    /// Tries to read a single event without awaiting.
    /// </summary>
    /// <param name="ev">When this method returns <see langword="true"/>, contains the event.</param>
    /// <returns><see langword="true"/> when an event was available; otherwise <see langword="false"/>.</returns>
    bool TryReadEvent(out TerminalEvent ev);

    /// <summary>
    /// Reads the next event asynchronously.
    /// </summary>
    /// <param name="cancellationToken">A cancellation token.</param>
    /// <returns>The next terminal event.</returns>
    ValueTask<TerminalEvent> ReadEventAsync(CancellationToken cancellationToken);

    /// <summary>
    /// Reads terminal input events as an async stream.
    /// </summary>
    /// <param name="cancellationToken">A cancellation token.</param>
    /// <returns>An async stream of terminal events.</returns>
    IAsyncEnumerable<TerminalEvent> ReadEventsAsync(CancellationToken cancellationToken);
}
