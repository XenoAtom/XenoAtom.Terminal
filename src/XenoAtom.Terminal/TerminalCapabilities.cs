// Copyright (c) Alexandre Mutel. All rights reserved.
// Licensed under the BSD-Clause 2 license.
// See license.txt file in the project root for full license information.

namespace XenoAtom.Terminal;

/// <summary>
/// Describes terminal capabilities detected by the backend.
/// </summary>
public sealed class TerminalCapabilities
{
    /// <summary>
    /// Gets a value indicating whether ANSI escape sequences are enabled and supported for output.
    /// </summary>
    public required bool AnsiEnabled { get; init; }

    /// <summary>
    /// Gets the detected color capability level.
    /// </summary>
    public required TerminalColorLevel ColorLevel { get; init; }

    /// <summary>
    /// Gets a value indicating whether OSC 8 hyperlinks are supported.
    /// </summary>
    public bool SupportsOsc8Links { get; init; }

    /// <summary>
    /// Gets a value indicating whether the alternate screen buffer is supported.
    /// </summary>
    public bool SupportsAlternateScreen { get; init; }

    /// <summary>
    /// Gets a value indicating whether cursor visibility can be controlled.
    /// </summary>
    public bool SupportsCursorVisibility { get; init; }

    /// <summary>
    /// Gets a value indicating whether mouse reporting can be enabled and decoded.
    /// </summary>
    public bool SupportsMouse { get; init; }

    /// <summary>
    /// Gets a value indicating whether bracketed paste can be enabled and decoded.
    /// </summary>
    public bool SupportsBracketedPaste { get; init; }

    /// <summary>
    /// Gets a value indicating whether DEC private modes (CSI ? ... h/l) should be emitted.
    /// </summary>
    /// <remarks>
    /// This is intentionally broader than individual high-level features. Some hosts (for example CI logs)
    /// preserve raw ANSI text but do not interpret private modes, which can pollute output.
    /// </remarks>
    public bool SupportsPrivateModes { get; init; }

    /// <summary>
    /// Gets a value indicating whether raw/cbreak input modes are supported.
    /// </summary>
    public bool SupportsRawMode { get; init; }

    /// <summary>
    /// Gets a value indicating whether cursor position can be retrieved.
    /// </summary>
    public bool SupportsCursorPositionGet { get; init; }

    /// <summary>
    /// Gets a value indicating whether cursor position can be set.
    /// </summary>
    public bool SupportsCursorPositionSet { get; init; }

    /// <summary>
    /// Gets a value indicating whether the system clipboard can be accessed (best effort).
    /// </summary>
    public bool SupportsClipboard { get; init; }

    /// <summary>
    /// Gets a value indicating whether clipboard text can be read.
    /// </summary>
    public bool SupportsClipboardGet { get; init; }

    /// <summary>
    /// Gets a value indicating whether clipboard text can be set.
    /// </summary>
    public bool SupportsClipboardSet { get; init; }

    /// <summary>
    /// Gets a value indicating whether OSC 52 clipboard sequences can be emitted (set-only).
    /// </summary>
    public bool SupportsOsc52Clipboard { get; init; }

    /// <summary>
    /// Gets a value indicating whether the terminal title can be retrieved.
    /// </summary>
    public bool SupportsTitleGet { get; init; }

    /// <summary>
    /// Gets a value indicating whether the terminal title can be set.
    /// </summary>
    public bool SupportsTitleSet { get; init; }

    /// <summary>
    /// Gets a value indicating whether the window size can be queried.
    /// </summary>
    public bool SupportsWindowSize { get; init; }

    /// <summary>
    /// Gets a value indicating whether the window size can be set.
    /// </summary>
    public bool SupportsWindowSizeSet { get; init; }

    /// <summary>
    /// Gets a value indicating whether the buffer size can be queried.
    /// </summary>
    public bool SupportsBufferSize { get; init; }

    /// <summary>
    /// Gets a value indicating whether the buffer size can be set.
    /// </summary>
    public bool SupportsBufferSizeSet { get; init; }

    /// <summary>
    /// Gets a value indicating whether an audible beep is supported.
    /// </summary>
    public bool SupportsBeep { get; init; }

    /// <summary>
    /// Gets a value indicating whether output is redirected (not a terminal/tty).
    /// </summary>
    public required bool IsOutputRedirected { get; init; }

    /// <summary>
    /// Gets a value indicating whether input is redirected (not a terminal/tty).
    /// </summary>
    public required bool IsInputRedirected { get; init; }

    /// <summary>
    /// Gets the detected terminal name or host identifier (best effort).
    /// </summary>
    public string? TerminalName { get; init; }
}
