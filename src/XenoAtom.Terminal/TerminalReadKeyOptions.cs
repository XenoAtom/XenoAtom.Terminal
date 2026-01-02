// Copyright (c) Alexandre Mutel. All rights reserved.
// Licensed under the BSD-Clause 2 license.
// See license.txt file in the project root for full license information.

namespace XenoAtom.Terminal;

/// <summary>
/// Options controlling <see cref="Terminal.ReadKey(TerminalReadKeyOptions?)"/> and <see cref="Terminal.ReadKeyAsync(TerminalReadKeyOptions?, CancellationToken)"/>.
/// </summary>
public sealed class TerminalReadKeyOptions
{
    /// <summary>
    /// When <see langword="true"/>, the key is not echoed to the terminal output.
    /// </summary>
    public bool Intercept { get; set; }
}

