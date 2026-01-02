// Copyright (c) Alexandre Mutel. All rights reserved.
// Licensed under the BSD-Clause 2 license.
// See license.txt file in the project root for full license information.

using XenoAtom.Ansi;

namespace XenoAtom.Terminal;

public sealed partial class TerminalInstance
{
    /// <inheritdoc cref="AnsiWriter.Reset()"/>
    public TerminalInstance Reset()
    {
        SetStyleCore(AnsiStyle.Default);
        return this;
    }

    /// <inheritdoc cref="AnsiWriter.ResetStyle()"/>
    public TerminalInstance ResetStyle()
    {
        SetStyleCore(AnsiStyle.Default);
        return this;
    }

    /// <inheritdoc cref="AnsiWriter.Foreground(AnsiColor)"/>
    public TerminalInstance Foreground(AnsiColor color)
    {
        SetStyleCore(_style.WithForeground(color));
        return this;
    }

    /// <inheritdoc cref="AnsiWriter.Background(AnsiColor)"/>
    public TerminalInstance Background(AnsiColor color)
    {
        SetStyleCore(_style.WithBackground(color));
        return this;
    }

    /// <summary>
    /// Applies a complete style transition from the current style to the specified style.
    /// </summary>
    public TerminalInstance ApplyStyle(AnsiStyle style)
    {
        SetStyleCore(style);
        return this;
    }

    /// <inheritdoc cref="AnsiWriter.StyleTransition(AnsiStyle,AnsiStyle)"/>
    public TerminalInstance StyleTransition(AnsiStyle from, AnsiStyle to)
    {
        var fromResolved = from.ResolveMissingFrom(AnsiStyle.Default);
        var toResolved = to.ResolveMissingFrom(fromResolved);
        lock (_outputLock)
        {
            _writerUnsafe!.StyleTransition(from, to);
            _style = toResolved;
        }
        return this;
    }

    /// <inheritdoc cref="AnsiWriter.StyleTransition(AnsiStyle,AnsiStyle,AnsiCapabilities)"/>
    public TerminalInstance StyleTransition(AnsiStyle from, AnsiStyle to, AnsiCapabilities capabilities)
    {
        var fromResolved = from.ResolveMissingFrom(AnsiStyle.Default);
        var toResolved = to.ResolveMissingFrom(fromResolved);
        lock (_outputLock)
        {
            _writerUnsafe!.StyleTransition(from, to, capabilities);
            _style = toResolved;
        }
        return this;
    }

    /// <inheritdoc cref="AnsiWriter.Decorate(AnsiDecorations)"/>
    public TerminalInstance Decorate(AnsiDecorations decorations)
    {
        SetStyleCore(_style.WithDecorations(_style.Decorations | decorations));
        return this;
    }

    /// <inheritdoc cref="AnsiWriter.Undecorate(AnsiDecorations)"/>
    public TerminalInstance Undecorate(AnsiDecorations decorations)
    {
        SetStyleCore(_style.WithDecorations(_style.Decorations & ~decorations));
        return this;
    }

    /// <inheritdoc cref="AnsiWriter.CursorUp(int)"/>
    public TerminalInstance CursorUp(int n = 1)
    {
        lock (_outputLock) { _writerUnsafe!.CursorUp(n); }
        return this;
    }

    /// <inheritdoc cref="AnsiWriter.CursorDown(int)"/>
    public TerminalInstance CursorDown(int n = 1)
    {
        lock (_outputLock) { _writerUnsafe!.CursorDown(n); }
        return this;
    }

    /// <inheritdoc cref="AnsiWriter.CursorForward(int)"/>
    public TerminalInstance CursorForward(int n = 1)
    {
        lock (_outputLock) { _writerUnsafe!.CursorForward(n); }
        return this;
    }

    /// <inheritdoc cref="AnsiWriter.CursorBack(int)"/>
    public TerminalInstance CursorBack(int n = 1)
    {
        lock (_outputLock) { _writerUnsafe!.CursorBack(n); }
        return this;
    }

