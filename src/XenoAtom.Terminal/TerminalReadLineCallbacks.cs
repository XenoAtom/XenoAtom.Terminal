// Copyright (c) Alexandre Mutel. All rights reserved.
// Licensed under the BSD-Clause 2 license.
// See license.txt file in the project root for full license information.

using System.Collections.Generic;

namespace XenoAtom.Terminal;

/// <summary>
/// A key handler invoked by the interactive line editor before applying default behavior.
/// </summary>
public delegate void TerminalReadLineKeyHandler(TerminalReadLineController controller, TerminalKeyEvent key);

/// <summary>
/// A mouse handler invoked by the interactive line editor before applying default behavior.
/// </summary>
public delegate void TerminalReadLineMouseHandler(TerminalReadLineController controller, TerminalMouseEvent mouse);

/// <summary>
/// A completion handler invoked when the user requests completion (e.g. Tab).
/// </summary>
public delegate TerminalReadLineCompletion TerminalReadLineCompletionHandler(ReadOnlySpan<char> text, int cursorIndex, int selectionStart, int selectionLength);

/// <summary>
/// A renderer invoked to produce markup for the current visible slice of the line.
/// </summary>
/// <remarks>
/// The returned string is interpreted as XenoAtom markup and rendered to ANSI. If the returned markup includes user input,
/// callers should escape it via <see cref="XenoAtom.Ansi.AnsiMarkup.Escape(System.ReadOnlySpan{char})"/>.
/// </remarks>
public delegate string TerminalReadLineMarkupRenderer(ReadOnlySpan<char> text, int cursorIndex, int viewStart, int viewLength, int selectionStart, int selectionLength);

/// <summary>
/// A renderer invoked to produce markup for the prompt of the interactive line editor.
/// </summary>
/// <remarks>
/// The returned string is interpreted as XenoAtom markup and rendered to ANSI.
/// </remarks>
public delegate string TerminalReadLinePromptMarkupRenderer();

/// <summary>
/// Result of <see cref="TerminalReadLineCompletionHandler"/>.
/// </summary>
public readonly record struct TerminalReadLineCompletion
{
    /// <summary>
    /// Gets whether the completion request was handled.
    /// </summary>
    public bool Handled { get; init; }

    /// <summary>
    /// Gets optional completion candidates. When provided, the line editor applies the first candidate and
    /// subsequent Tab presses cycle through the list until the user performs another edit action.
    /// </summary>
    /// <remarks>
    /// When using candidates, the editor replaces a range of text. Set <see cref="ReplaceStart"/>/<see cref="ReplaceLength"/>
    /// to control the replacement. When not set, the editor replaces the current selection if any; otherwise, it replaces
    /// the token fragment to the left of the cursor (best effort).
    /// </remarks>
    public IReadOnlyList<string>? Candidates { get; init; }

    /// <summary>
    /// Gets the start index (UTF-16 code units) of the range to replace when applying completion candidates.
    /// </summary>
    public int? ReplaceStart { get; init; }

    /// <summary>
    /// Gets the length (UTF-16 code units) of the range to replace when applying completion candidates.
    /// </summary>
    public int? ReplaceLength { get; init; }

    /// <summary>
    /// Inserts the provided text at the cursor position.
    /// </summary>
    public string? InsertText { get; init; }

    /// <summary>
    /// Replaces the entire line with the provided text.
    /// </summary>
    public string? ReplaceText { get; init; }

    /// <summary>
    /// Sets the cursor index after applying changes (0..Length in UTF-16 code units).
    /// </summary>
    public int? CursorIndex { get; init; }
}
