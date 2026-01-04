// Copyright (c) Alexandre Mutel. All rights reserved.
// Licensed under the BSD-Clause 2 license.
// See license.txt file in the project root for full license information.

namespace XenoAtom.Terminal.Internal.Unix;

internal static unsafe class UnixTermiosModes
{
    public static void ConfigureCbreak(ref LibC.termios_linux termios, bool enableSignals)
    {
        // "cbreak" is intended as a TUI-friendly default:
        // - no canonical (line-buffered) input
        // - no echo
        // - no implementation-defined extensions that can intercept certain keys
        // - no CR->NL mapping so Enter is typically '\r' (closer to Windows)
        // - no XON/XOFF flow control so Ctrl+S/Ctrl+Q are delivered to the app
        termios.c_lflag &= ~(LibC.LINUX_ICANON | LibC.LINUX_ECHO | LibC.LINUX_IEXTEN);
        if (enableSignals) termios.c_lflag |= LibC.LINUX_ISIG;
        else termios.c_lflag &= ~LibC.LINUX_ISIG;

        termios.c_iflag &= ~(LibC.LINUX_ICRNL | LibC.LINUX_IXON);

        SetCc(ref termios, LibC.LINUX_VMIN, 1);
        SetCc(ref termios, LibC.LINUX_VTIME, 0);
    }

    public static void ConfigureCbreak(ref LibC.termios_macos termios, bool enableSignals)
    {
        // See the Linux variant for rationale. We apply the same intent using macOS termios flags.
        termios.c_lflag &= ~(LibC.MACOS_ICANON | LibC.MACOS_ECHO | LibC.MACOS_IEXTEN);
        if (enableSignals) termios.c_lflag |= LibC.MACOS_ISIG;
        else termios.c_lflag &= ~LibC.MACOS_ISIG;

        termios.c_iflag &= ~(LibC.MACOS_ICRNL | LibC.MACOS_IXON);

        SetCc(ref termios, LibC.MACOS_VMIN, 1);
        SetCc(ref termios, LibC.MACOS_VTIME, 0);
    }

    private static void SetCc(ref LibC.termios_linux termios, int index, byte value)
    {
        if ((uint)index >= 32)
        {
            return;
        }

        fixed (byte* cc = termios.c_cc)
        {
            cc[index] = value;
        }
    }

    private static void SetCc(ref LibC.termios_macos termios, int index, byte value)
    {
        if ((uint)index >= 20)
        {
            return;
        }

        fixed (byte* cc = termios.c_cc)
        {
            cc[index] = value;
        }
    }
}