    /// <inheritdoc cref="AnsiWriter.NextLine(int)"/>
    public TerminalInstance NextLine(int n = 1)
    {
        lock (_outputLock) { _writerUnsafe!.NextLine(n); }
        return this;
    }

    /// <inheritdoc cref="AnsiWriter.PreviousLine(int)"/>
    public TerminalInstance PreviousLine(int n = 1)
    {
        lock (_outputLock) { _writerUnsafe!.PreviousLine(n); }
        return this;
    }

    /// <inheritdoc cref="AnsiWriter.CursorHorizontalAbsolute(int)"/>
    public TerminalInstance CursorHorizontalAbsolute(int col = 1)
    {
        lock (_outputLock) { _writerUnsafe!.CursorHorizontalAbsolute(col); }
        return this;
    }

    /// <inheritdoc cref="AnsiWriter.CursorVerticalAbsolute(int)"/>
    public TerminalInstance CursorVerticalAbsolute(int row = 1)
    {
        lock (_outputLock) { _writerUnsafe!.CursorVerticalAbsolute(row); }
        return this;
    }

    /// <inheritdoc cref="AnsiWriter.CursorPosition(int,int)"/>
    public TerminalInstance CursorPosition(int row, int col)
    {
        lock (_outputLock) { _writerUnsafe!.CursorPosition(row, col); }
        return this;
    }

    /// <inheritdoc cref="AnsiWriter.HorizontalAndVerticalPosition(int,int)"/>
    public TerminalInstance HorizontalAndVerticalPosition(int row, int col)
    {
        lock (_outputLock) { _writerUnsafe!.HorizontalAndVerticalPosition(row, col); }
        return this;
    }

    /// <inheritdoc cref="AnsiWriter.MoveTo(int,int)"/>
    public TerminalInstance MoveTo(int row, int col)
    {
        lock (_outputLock) { _writerUnsafe!.MoveTo(row, col); }
        return this;
    }

    /// <inheritdoc cref="AnsiWriter.Up(int)"/>
    public TerminalInstance Up(int n = 1)
    {
        lock (_outputLock) { _writerUnsafe!.Up(n); }
        return this;
    }

    /// <inheritdoc cref="AnsiWriter.Down(int)"/>
    public TerminalInstance Down(int n = 1)
    {
        lock (_outputLock) { _writerUnsafe!.Down(n); }
        return this;
    }

    /// <inheritdoc cref="AnsiWriter.Forward(int)"/>
    public TerminalInstance Forward(int n = 1)
    {
        lock (_outputLock) { _writerUnsafe!.Forward(n); }
        return this;
    }

    /// <inheritdoc cref="AnsiWriter.Back(int)"/>
    public TerminalInstance Back(int n = 1)
    {
        lock (_outputLock) { _writerUnsafe!.Back(n); }
        return this;
    }

    /// <inheritdoc cref="AnsiWriter.ReverseIndex()"/>
    public TerminalInstance ReverseIndex()
    {
        lock (_outputLock) { _writerUnsafe!.ReverseIndex(); }
        return this;
    }

    /// <inheritdoc cref="AnsiWriter.SaveCursor()"/>
    public TerminalInstance SaveCursor()
    {
        lock (_outputLock) { _writerUnsafe!.SaveCursor(); }
        return this;
    }

    /// <inheritdoc cref="AnsiWriter.HorizontalTabSet()"/>
    public TerminalInstance HorizontalTabSet()
    {
        lock (_outputLock) { _writerUnsafe!.HorizontalTabSet(); }
        return this;
    }

    /// <inheritdoc cref="AnsiWriter.CursorForwardTab(int)"/>
    public TerminalInstance CursorForwardTab(int n = 1)
    {
        lock (_outputLock) { _writerUnsafe!.CursorForwardTab(n); }
        return this;
    }

    /// <inheritdoc cref="AnsiWriter.CursorBackTab(int)"/>
    public TerminalInstance CursorBackTab(int n = 1)
    {
        lock (_outputLock) { _writerUnsafe!.CursorBackTab(n); }
        return this;
    }

