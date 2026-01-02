// Copyright (c) Alexandre Mutel. All rights reserved.
// Licensed under the BSD-Clause 2 license.
// See license.txt file in the project root for full license information.

namespace XenoAtom.Terminal;

/// <summary>
/// Represents the color feature level supported by the output target.
/// </summary>
public enum TerminalColorLevel
{
    /// <summary>
    /// No color support.
    /// </summary>
    None,

    /// <summary>
    /// The 16-color palette (8 normal + 8 bright).
    /// </summary>
    Color16,

    /// <summary>
    /// The 256-color indexed palette.
    /// </summary>
    Color256,

    /// <summary>
    /// Truecolor (24-bit RGB).
    /// </summary>
    TrueColor
}

