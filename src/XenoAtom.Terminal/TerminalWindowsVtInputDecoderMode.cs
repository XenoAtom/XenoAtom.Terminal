// Copyright (c) Alexandre Mutel. All rights reserved.
// Licensed under the BSD-Clause 2 license.
// See license.txt file in the project root for full license information.

namespace XenoAtom.Terminal;

/// <summary>
/// Controls whether the Windows backend uses a VT (ANSI) input decoder for key/mouse sequences (ConPTY-style input).
/// </summary>
public enum TerminalWindowsVtInputDecoderMode
{
    /// <summary>
    /// Enables VT input decoding when a ConPTY-style host is detected (e.g. Windows Terminal, VS Code terminal).
    /// </summary>
    Auto,

    /// <summary>
    /// Always enables VT input decoding when possible.
    /// </summary>
    Enabled,

    /// <summary>
    /// Disables VT input decoding.
    /// </summary>
    Disabled,
}