    /// <inheritdoc cref="AnsiWriter.ClearTabStop()"/>
    public TerminalInstance ClearTabStop()
    {
        lock (_outputLock) { _writerUnsafe!.ClearTabStop(); }
        return this;
    }

    /// <inheritdoc cref="AnsiWriter.ClearAllTabStops()"/>
    public TerminalInstance ClearAllTabStops()
    {
        lock (_outputLock) { _writerUnsafe!.ClearAllTabStops(); }
        return this;
    }

    /// <inheritdoc cref="AnsiWriter.EnterLineDrawingMode()"/>
    public TerminalInstance EnterLineDrawingMode()
    {
        lock (_outputLock) { _writerUnsafe!.EnterLineDrawingMode(); }
        return this;
    }

    /// <inheritdoc cref="AnsiWriter.ExitLineDrawingMode()"/>
    public TerminalInstance ExitLineDrawingMode()
    {
        lock (_outputLock) { _writerUnsafe!.ExitLineDrawingMode(); }
        return this;
    }

    /// <inheritdoc cref="AnsiWriter.KeypadApplicationMode()"/>
    public TerminalInstance KeypadApplicationMode()
    {
        lock (_outputLock) { _writerUnsafe!.KeypadApplicationMode(); }
        return this;
    }

    /// <inheritdoc cref="AnsiWriter.KeypadNumericMode()"/>
    public TerminalInstance KeypadNumericMode()
    {
        lock (_outputLock) { _writerUnsafe!.KeypadNumericMode(); }
        return this;
    }

    /// <inheritdoc cref="AnsiWriter.CursorKeysApplicationMode(bool)"/>
    public TerminalInstance CursorKeysApplicationMode(bool enabled)
    {
        lock (_outputLock) { _writerUnsafe!.CursorKeysApplicationMode(enabled); }
        return this;
    }

    /// <inheritdoc cref="AnsiWriter.CursorBlinking(bool)"/>
    public TerminalInstance CursorBlinking(bool enabled)
    {
        lock (_outputLock) { _writerUnsafe!.CursorBlinking(enabled); }
        return this;
    }

    /// <inheritdoc cref="AnsiWriter.Columns132(bool)"/>
    public TerminalInstance Columns132(bool enabled)
    {
        lock (_outputLock) { _writerUnsafe!.Columns132(enabled); }
        return this;
    }

    /// <inheritdoc cref="AnsiWriter.SaveCursorPosition()"/>
    public TerminalInstance SaveCursorPosition()
    {
        lock (_outputLock) { _writerUnsafe!.SaveCursorPosition(); }
        return this;
    }

    /// <inheritdoc cref="AnsiWriter.RestoreCursor()"/>
    public TerminalInstance RestoreCursor()
    {
        lock (_outputLock) { _writerUnsafe!.RestoreCursor(); }
        return this;
    }

    /// <inheritdoc cref="AnsiWriter.RestoreCursorPosition()"/>
    public TerminalInstance RestoreCursorPosition()
    {
        lock (_outputLock) { _writerUnsafe!.RestoreCursorPosition(); }
        return this;
    }

    /// <inheritdoc cref="AnsiWriter.EraseInLine(int)"/>
    public TerminalInstance EraseInLine(int mode = 0)
    {
        lock (_outputLock) { _writerUnsafe!.EraseInLine(mode); }
        return this;
    }

    /// <inheritdoc cref="AnsiWriter.EraseInDisplay(int)"/>
    public TerminalInstance EraseInDisplay(int mode = 0)
    {
        lock (_outputLock) { _writerUnsafe!.EraseInDisplay(mode); }
        return this;
    }

    /// <inheritdoc cref="AnsiWriter.EraseScrollback()"/>
    public TerminalInstance EraseScrollback()
    {
        lock (_outputLock) { _writerUnsafe!.EraseScrollback(); }
        return this;
    }

    /// <inheritdoc cref="AnsiWriter.EraseCharacters(int)"/>
    public TerminalInstance EraseCharacters(int n = 1)
    {
        lock (_outputLock) { _writerUnsafe!.EraseCharacters(n); }
        return this;
    }

