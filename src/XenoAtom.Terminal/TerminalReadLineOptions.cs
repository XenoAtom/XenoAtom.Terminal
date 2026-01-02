// Copyright (c) Alexandre Mutel. All rights reserved.
// Licensed under the BSD-Clause 2 license.
// See license.txt file in the project root for full license information.

namespace XenoAtom.Terminal;

/// <summary>
/// Options controlling <see cref="Terminal.ReadLine(TerminalReadLineOptions?)"/> and
/// <see cref="Terminal.ReadLineAsync(TerminalReadLineOptions?, System.Threading.CancellationToken)"/>.
/// </summary>
public sealed class TerminalReadLineOptions
{
    /// <summary>
     /// Echoes typed text to the terminal output.
     /// </summary>
    public bool Echo { get; set; } = true;

    /// <summary>
    /// When enabled, Ctrl+C / Ctrl+Break interrupts the read.
    /// </summary>
    public bool CancelOnSignal { get; set; } = true;
}
