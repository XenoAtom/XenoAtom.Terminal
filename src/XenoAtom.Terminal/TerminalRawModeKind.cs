// Copyright (c) Alexandre Mutel. All rights reserved.
// Licensed under the BSD-Clause 2 license.
// See license.txt file in the project root for full license information.

namespace XenoAtom.Terminal;

/// <summary>
/// Specifies the raw input mode kind.
/// </summary>
public enum TerminalRawModeKind
{
    /// <summary>
    /// A cbreak-like mode (no line buffering, still allows some processing depending on platform).
    /// </summary>
    CBreak,

    /// <summary>
    /// A raw mode (as raw as the platform supports).
    /// </summary>
    Raw
}

