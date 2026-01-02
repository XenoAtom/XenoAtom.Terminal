// Copyright (c) Alexandre Mutel. All rights reserved.
// Licensed under the BSD-Clause 2 license.
// See license.txt file in the project root for full license information.

using XenoAtom.Ansi;

namespace XenoAtom.Terminal;

/// <summary>
/// Provides cursor-related operations and state.
/// </summary>
public sealed class TerminalCursor
{
    private readonly TerminalInstance _terminal;
    private AnsiCursorStyle _style;

    internal TerminalCursor(TerminalInstance terminal)
    {
        _terminal = terminal ?? throw new ArgumentNullException(nameof(terminal));
        _style = AnsiCursorStyle.Default;
    }

    /// <summary>
    /// Gets or sets the 0-based cursor column.
    /// </summary>
    public int Left
    {
        get => Position.Column;
        set => Position = new TerminalPosition(value, Top);
    }

    /// <summary>
    /// Gets or sets the 0-based cursor row.
    /// </summary>
    public int Top
    {
        get => Position.Row;
        set => Position = new TerminalPosition(Left, value);
    }

    /// <summary>
    /// Gets or sets the 0-based cursor position.
    /// </summary>
    public TerminalPosition Position
    {
        get => _terminal.GetCursorPosition();
        set => _terminal.SetCursorPosition(value);
    }

    /// <summary>
    /// Gets or sets whether the cursor is visible.
    /// </summary>
    public bool Visible
    {
        get => _terminal.GetCursorVisible();
        set => _terminal.SetCursorVisible(value);
    }

    /// <summary>
    /// Gets or sets the cursor style (best effort).
    /// </summary>
    public AnsiCursorStyle Style
    {
        get => _style;
        set
        {
            _style = value;
            _terminal.CursorStyle(value);
        }
    }

    /// <summary>
    /// Saves the current cursor position and restores it when disposed (best effort).
    /// </summary>
    public TerminalScope UsePosition() => _terminal.UseCursorPosition();

    /// <summary>
    /// Sets the cursor position and restores the previous position when disposed (best effort).
    /// </summary>
    public TerminalScope UsePosition(TerminalPosition position) => _terminal.UseCursorPosition(position);

    /// <summary>
    /// Sets the cursor style and restores the previous style when disposed (best effort).
    /// </summary>
    public TerminalScope UseStyle(AnsiCursorStyle style)
    {
        var previous = _style;
        Style = style;
        return TerminalScope.Create(() => Style = previous);
    }
}

