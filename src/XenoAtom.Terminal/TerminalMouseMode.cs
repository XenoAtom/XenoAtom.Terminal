// Copyright (c) Alexandre Mutel. All rights reserved.
// Licensed under the BSD-Clause 2 license.
// See license.txt file in the project root for full license information.

namespace XenoAtom.Terminal;

/// <summary>
/// Controls which mouse events are reported.
/// </summary>
public enum TerminalMouseMode
{
    /// <summary>
    /// Mouse reporting is disabled.
    /// </summary>
    Off,

    /// <summary>
    /// Reports button clicks.
    /// </summary>
    Clicks,

    /// <summary>
    /// Reports clicks and drag events.
    /// </summary>
    Drag,

    /// <summary>
    /// Reports movement events (usually includes drag).
    /// </summary>
    Move
}

