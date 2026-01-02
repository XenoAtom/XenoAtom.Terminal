// Copyright (c) Alexandre Mutel. All rights reserved.
// Licensed under the BSD-Clause 2 license.
// See license.txt file in the project root for full license information.

namespace XenoAtom.Terminal;

/// <summary>
/// Represents a paste event (typically bracketed paste).
/// </summary>
public sealed record TerminalPasteEvent : TerminalEvent
{
    /// <summary>
    /// Gets the pasted text.
    /// </summary>
    public required string Text { get; init; }
}
