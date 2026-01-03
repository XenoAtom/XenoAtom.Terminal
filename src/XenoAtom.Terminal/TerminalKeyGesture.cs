// Copyright (c) Alexandre Mutel. All rights reserved.
// Licensed under the BSD-Clause 2 license.
// See license.txt file in the project root for full license information.

namespace XenoAtom.Terminal;

/// <summary>
/// Represents a key gesture for binding editor commands (key + optional char + modifiers).
/// </summary>
public readonly record struct TerminalKeyGesture(TerminalKey Key, char? Char, TerminalModifiers Modifiers)
{
    /// <summary>
    /// Creates a gesture from a key event.
    /// </summary>
    public static TerminalKeyGesture From(TerminalKeyEvent key)
    {
        ArgumentNullException.ThrowIfNull(key);
        return new TerminalKeyGesture(key.Key, key.Char, key.Modifiers);
    }
}

