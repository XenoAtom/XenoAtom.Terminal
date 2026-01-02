// Copyright (c) Alexandre Mutel. All rights reserved.
// Licensed under the BSD-Clause 2 license.
// See license.txt file in the project root for full license information.

namespace XenoAtom.Terminal.Internal;

internal static class TerminalKeyMappings
{
    public static ConsoleModifiers ToConsoleModifiers(TerminalModifiers modifiers)
    {
        ConsoleModifiers console = 0;
        if (modifiers.HasFlag(TerminalModifiers.Shift)) console |= ConsoleModifiers.Shift;
        if (modifiers.HasFlag(TerminalModifiers.Alt)) console |= ConsoleModifiers.Alt;
        if (modifiers.HasFlag(TerminalModifiers.Ctrl)) console |= ConsoleModifiers.Control;
        return console;
    }

    public static TerminalModifiers FromConsoleModifiers(ConsoleModifiers modifiers)
    {
        TerminalModifiers terminal = TerminalModifiers.None;
        if ((modifiers & ConsoleModifiers.Shift) != 0) terminal |= TerminalModifiers.Shift;
        if ((modifiers & ConsoleModifiers.Alt) != 0) terminal |= TerminalModifiers.Alt;
        if ((modifiers & ConsoleModifiers.Control) != 0) terminal |= TerminalModifiers.Ctrl;
        return terminal;
    }

    public static ConsoleKey ToConsoleKey(TerminalKey key) => key switch
    {
        TerminalKey.Enter => ConsoleKey.Enter,
        TerminalKey.Escape => ConsoleKey.Escape,
        TerminalKey.Backspace => ConsoleKey.Backspace,
        TerminalKey.Tab => ConsoleKey.Tab,
        TerminalKey.Space => ConsoleKey.Spacebar,
        TerminalKey.Up => ConsoleKey.UpArrow,
        TerminalKey.Down => ConsoleKey.DownArrow,
        TerminalKey.Left => ConsoleKey.LeftArrow,
        TerminalKey.Right => ConsoleKey.RightArrow,
        TerminalKey.Home => ConsoleKey.Home,
        TerminalKey.End => ConsoleKey.End,
        TerminalKey.PageUp => ConsoleKey.PageUp,
        TerminalKey.PageDown => ConsoleKey.PageDown,
        TerminalKey.Insert => ConsoleKey.Insert,
        TerminalKey.Delete => ConsoleKey.Delete,
        TerminalKey.F1 => ConsoleKey.F1,
        TerminalKey.F2 => ConsoleKey.F2,
        TerminalKey.F3 => ConsoleKey.F3,
        TerminalKey.F4 => ConsoleKey.F4,
        TerminalKey.F5 => ConsoleKey.F5,
        TerminalKey.F6 => ConsoleKey.F6,
        TerminalKey.F7 => ConsoleKey.F7,
        TerminalKey.F8 => ConsoleKey.F8,
        TerminalKey.F9 => ConsoleKey.F9,
        TerminalKey.F10 => ConsoleKey.F10,
        TerminalKey.F11 => ConsoleKey.F11,
        TerminalKey.F12 => ConsoleKey.F12,
        _ => ConsoleKey.NoName,
    };

    public static TerminalKey FromConsoleKey(ConsoleKey key) => key switch
    {
        ConsoleKey.Enter => TerminalKey.Enter,
        ConsoleKey.Escape => TerminalKey.Escape,
        ConsoleKey.Backspace => TerminalKey.Backspace,
        ConsoleKey.Tab => TerminalKey.Tab,
        ConsoleKey.Spacebar => TerminalKey.Space,
        ConsoleKey.UpArrow => TerminalKey.Up,
        ConsoleKey.DownArrow => TerminalKey.Down,
        ConsoleKey.LeftArrow => TerminalKey.Left,
        ConsoleKey.RightArrow => TerminalKey.Right,
        ConsoleKey.Home => TerminalKey.Home,
        ConsoleKey.End => TerminalKey.End,
        ConsoleKey.PageUp => TerminalKey.PageUp,
        ConsoleKey.PageDown => TerminalKey.PageDown,
        ConsoleKey.Insert => TerminalKey.Insert,
        ConsoleKey.Delete => TerminalKey.Delete,
        ConsoleKey.F1 => TerminalKey.F1,
        ConsoleKey.F2 => TerminalKey.F2,
        ConsoleKey.F3 => TerminalKey.F3,
        ConsoleKey.F4 => TerminalKey.F4,
        ConsoleKey.F5 => TerminalKey.F5,
        ConsoleKey.F6 => TerminalKey.F6,
        ConsoleKey.F7 => TerminalKey.F7,
        ConsoleKey.F8 => TerminalKey.F8,
        ConsoleKey.F9 => TerminalKey.F9,
        ConsoleKey.F10 => TerminalKey.F10,
        ConsoleKey.F11 => TerminalKey.F11,
        ConsoleKey.F12 => TerminalKey.F12,
        _ => TerminalKey.Unknown,
    };
}

