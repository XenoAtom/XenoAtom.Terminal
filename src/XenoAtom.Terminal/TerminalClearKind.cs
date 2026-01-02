// Copyright (c) Alexandre Mutel. All rights reserved.
// Licensed under the BSD-Clause 2 license.
// See license.txt file in the project root for full license information.

namespace XenoAtom.Terminal;

/// <summary>
/// Specifies what region should be cleared.
/// </summary>
public enum TerminalClearKind
{
    /// <summary>
    /// Clears the current line.
    /// </summary>
    Line,

    /// <summary>
    /// Clears the visible screen.
    /// </summary>
    Screen,

    /// <summary>
    /// Clears the visible screen and the scrollback buffer (where supported).
    /// </summary>
    ScreenAndScrollback
}