    /// <inheritdoc cref="AnsiWriter.InsertCharacters(int)"/>
    public TerminalInstance InsertCharacters(int n = 1)
    {
        lock (_outputLock) { _writerUnsafe!.InsertCharacters(n); }
        return this;
    }

    /// <inheritdoc cref="AnsiWriter.DeleteCharacters(int)"/>
    public TerminalInstance DeleteCharacters(int n = 1)
    {
        lock (_outputLock) { _writerUnsafe!.DeleteCharacters(n); }
        return this;
    }

    /// <inheritdoc cref="AnsiWriter.InsertLines(int)"/>
    public TerminalInstance InsertLines(int n = 1)
    {
        lock (_outputLock) { _writerUnsafe!.InsertLines(n); }
        return this;
    }

    /// <inheritdoc cref="AnsiWriter.DeleteLines(int)"/>
    public TerminalInstance DeleteLines(int n = 1)
    {
        lock (_outputLock) { _writerUnsafe!.DeleteLines(n); }
        return this;
    }

    /// <inheritdoc cref="AnsiWriter.ScrollUp(int)"/>
    public TerminalInstance ScrollUp(int n = 1)
    {
        lock (_outputLock) { _writerUnsafe!.ScrollUp(n); }
        return this;
    }

    /// <inheritdoc cref="AnsiWriter.ScrollDown(int)"/>
    public TerminalInstance ScrollDown(int n = 1)
    {
        lock (_outputLock) { _writerUnsafe!.ScrollDown(n); }
        return this;
    }

    /// <inheritdoc cref="AnsiWriter.SetScrollRegion(int,int)"/>
    public TerminalInstance SetScrollRegion(int top, int bottom)
    {
        lock (_outputLock) { _writerUnsafe!.SetScrollRegion(top, bottom); }
        return this;
    }

    /// <inheritdoc cref="AnsiWriter.ResetScrollRegion()"/>
    public TerminalInstance ResetScrollRegion()
    {
        lock (_outputLock) { _writerUnsafe!.ResetScrollRegion(); }
        return this;
    }

    /// <inheritdoc cref="AnsiWriter.SetMode(int,bool)"/>
    public TerminalInstance SetMode(int mode, bool enabled)
    {
        lock (_outputLock) { _writerUnsafe!.SetMode(mode, enabled); }
        return this;
    }

    /// <inheritdoc cref="AnsiWriter.PrivateMode(int,bool)"/>
    public TerminalInstance PrivateMode(int mode, bool enabled)
    {
        lock (_outputLock) { _writerUnsafe!.PrivateMode(mode, enabled); }
        return this;
    }

    /// <inheritdoc cref="AnsiWriter.CursorStyle(AnsiCursorStyle)"/>
    public TerminalInstance CursorStyle(AnsiCursorStyle style)
    {
        lock (_outputLock) { _writerUnsafe!.CursorStyle(style); }
        return this;
    }

    /// <inheritdoc cref="AnsiWriter.EraseLine(int)"/>
    public TerminalInstance EraseLine(int mode = 0)
    {
        lock (_outputLock) { _writerUnsafe!.EraseLine(mode); }
        return this;
    }

    /// <inheritdoc cref="AnsiWriter.EraseDisplay(int)"/>
    public TerminalInstance EraseDisplay(int mode = 0)
    {
        lock (_outputLock) { _writerUnsafe!.EraseDisplay(mode); }
        return this;
    }

    /// <inheritdoc cref="AnsiWriter.ShowCursor(bool)"/>
    public TerminalInstance ShowCursor(bool visible)
    {
        lock (_outputLock) { _writerUnsafe!.ShowCursor(visible); }
        return this;
    }

    /// <inheritdoc cref="AnsiWriter.CursorVisible(bool)"/>
    public TerminalInstance CursorVisible(bool visible)
    {
        lock (_outputLock) { _writerUnsafe!.CursorVisible(visible); }
        return this;
    }

    /// <inheritdoc cref="AnsiWriter.EnterAlternateScreen()"/>
    public TerminalInstance EnterAlternateScreen()
    {
        lock (_outputLock) { _writerUnsafe!.EnterAlternateScreen(); }
        return this;
    }

