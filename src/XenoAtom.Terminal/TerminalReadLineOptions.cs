// Copyright (c) Alexandre Mutel. All rights reserved.
// Licensed under the BSD-Clause 2 license.
// See license.txt file in the project root for full license information.

namespace XenoAtom.Terminal;

/// <summary>
/// Options controlling <see cref="Terminal.ReadLine(TerminalReadLineOptions?)"/> and
/// <see cref="Terminal.ReadLineAsync(TerminalReadLineOptions?, System.Threading.CancellationToken)"/>.
/// </summary>
public sealed class TerminalReadLineOptions
{
    /// <summary>
     /// Echoes typed text to the terminal output.
     /// </summary>
    public bool Echo { get; set; } = true;

    /// <summary>
    /// Optional prompt written before the editable text (plain text).
    /// </summary>
    /// <remarks>
    /// If <see cref="PromptMarkup"/> is set, it is used instead.
    /// </remarks>
    public string Prompt { get; set; } = string.Empty;

    /// <summary>
    /// Optional function that returns prompt markup, written before the editable text.
    /// </summary>
    /// <remarks>
    /// This callback is invoked once per ReadLine call (best effort) and the result is cached for the duration of that read.
    /// </remarks>
    public TerminalReadLinePromptMarkupRenderer? PromptMarkup { get; set; }

    /// <summary>
    /// When enabled, Ctrl+C / Ctrl+Break interrupts the read.
    /// </summary>
    public bool CancelOnSignal { get; set; } = true;

    /// <summary>
    /// Enables interactive line editing when supported (cursor movement, mid-line edits, history).
    /// </summary>
    /// <remarks>
    /// When output is redirected or cursor positioning is unavailable, the read falls back to a simple non-editing mode.
    /// </remarks>
    public bool EnableEditing { get; set; } = true;

    /// <summary>
    /// Enables history navigation (Up/Down) in the interactive editor.
    /// </summary>
    public bool EnableHistory { get; set; } = true;

    /// <summary>
    /// Adds accepted lines to history when history is enabled.
    /// </summary>
    public bool AddToHistory { get; set; } = true;

    /// <summary>
    /// Maximum number of history entries to keep (best effort).
    /// </summary>
    public int HistoryCapacity { get; set; } = 200;

    /// <summary>
    /// Gets the history associated with this options instance.
    /// </summary>
    public TerminalReadLineHistory History { get; init; } = new();

    /// <summary>
    /// Maximum number of UTF-16 code units allowed in the line (best effort).
    /// </summary>
    public int? MaxLength { get; set; }

    /// <summary>
    /// Optional maximum width (in terminal cells) reserved for the editable text region (excluding the prompt).
    /// </summary>
    public int? ViewWidth { get; set; }

    /// <summary>
    /// Controls whether an ellipsis is shown when the line does not fit in the visible width.
    /// </summary>
    public bool ShowEllipsis { get; set; } = true;

    /// <summary>
    /// The ellipsis string to show when truncation occurs.
    /// </summary>
    public string Ellipsis { get; set; } = "…";

    /// <summary>
    /// When enabled, pressing Enter writes a newline when <see cref="Echo"/> is enabled.
    /// </summary>
    public bool EmitNewLineOnAccept { get; set; } = true;

    /// <summary>
    /// Enables bracketed paste mode within the read scope (best effort).
    /// </summary>
    public bool EnableBracketedPaste { get; set; } = true;

    /// <summary>
    /// Enables mouse-based editing (cursor positioning and selection) when mouse events are available.
    /// </summary>
    public bool EnableMouseEditing { get; set; }

    /// <summary>
    /// Optional key handler invoked before default editor handling.
    /// </summary>
    public TerminalReadLineKeyHandler? KeyHandler { get; set; }

    /// <summary>
    /// Optional mouse handler invoked before default editor handling.
    /// </summary>
    public TerminalReadLineMouseHandler? MouseHandler { get; set; }

    /// <summary>
    /// Optional completion handler invoked for completion requests (Tab).
    /// </summary>
    public TerminalReadLineCompletionHandler? CompletionHandler { get; set; }

    /// <summary>
    /// Optional renderer that can return markup for the visible slice of the line.
    /// </summary>
    public TerminalReadLineMarkupRenderer? MarkupRenderer { get; set; }
}
