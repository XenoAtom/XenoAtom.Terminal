// Copyright (c) Alexandre Mutel. All rights reserved.
// Licensed under the BSD-Clause 2 license.
// See license.txt file in the project root for full license information.

using XenoAtom.Ansi;

namespace XenoAtom.Terminal;

public static partial class Terminal
{
    /// <inheritdoc cref="AnsiWriter.Reset()"/>
    public static TerminalInstance Reset() => Instance.Reset();

    /// <inheritdoc cref="AnsiWriter.ResetStyle()"/>
    public static TerminalInstance ResetStyle() => Instance.ResetStyle();

    /// <inheritdoc cref="AnsiWriter.Foreground(AnsiColor)"/>
    public static TerminalInstance Foreground(AnsiColor color) => Instance.Foreground(color);

    /// <inheritdoc cref="AnsiWriter.Background(AnsiColor)"/>
    public static TerminalInstance Background(AnsiColor color) => Instance.Background(color);

    /// <inheritdoc cref="TerminalInstance.ApplyStyle(AnsiStyle)"/>
    public static TerminalInstance ApplyStyle(AnsiStyle style) => Instance.ApplyStyle(style);

    /// <inheritdoc cref="AnsiWriter.StyleTransition(AnsiStyle,AnsiStyle)"/>
    public static TerminalInstance StyleTransition(AnsiStyle from, AnsiStyle to) => Instance.StyleTransition(from, to);

    /// <inheritdoc cref="AnsiWriter.StyleTransition(AnsiStyle,AnsiStyle,AnsiCapabilities)"/>
    public static TerminalInstance StyleTransition(AnsiStyle from, AnsiStyle to, AnsiCapabilities capabilities) => Instance.StyleTransition(from, to, capabilities);

    /// <inheritdoc cref="AnsiWriter.Decorate(AnsiDecorations)"/>
    public static TerminalInstance Decorate(AnsiDecorations decorations) => Instance.Decorate(decorations);

    /// <inheritdoc cref="AnsiWriter.Undecorate(AnsiDecorations)"/>
    public static TerminalInstance Undecorate(AnsiDecorations decorations) => Instance.Undecorate(decorations);

    /// <inheritdoc cref="AnsiWriter.CursorUp(int)"/>
    public static TerminalInstance CursorUp(int n = 1) => Instance.CursorUp(n);

    /// <inheritdoc cref="AnsiWriter.CursorDown(int)"/>
    public static TerminalInstance CursorDown(int n = 1) => Instance.CursorDown(n);

    /// <inheritdoc cref="AnsiWriter.CursorForward(int)"/>
    public static TerminalInstance CursorForward(int n = 1) => Instance.CursorForward(n);

    /// <inheritdoc cref="AnsiWriter.CursorBack(int)"/>
    public static TerminalInstance CursorBack(int n = 1) => Instance.CursorBack(n);

    /// <inheritdoc cref="AnsiWriter.NextLine(int)"/>
    public static TerminalInstance NextLine(int n = 1) => Instance.NextLine(n);

    /// <inheritdoc cref="AnsiWriter.PreviousLine(int)"/>
    public static TerminalInstance PreviousLine(int n = 1) => Instance.PreviousLine(n);

    /// <inheritdoc cref="AnsiWriter.CursorHorizontalAbsolute(int)"/>
    public static TerminalInstance CursorHorizontalAbsolute(int col = 1) => Instance.CursorHorizontalAbsolute(col);

    /// <inheritdoc cref="AnsiWriter.CursorVerticalAbsolute(int)"/>
    public static TerminalInstance CursorVerticalAbsolute(int row = 1) => Instance.CursorVerticalAbsolute(row);

    /// <inheritdoc cref="AnsiWriter.CursorPosition(int,int)"/>
    public static TerminalInstance CursorPosition(int row, int col) => Instance.CursorPosition(row, col);

    /// <inheritdoc cref="AnsiWriter.HorizontalAndVerticalPosition(int,int)"/>
    public static TerminalInstance HorizontalAndVerticalPosition(int row, int col) => Instance.HorizontalAndVerticalPosition(row, col);

    /// <inheritdoc cref="AnsiWriter.MoveTo(int,int)"/>
    public static TerminalInstance MoveTo(int row, int col) => Instance.MoveTo(row, col);

    /// <inheritdoc cref="AnsiWriter.Up(int)"/>
    public static TerminalInstance Up(int n = 1) => Instance.Up(n);

    /// <inheritdoc cref="AnsiWriter.Down(int)"/>
    public static TerminalInstance Down(int n = 1) => Instance.Down(n);

    /// <inheritdoc cref="AnsiWriter.Forward(int)"/>
    public static TerminalInstance Forward(int n = 1) => Instance.Forward(n);

