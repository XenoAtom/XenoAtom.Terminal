// Copyright (c) Alexandre Mutel. All rights reserved.
// Licensed under the BSD-Clause 2 license.
// See license.txt file in the project root for full license information.

namespace XenoAtom.Terminal;

/// <summary>
/// Specifies the raw input mode kind.
/// </summary>
public enum TerminalRawModeKind
{
    /// <summary>
    /// A cbreak-like mode suitable for TUIs (no line buffering, no echo).
    /// </summary>
    /// <remarks>
    /// This mode is designed as a portable default:
    /// <list type="bullet">
    /// <item><description>Disables canonical input (characters are available immediately).</description></item>
    /// <item><description>Disables input echo.</description></item>
    /// <item><description>On Unix, disables software flow control (so Ctrl+S/Ctrl+Q are delivered as input) and disables CR-to-NL translation (so Enter typically yields <c>'\r'</c>).</description></item>
    /// <item><description>On Windows, keeps the console in a processed input mode by default so Ctrl+C remains a signal unless <see cref="TerminalOptions.TreatControlCAsInput"/> is enabled.</description></item>
    /// </list>
    /// </remarks>
    CBreak,

    /// <summary>
    /// A raw mode (as raw as the platform supports).
    /// </summary>
    /// <remarks>
    /// On Unix this maps to <c>cfmakeraw</c> (disables <c>ISIG</c> and most input/output processing).
    /// On Windows this disables processed input so Ctrl+C can be delivered as a key when requested.
    /// </remarks>
    Raw
}

