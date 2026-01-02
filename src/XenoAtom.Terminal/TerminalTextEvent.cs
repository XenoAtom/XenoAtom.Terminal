// Copyright (c) Alexandre Mutel. All rights reserved.
// Licensed under the BSD-Clause 2 license.
// See license.txt file in the project root for full license information.

namespace XenoAtom.Terminal;

/// <summary>
/// Represents a text input event that is not better represented as a <see cref="TerminalKeyEvent"/>.
/// </summary>
public sealed record TerminalTextEvent : TerminalEvent
{
    /// <summary>
    /// Gets the text produced by the input method.
    /// </summary>
    public required string Text { get; init; }
}
