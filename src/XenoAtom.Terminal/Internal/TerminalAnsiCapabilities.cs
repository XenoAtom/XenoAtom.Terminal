// Copyright (c) Alexandre Mutel. All rights reserved.
// Licensed under the BSD-Clause 2 license.
// See license.txt file in the project root for full license information.

using XenoAtom.Ansi;

namespace XenoAtom.Terminal.Internal;

internal static class TerminalAnsiCapabilities
{
    public static AnsiCapabilities Create(TerminalCapabilities caps, TerminalOptions options)
    {
        var colorLevel = caps.ColorLevel switch
        {
            TerminalColorLevel.None => AnsiColorLevel.None,
            TerminalColorLevel.Color16 => AnsiColorLevel.Colors16,
            TerminalColorLevel.Color256 => AnsiColorLevel.Colors256,
            _ => AnsiColorLevel.TrueColor,
        };

        return AnsiCapabilities.Default with
        {
            AnsiEnabled = caps.AnsiEnabled,
            ColorLevel = colorLevel,
            SupportsOsc8 = caps.SupportsOsc8Links,
            SupportsPrivateModes = caps.SupportsPrivateModes,
            Prefer7BitC1 = options.Prefer7BitC1,
        };
    }
}

