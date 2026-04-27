// Copyright (c) Alexandre Mutel. All rights reserved.
// Licensed under the BSD-Clause 2 license.
// See license.txt file in the project root for full license information.

namespace XenoAtom.Terminal;

/// <summary>
/// Identifies a terminal graphics protocol.
/// </summary>
public enum TerminalGraphicsProtocol
{
    /// <summary>
    /// No terminal graphics protocol is available or selected.
    /// </summary>
    None = 0,

    /// <summary>
    /// Kitty graphics protocol.
    /// </summary>
    Kitty = 1,

    /// <summary>
    /// iTerm2 inline image protocol (OSC 1337 File).
    /// </summary>
    ITerm2 = 2,

    /// <summary>
    /// DEC Sixel graphics.
    /// </summary>
    Sixel = 3,
}