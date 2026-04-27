// Copyright (c) Alexandre Mutel. All rights reserved.
// Licensed under the BSD-Clause 2 license.
// See license.txt file in the project root for full license information.

namespace XenoAtom.Terminal;

/// <summary>
/// Describes the presentation model of the selected graphics protocol.
/// </summary>
public enum TerminalGraphicsPresentationModel
{
    /// <summary>
    /// No graphics presentation model is available.
    /// </summary>
    None = 0,

    /// <summary>
    /// Graphics are streamed at the cursor and must be redrawn when the region changes.
    /// </summary>
    Streamed = 1,

    /// <summary>
    /// Graphics can be retained by the terminal and updated or deleted by id.
    /// </summary>
    Retained = 2,
}