// Copyright (c) Alexandre Mutel. All rights reserved.
// Licensed under the BSD-Clause 2 license.
// See license.txt file in the project root for full license information.

namespace XenoAtom.Terminal;

/// <summary>
/// Represents a keyboard key press event.
/// </summary>
public sealed record TerminalKeyEvent : TerminalEvent
{
    /// <summary>
    /// Gets the logical key.
    /// </summary>
    public required TerminalKey Key { get; init; }

    /// <summary>
    /// Gets the Unicode character produced by this key event, if any.
    /// </summary>
    /// <remarks>
    /// For non-text keys, or for key combinations that do not produce a character, this value can be <see langword="null"/>.
    /// </remarks>
    public char? Char { get; init; }

    /// <summary>
    /// Gets the modifier keys active for this event.
    /// </summary>
    public TerminalModifiers Modifiers { get; init; }
}
