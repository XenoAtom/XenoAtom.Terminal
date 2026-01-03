// Copyright (c) Alexandre Mutel. All rights reserved.
// Licensed under the BSD-Clause 2 license.
// See license.txt file in the project root for full license information.

namespace XenoAtom.Terminal;

/// <summary>
/// Built-in commands for the interactive ReadLine editor.
/// </summary>
public enum TerminalReadLineCommand
{
    /// <summary>
    /// No command.
    /// </summary>
    None,

    /// <summary>
    /// Ignores the key gesture (marks it handled).
    /// </summary>
    Ignore,

    /// <summary>
    /// Accepts the current line.
    /// </summary>
    AcceptLine,

    /// <summary>
    /// Cancels the current ReadLine.
    /// </summary>
    Cancel,

    /// <summary>
    /// Requests completion (typically Tab).
    /// </summary>
    Complete,

    /// <summary>
    /// Moves the cursor left by one rune.
    /// </summary>
    CursorLeft,

    /// <summary>
    /// Moves the cursor right by one rune.
    /// </summary>
    CursorRight,

    /// <summary>
    /// Moves the cursor to the start of the line.
    /// </summary>
    CursorHome,

    /// <summary>
    /// Moves the cursor to the end of the line.
    /// </summary>
    CursorEnd,

    /// <summary>
    /// Moves the cursor left by one word.
    /// </summary>
    CursorWordLeft,

    /// <summary>
    /// Moves the cursor right by one word.
    /// </summary>
    CursorWordRight,

    /// <summary>
    /// Deletes one rune to the left of the cursor.
    /// </summary>
    DeleteBackward,

    /// <summary>
    /// Deletes one rune to the right of the cursor.
    /// </summary>
    DeleteForward,

    /// <summary>
    /// Deletes one word to the left of the cursor.
    /// </summary>
    DeleteWordBackward,

    /// <summary>
    /// Deletes one word to the right of the cursor.
    /// </summary>
    DeleteWordForward,

    /// <summary>
    /// Clears the current selection.
    /// </summary>
    ClearSelection,

    /// <summary>
    /// Navigates to the previous history entry.
    /// </summary>
    HistoryPrevious,

    /// <summary>
    /// Navigates to the next history entry.
    /// </summary>
    HistoryNext,

    /// <summary>
    /// Copies the current selection to the clipboard.
    /// </summary>
    CopySelection,

    /// <summary>
    /// Copies the current selection to the clipboard; when there is no selection, cancels the read.
    /// </summary>
    CopySelectionOrCancel,

    /// <summary>
    /// Cuts the current selection (or the full line) to the clipboard.
    /// </summary>
    CutSelectionOrAll,

    /// <summary>
    /// Pastes clipboard text (or the internal kill buffer).
    /// </summary>
    Paste,

    /// <summary>
    /// Kills text from the cursor to the end of the line (stores it in the internal kill buffer).
    /// </summary>
    KillToEnd,

    /// <summary>
    /// Kills text from the start of the line to the cursor (stores it in the internal kill buffer).
    /// </summary>
    KillToStart,

    /// <summary>
    /// Kills one word to the left of the cursor (stores it in the internal kill buffer).
    /// </summary>
    KillWordLeft,

    /// <summary>
    /// Kills one word to the right of the cursor (stores it in the internal kill buffer).
    /// </summary>
    KillWordRight,

    /// <summary>
    /// Undoes the last edit.
    /// </summary>
    Undo,

    /// <summary>
    /// Redoes the last undone edit.
    /// </summary>
    Redo,

    /// <summary>
    /// Starts or continues reverse incremental history search.
    /// </summary>
    ReverseSearch,

    /// <summary>
    /// Clears the screen.
    /// </summary>
    ClearScreen,
}
