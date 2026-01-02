// Copyright (c) Alexandre Mutel. All rights reserved.
// Licensed under the BSD-Clause 2 license.
// See license.txt file in the project root for full license information.

namespace XenoAtom.Terminal;

/// <summary>
/// Represents a terminal signal event (for example Ctrl+C/Ctrl+Break).
/// </summary>
public sealed record TerminalSignalEvent : TerminalEvent
{
    /// <summary>
    /// Gets the signal kind.
    /// </summary>
    public required TerminalSignalKind Kind { get; init; }
}
