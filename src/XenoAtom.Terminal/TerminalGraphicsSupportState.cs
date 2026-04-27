// Copyright (c) Alexandre Mutel. All rights reserved.
// Licensed under the BSD-Clause 2 license.
// See license.txt file in the project root for full license information.

namespace XenoAtom.Terminal;

/// <summary>
/// Describes how graphics support was determined.
/// </summary>
public enum TerminalGraphicsSupportState
{
    /// <summary>
    /// Graphics are not supported.
    /// </summary>
    Unsupported = 0,

    /// <summary>
    /// Graphics are supported by the environment but disabled by options or policy.
    /// </summary>
    Disabled = 1,

    /// <summary>
    /// Graphics were enabled from environment heuristics.
    /// </summary>
    Heuristic = 2,

    /// <summary>
    /// Graphics were confirmed by an active probe.
    /// </summary>
    Confirmed = 3,

    /// <summary>
    /// Graphics were forced by explicit options or environment override.
    /// </summary>
    Forced = 4,
}