    /// <inheritdoc cref="AnsiWriter.Back(int)"/>
    public static TerminalInstance Back(int n = 1) => Instance.Back(n);

    /// <inheritdoc cref="AnsiWriter.ReverseIndex()"/>
    public static TerminalInstance ReverseIndex() => Instance.ReverseIndex();

    /// <inheritdoc cref="AnsiWriter.SaveCursor()"/>
    public static TerminalInstance SaveCursor() => Instance.SaveCursor();

    /// <inheritdoc cref="AnsiWriter.HorizontalTabSet()"/>
    public static TerminalInstance HorizontalTabSet() => Instance.HorizontalTabSet();

    /// <inheritdoc cref="AnsiWriter.CursorForwardTab(int)"/>
    public static TerminalInstance CursorForwardTab(int n = 1) => Instance.CursorForwardTab(n);

    /// <inheritdoc cref="AnsiWriter.CursorBackTab(int)"/>
    public static TerminalInstance CursorBackTab(int n = 1) => Instance.CursorBackTab(n);

    /// <inheritdoc cref="AnsiWriter.ClearTabStop()"/>
    public static TerminalInstance ClearTabStop() => Instance.ClearTabStop();

    /// <inheritdoc cref="AnsiWriter.ClearAllTabStops()"/>
    public static TerminalInstance ClearAllTabStops() => Instance.ClearAllTabStops();

    /// <inheritdoc cref="AnsiWriter.EnterLineDrawingMode()"/>
    public static TerminalInstance EnterLineDrawingMode() => Instance.EnterLineDrawingMode();

    /// <inheritdoc cref="AnsiWriter.ExitLineDrawingMode()"/>
    public static TerminalInstance ExitLineDrawingMode() => Instance.ExitLineDrawingMode();

    /// <inheritdoc cref="AnsiWriter.KeypadApplicationMode()"/>
    public static TerminalInstance KeypadApplicationMode() => Instance.KeypadApplicationMode();

    /// <inheritdoc cref="AnsiWriter.KeypadNumericMode()"/>
    public static TerminalInstance KeypadNumericMode() => Instance.KeypadNumericMode();

    /// <inheritdoc cref="AnsiWriter.CursorKeysApplicationMode(bool)"/>
    public static TerminalInstance CursorKeysApplicationMode(bool enabled) => Instance.CursorKeysApplicationMode(enabled);

    /// <inheritdoc cref="AnsiWriter.CursorBlinking(bool)"/>
    public static TerminalInstance CursorBlinking(bool enabled) => Instance.CursorBlinking(enabled);

    /// <inheritdoc cref="AnsiWriter.Columns132(bool)"/>
    public static TerminalInstance Columns132(bool enabled) => Instance.Columns132(enabled);

    /// <inheritdoc cref="AnsiWriter.SaveCursorPosition()"/>
    public static TerminalInstance SaveCursorPosition() => Instance.SaveCursorPosition();

    /// <inheritdoc cref="AnsiWriter.RestoreCursor()"/>
    public static TerminalInstance RestoreCursor() => Instance.RestoreCursor();

    /// <inheritdoc cref="AnsiWriter.RestoreCursorPosition()"/>
    public static TerminalInstance RestoreCursorPosition() => Instance.RestoreCursorPosition();

    /// <inheritdoc cref="AnsiWriter.EraseInLine(int)"/>
    public static TerminalInstance EraseInLine(int mode = 0) => Instance.EraseInLine(mode);

    /// <inheritdoc cref="AnsiWriter.EraseInDisplay(int)"/>
    public static TerminalInstance EraseInDisplay(int mode = 0) => Instance.EraseInDisplay(mode);

    /// <inheritdoc cref="AnsiWriter.EraseScrollback()"/>
    public static TerminalInstance EraseScrollback() => Instance.EraseScrollback();

    /// <inheritdoc cref="AnsiWriter.EraseCharacters(int)"/>
    public static TerminalInstance EraseCharacters(int n = 1) => Instance.EraseCharacters(n);

    /// <inheritdoc cref="AnsiWriter.InsertCharacters(int)"/>
    public static TerminalInstance InsertCharacters(int n = 1) => Instance.InsertCharacters(n);

    /// <inheritdoc cref="AnsiWriter.DeleteCharacters(int)"/>
    public static TerminalInstance DeleteCharacters(int n = 1) => Instance.DeleteCharacters(n);

    /// <inheritdoc cref="AnsiWriter.InsertLines(int)"/>
    public static TerminalInstance InsertLines(int n = 1) => Instance.InsertLines(n);

