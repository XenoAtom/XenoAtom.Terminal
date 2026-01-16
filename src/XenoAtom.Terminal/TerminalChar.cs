// Copyright (c) Alexandre Mutel. All rights reserved.
// Licensed under the BSD-Clause 2 license.
// See license.txt file in the project root for full license information.

namespace XenoAtom.Terminal;

/// <summary>
/// Provides control-character constants and helpers (Ctrl+A through Ctrl+Z).
/// </summary>
public static class TerminalChar
{
    /// <summary>
    /// Ctrl+A (U+0001).
    /// </summary>
    public const char CtrlA = '\x01';

    /// <summary>
    /// Ctrl+B (U+0002).
    /// </summary>
    public const char CtrlB = '\x02';

    /// <summary>
    /// Ctrl+C (U+0003).
    /// </summary>
    public const char CtrlC = '\x03';

    /// <summary>
    /// Ctrl+D (U+0004).
    /// </summary>
    public const char CtrlD = '\x04';

    /// <summary>
    /// Ctrl+E (U+0005).
    /// </summary>
    public const char CtrlE = '\x05';

    /// <summary>
    /// Ctrl+F (U+0006).
    /// </summary>
    public const char CtrlF = '\x06';

    /// <summary>
    /// Ctrl+G (U+0007).
    /// </summary>
    public const char CtrlG = '\x07';

    /// <summary>
    /// Ctrl+H (U+0008).
    /// </summary>
    public const char CtrlH = '\x08';

    /// <summary>
    /// Ctrl+I (U+0009).
    /// </summary>
    public const char CtrlI = '\x09';

    /// <summary>
    /// Ctrl+J (U+000A).
    /// </summary>
    public const char CtrlJ = '\x0A';

    /// <summary>
    /// Ctrl+K (U+000B).
    /// </summary>
    public const char CtrlK = '\x0B';

    /// <summary>
    /// Ctrl+L (U+000C).
    /// </summary>
    public const char CtrlL = '\x0C';

    /// <summary>
    /// Ctrl+M (U+000D).
    /// </summary>
    public const char CtrlM = '\x0D';

    /// <summary>
    /// Ctrl+N (U+000E).
    /// </summary>
    public const char CtrlN = '\x0E';

    /// <summary>
    /// Ctrl+O (U+000F).
    /// </summary>
    public const char CtrlO = '\x0F';

    /// <summary>
    /// Ctrl+P (U+0010).
    /// </summary>
    public const char CtrlP = '\x10';

    /// <summary>
    /// Ctrl+Q (U+0011).
    /// </summary>
    public const char CtrlQ = '\x11';

    /// <summary>
    /// Ctrl+R (U+0012).
    /// </summary>
    public const char CtrlR = '\x12';

    /// <summary>
    /// Ctrl+S (U+0013).
    /// </summary>
    public const char CtrlS = '\x13';

    /// <summary>
    /// Ctrl+T (U+0014).
    /// </summary>
    public const char CtrlT = '\x14';

    /// <summary>
    /// Ctrl+U (U+0015).
    /// </summary>
    public const char CtrlU = '\x15';

    /// <summary>
    /// Ctrl+V (U+0016).
    /// </summary>
    public const char CtrlV = '\x16';

    /// <summary>
    /// Ctrl+W (U+0017).
    /// </summary>
    public const char CtrlW = '\x17';

    /// <summary>
    /// Ctrl+X (U+0018).
    /// </summary>
    public const char CtrlX = '\x18';

    /// <summary>
    /// Ctrl+Y (U+0019).
    /// </summary>
    public const char CtrlY = '\x19';

    /// <summary>
    /// Ctrl+Z (U+001A).
    /// </summary>
    public const char CtrlZ = '\x1A';

    /// <summary>
    /// Gets the control character for the specified letter (A-Z).
    /// </summary>
    /// <exception cref="ArgumentOutOfRangeException">
    /// Thrown when <paramref name="letter"/> is not between A and Z.
    /// </exception>
    public static char Ctrl(char letter)
    {
        if (letter is >= 'a' and <= 'z')
        {
            letter = (char)(letter - 32);
        }

        if (letter is < 'A' or > 'Z')
        {
            throw new ArgumentOutOfRangeException(nameof(letter), "Expected a letter A-Z.");
        }

        return (char)(CtrlA + (letter - 'A'));
    }
}
