// Copyright (c) Alexandre Mutel. All rights reserved.
// Licensed under the BSD-Clause 2 license.
// See license.txt file in the project root for full license information.

namespace XenoAtom.Terminal;

/// <summary>
/// Modifier keys active for an input event.
/// </summary>
[Flags]
public enum TerminalModifiers
{
    /// <summary>
    /// No modifiers.
    /// </summary>
    None = 0,

    /// <summary>
    /// Shift is pressed.
    /// </summary>
    Shift = 1 << 0,

    /// <summary>
    /// Control is pressed.
    /// </summary>
    Ctrl = 1 << 1,

    /// <summary>
    /// Alt is pressed.
    /// </summary>
    Alt = 1 << 2,

    /// <summary>
    /// Meta/Super/Windows key is pressed (if available).
    /// </summary>
    Meta = 1 << 3,
}

