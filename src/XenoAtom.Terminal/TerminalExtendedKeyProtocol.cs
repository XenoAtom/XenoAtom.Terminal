// Copyright (c) Alexandre Mutel. All rights reserved.
// Licensed under the BSD-Clause 2 license.
// See license.txt file in the project root for full license information.

namespace XenoAtom.Terminal;

/// <summary>
/// Identifies the protocol used to provide extended keyboard input.
/// </summary>
public enum TerminalExtendedKeyProtocol
{
    /// <summary>
    /// No extended keyboard protocol is available.
    /// </summary>
    None,

    /// <summary>
    /// Native Windows console input records are used.
    /// </summary>
    WindowsConsole,

    /// <summary>
    /// The Kitty keyboard protocol is enabled for ANSI/VT terminal input.
    /// </summary>
    KittyKeyboard,
}
