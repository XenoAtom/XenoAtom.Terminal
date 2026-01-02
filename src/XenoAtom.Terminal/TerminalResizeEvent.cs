// Copyright (c) Alexandre Mutel. All rights reserved.
// Licensed under the BSD-Clause 2 license.
// See license.txt file in the project root for full license information.

namespace XenoAtom.Terminal;

/// <summary>
/// Represents a terminal size change event.
/// </summary>
public sealed record TerminalResizeEvent : TerminalEvent
{
    /// <summary>
    /// Gets the new terminal size.
    /// </summary>
    public required TerminalSize Size { get; init; }
}
