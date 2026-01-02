// Copyright (c) Alexandre Mutel. All rights reserved.
// Licensed under the BSD-Clause 2 license.
// See license.txt file in the project root for full license information.

namespace XenoAtom.Terminal;

/// <summary>
/// Represents a process-level signal that was captured by the terminal input layer.
/// </summary>
public enum TerminalSignalKind
{
    /// <summary>
    /// Interrupt signal (typically Ctrl+C).
    /// </summary>
    Interrupt,

    /// <summary>
    /// Break signal (typically Ctrl+Break).
    /// </summary>
    Break,
}