    /// <inheritdoc cref="AnsiWriter.LeaveAlternateScreen()"/>
    public TerminalInstance LeaveAlternateScreen()
    {
        lock (_outputLock) { _writerUnsafe!.LeaveAlternateScreen(); }
        return this;
    }

    /// <inheritdoc cref="AnsiWriter.AlternateScreen(bool)"/>
    public TerminalInstance AlternateScreen(bool enabled)
    {
        lock (_outputLock) { _writerUnsafe!.AlternateScreen(enabled); }
        return this;
    }

    /// <inheritdoc cref="AnsiWriter.SoftReset()"/>
    public TerminalInstance SoftReset()
    {
        lock (_outputLock) { _writerUnsafe!.SoftReset(); }
        return this;
    }

    /// <inheritdoc cref="AnsiWriter.RequestCursorPosition()"/>
    public TerminalInstance RequestCursorPosition()
    {
        lock (_outputLock) { _writerUnsafe!.RequestCursorPosition(); }
        return this;
    }

    /// <inheritdoc cref="AnsiWriter.RequestDeviceAttributes()"/>
    public TerminalInstance RequestDeviceAttributes()
    {
        lock (_outputLock) { _writerUnsafe!.RequestDeviceAttributes(); }
        return this;
    }

    /// <inheritdoc cref="AnsiWriter.WindowTitle(System.ReadOnlySpan{char})"/>
    public TerminalInstance WindowTitle(ReadOnlySpan<char> title)
    {
        lock (_outputLock) { _writerUnsafe!.WindowTitle(title); }
        return this;
    }

    /// <inheritdoc cref="AnsiWriter.IconAndWindowTitle(System.ReadOnlySpan{char})"/>
    public TerminalInstance IconAndWindowTitle(ReadOnlySpan<char> title)
    {
        lock (_outputLock) { _writerUnsafe!.IconAndWindowTitle(title); }
        return this;
    }

    /// <inheritdoc cref="AnsiWriter.SetPaletteColor(int,byte,byte,byte)"/>
    public TerminalInstance SetPaletteColor(int index, byte r, byte g, byte b)
    {
        lock (_outputLock) { _writerUnsafe!.SetPaletteColor(index, r, g, b); }
        return this;
    }

    /// <inheritdoc cref="AnsiWriter.BeginLink(string,string?)"/>
    public TerminalInstance BeginLink(string uri, string? id = null)
    {
        lock (_outputLock) { _writerUnsafe!.BeginLink(uri, id); }
        return this;
    }

    /// <inheritdoc cref="AnsiWriter.EndLink()"/>
    public TerminalInstance EndLink()
    {
        lock (_outputLock) { _writerUnsafe!.EndLink(); }
        return this;
    }

    /// <inheritdoc cref="AnsiWriter.WriteCursorPositionReport(int,int)"/>
    public TerminalInstance WriteCursorPositionReport(int row, int column)
    {
        lock (_outputLock) { _writerUnsafe!.WriteCursorPositionReport(row, column); }
        return this;
    }

    /// <inheritdoc cref="AnsiWriter.WriteSs3(char)"/>
    public TerminalInstance WriteSs3(char final)
    {
        lock (_outputLock) { _writerUnsafe!.WriteSs3(final); }
        return this;
    }

    /// <inheritdoc cref="AnsiWriter.WriteSgrMouseEvent(AnsiMouseEvent)"/>
    public TerminalInstance WriteSgrMouseEvent(AnsiMouseEvent mouseEvent)
    {
        lock (_outputLock) { _writerUnsafe!.WriteSgrMouseEvent(mouseEvent); }
        return this;
    }

    /// <inheritdoc cref="AnsiWriter.WriteKeyEvent(AnsiKeyEvent,bool)"/>
    public TerminalInstance WriteKeyEvent(AnsiKeyEvent keyEvent, bool applicationCursorKeysMode = false)
    {
        lock (_outputLock) { _writerUnsafe!.WriteKeyEvent(keyEvent, applicationCursorKeysMode); }
        return this;
    }
}
