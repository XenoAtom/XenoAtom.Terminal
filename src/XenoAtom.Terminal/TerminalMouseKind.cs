// Copyright (c) Alexandre Mutel. All rights reserved.
// Licensed under the BSD-Clause 2 license.
// See license.txt file in the project root for full license information.

namespace XenoAtom.Terminal;

/// <summary>
/// Specifies the kind of mouse event.
/// </summary>
public enum TerminalMouseKind
{
    /// <summary>
    /// Mouse moved.
    /// </summary>
    Move,

    /// <summary>
    /// Button pressed.
    /// </summary>
    Down,

    /// <summary>
    /// Button released.
    /// </summary>
    Up,

    /// <summary>
    /// Button double clicked.
    /// </summary>
    DoubleClick,

    /// <summary>
    /// Mouse wheel event.
    /// </summary>
    Wheel,

    /// <summary>
    /// Mouse moved while a button is pressed.
    /// </summary>
    Drag
}

