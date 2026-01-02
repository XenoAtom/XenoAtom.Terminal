// Copyright (c) Alexandre Mutel. All rights reserved.
// Licensed under the BSD-Clause 2 license.
// See license.txt file in the project root for full license information.

namespace XenoAtom.Terminal.Internal;

internal static class TerminalWindowsKeyFiltering
{
    // Virtual-Key codes from WinUser.h (subset).
    private const ushort VK_SHIFT = 0x10;
    private const ushort VK_CONTROL = 0x11;
    private const ushort VK_MENU = 0x12; // Alt

    private const ushort VK_LSHIFT = 0xA0;
    private const ushort VK_RSHIFT = 0xA1;
    private const ushort VK_LCONTROL = 0xA2;
    private const ushort VK_RCONTROL = 0xA3;
    private const ushort VK_LMENU = 0xA4;
    private const ushort VK_RMENU = 0xA5;

    public static bool IsStandaloneModifierKey(ushort virtualKey, char? ch)
    {
        // Standalone modifier press has no character payload.
        if (ch is not null)
        {
            return false;
        }

        return virtualKey is VK_SHIFT or VK_CONTROL or VK_MENU
               or VK_LSHIFT or VK_RSHIFT
               or VK_LCONTROL or VK_RCONTROL
               or VK_LMENU or VK_RMENU;
    }
}