    /// <inheritdoc cref="AnsiWriter.DeleteLines(int)"/>
    public static TerminalInstance DeleteLines(int n = 1) => Instance.DeleteLines(n);

    /// <inheritdoc cref="AnsiWriter.ScrollUp(int)"/>
    public static TerminalInstance ScrollUp(int n = 1) => Instance.ScrollUp(n);

    /// <inheritdoc cref="AnsiWriter.ScrollDown(int)"/>
    public static TerminalInstance ScrollDown(int n = 1) => Instance.ScrollDown(n);

    /// <inheritdoc cref="AnsiWriter.SetScrollRegion(int,int)"/>
    public static TerminalInstance SetScrollRegion(int top, int bottom) => Instance.SetScrollRegion(top, bottom);

    /// <inheritdoc cref="AnsiWriter.ResetScrollRegion()"/>
    public static TerminalInstance ResetScrollRegion() => Instance.ResetScrollRegion();

    /// <inheritdoc cref="AnsiWriter.SetMode(int,bool)"/>
    public static TerminalInstance SetMode(int mode, bool enabled) => Instance.SetMode(mode, enabled);

    /// <inheritdoc cref="AnsiWriter.PrivateMode(int,bool)"/>
    public static TerminalInstance PrivateMode(int mode, bool enabled) => Instance.PrivateMode(mode, enabled);

    /// <inheritdoc cref="AnsiWriter.CursorStyle(AnsiCursorStyle)"/>
    public static TerminalInstance CursorStyle(AnsiCursorStyle style) => Instance.CursorStyle(style);

    /// <inheritdoc cref="AnsiWriter.EraseLine(int)"/>
    public static TerminalInstance EraseLine(int mode = 0) => Instance.EraseLine(mode);

    /// <inheritdoc cref="AnsiWriter.EraseDisplay(int)"/>
    public static TerminalInstance EraseDisplay(int mode = 0) => Instance.EraseDisplay(mode);

    /// <inheritdoc cref="AnsiWriter.ShowCursor(bool)"/>
    public static TerminalInstance ShowCursor(bool visible) => Instance.ShowCursor(visible);

    /// <inheritdoc cref="AnsiWriter.CursorVisible(bool)"/>
    public static TerminalInstance CursorVisible(bool visible) => Instance.CursorVisible(visible);

    /// <inheritdoc cref="AnsiWriter.EnterAlternateScreen()"/>
    public static TerminalInstance EnterAlternateScreen() => Instance.EnterAlternateScreen();

    /// <inheritdoc cref="AnsiWriter.LeaveAlternateScreen()"/>
    public static TerminalInstance LeaveAlternateScreen() => Instance.LeaveAlternateScreen();

    /// <inheritdoc cref="AnsiWriter.AlternateScreen(bool)"/>
    public static TerminalInstance AlternateScreen(bool enabled) => Instance.AlternateScreen(enabled);

    /// <inheritdoc cref="AnsiWriter.SoftReset()"/>
    public static TerminalInstance SoftReset() => Instance.SoftReset();

    /// <inheritdoc cref="AnsiWriter.RequestCursorPosition()"/>
    public static TerminalInstance RequestCursorPosition() => Instance.RequestCursorPosition();

    /// <inheritdoc cref="AnsiWriter.RequestDeviceAttributes()"/>
    public static TerminalInstance RequestDeviceAttributes() => Instance.RequestDeviceAttributes();

    /// <inheritdoc cref="AnsiWriter.WindowTitle(ReadOnlySpan{char})"/>
    public static TerminalInstance WindowTitle(ReadOnlySpan<char> title) => Instance.WindowTitle(title);

    /// <inheritdoc cref="AnsiWriter.IconAndWindowTitle(ReadOnlySpan{char})"/>
    public static TerminalInstance IconAndWindowTitle(ReadOnlySpan<char> title) => Instance.IconAndWindowTitle(title);

    /// <inheritdoc cref="AnsiWriter.SetPaletteColor(int,byte,byte,byte)"/>
    public static TerminalInstance SetPaletteColor(int index, byte r, byte g, byte b) => Instance.SetPaletteColor(index, r, g, b);

    /// <inheritdoc cref="AnsiWriter.BeginLink(string,string?)"/>
    public static TerminalInstance BeginLink(string uri, string? id = null) => Instance.BeginLink(uri, id);

    /// <inheritdoc cref="AnsiWriter.EndLink()"/>
    public static TerminalInstance EndLink() => Instance.EndLink();

    /// <inheritdoc cref="AnsiWriter.WriteCursorPositionReport(int,int)"/>
    public static TerminalInstance WriteCursorPositionReport(int row, int column) => Instance.WriteCursorPositionReport(row, column);
}

