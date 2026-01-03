// Copyright (c) Alexandre Mutel. All rights reserved.
// Licensed under the BSD-Clause 2 license.
// See license.txt file in the project root for full license information.

namespace XenoAtom.Terminal;

/// <summary>
/// Controls whether the Windows backend uses a VT (ANSI) input decoder for ANSI/VT escape sequences.
/// </summary>
public enum TerminalWindowsVtInputDecoderMode
{
    /// <summary>
    /// Enables VT input decoding only when the console input mode already has <c>ENABLE_VIRTUAL_TERMINAL_INPUT</c> set.
    /// </summary>
    Auto,

    /// <summary>
    /// Requests enabling <c>ENABLE_VIRTUAL_TERMINAL_INPUT</c> and uses VT input decoding when possible.
    /// </summary>
    Enabled,

    /// <summary>
    /// Disables VT input decoding.
    /// </summary>
    Disabled,
}
