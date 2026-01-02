// Copyright (c) Alexandre Mutel. All rights reserved.
// Licensed under the BSD-Clause 2 license.
// See license.txt file in the project root for full license information.

namespace XenoAtom.Terminal;

/// <summary>
/// Represents a mouse event (move, button, drag, wheel).
/// </summary>
public sealed record TerminalMouseEvent : TerminalEvent
{
    /// <summary>
    /// Gets the mouse column coordinate (0-based).
    /// </summary>
    public required int X { get; init; }

    /// <summary>
    /// Gets the mouse row coordinate (0-based).
    /// </summary>
    public required int Y { get; init; }

    /// <summary>
    /// Gets the mouse button associated with this event.
    /// </summary>
    public TerminalMouseButton Button { get; init; }

    /// <summary>
    /// Gets the mouse event kind.
    /// </summary>
    public required TerminalMouseKind Kind { get; init; }

    /// <summary>
    /// Gets the modifier keys active for this event.
    /// </summary>
    public TerminalModifiers Modifiers { get; init; }

    /// <summary>
    /// Gets the wheel delta for wheel events.
    /// </summary>
    public int WheelDelta { get; init; }
}
