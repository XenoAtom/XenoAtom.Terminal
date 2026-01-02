// Copyright (c) Alexandre Mutel. All rights reserved.
// Licensed under the BSD-Clause 2 license.
// See license.txt file in the project root for full license information.

namespace XenoAtom.Terminal;

/// <summary>
/// Represents a console-like key input record.
/// </summary>
public readonly record struct TerminalKeyInfo(TerminalKey Key, char KeyChar, TerminalModifiers Modifiers)
{
    /// <summary>
    /// Converts this instance to a <see cref="ConsoleKeyInfo"/> (best effort).
    /// </summary>
    public ConsoleKeyInfo ToConsoleKeyInfo()
    {
        var consoleKey = Internal.TerminalKeyMappings.ToConsoleKey(Key);
        var modifiers = Internal.TerminalKeyMappings.ToConsoleModifiers(Modifiers);
        return new ConsoleKeyInfo(KeyChar, consoleKey, (modifiers & ConsoleModifiers.Shift) != 0, (modifiers & ConsoleModifiers.Alt) != 0, (modifiers & ConsoleModifiers.Control) != 0);
    }

    /// <summary>
    /// Creates an instance from a <see cref="ConsoleKeyInfo"/> (best effort).
    /// </summary>
    public static TerminalKeyInfo FromConsoleKeyInfo(ConsoleKeyInfo info)
    {
        var key = Internal.TerminalKeyMappings.FromConsoleKey(info.Key);
        var modifiers = Internal.TerminalKeyMappings.FromConsoleModifiers(info.Modifiers);
        return new TerminalKeyInfo(key, info.KeyChar, modifiers);
    }
}
