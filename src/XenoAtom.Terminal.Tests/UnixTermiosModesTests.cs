// Copyright (c) Alexandre Mutel. All rights reserved.
// Licensed under the BSD-Clause 2 license.
// See license.txt file in the project root for full license information.

using XenoAtom.Terminal.Internal.Unix;

namespace XenoAtom.Terminal.Tests;

[TestClass]
public sealed unsafe class UnixTermiosModesTests
{
    [TestMethod]
    public void ConfigureCbreakLinux_DisablesCrlfMappingAndXonXoff()
    {
        var termios = new LibC.termios_linux
        {
            c_iflag = LibC.LINUX_ICRNL | LibC.LINUX_IXON | 0x8000_0000u,
            c_lflag = LibC.LINUX_ISIG | LibC.LINUX_ICANON | LibC.LINUX_ECHO | LibC.LINUX_IEXTEN | 0x4000_0000u,
        };

        UnixTermiosModes.ConfigureCbreak(ref termios, enableSignals: true);

        Assert.AreEqual(0x8000_0000u, termios.c_iflag);

        Assert.AreEqual(0u, termios.c_lflag & LibC.LINUX_ICANON);
        Assert.AreEqual(0u, termios.c_lflag & LibC.LINUX_ECHO);
        Assert.AreEqual(0u, termios.c_lflag & LibC.LINUX_IEXTEN);
        Assert.AreNotEqual(0u, termios.c_lflag & LibC.LINUX_ISIG);
        Assert.AreNotEqual(0u, termios.c_lflag & 0x4000_0000u);

        LibC.termios_linux* p = &termios;
        Assert.AreEqual((byte)1, p->c_cc[LibC.LINUX_VMIN]);
        Assert.AreEqual((byte)0, p->c_cc[LibC.LINUX_VTIME]);
    }

    [TestMethod]
    public void ConfigureCbreakLinux_CanDisableSignals()
    {
        var termios = new LibC.termios_linux
        {
            c_lflag = LibC.LINUX_ISIG | LibC.LINUX_ICANON | LibC.LINUX_ECHO | LibC.LINUX_IEXTEN,
        };

        UnixTermiosModes.ConfigureCbreak(ref termios, enableSignals: false);

        Assert.AreEqual(0u, termios.c_lflag & LibC.LINUX_ISIG);
        Assert.AreEqual(0u, termios.c_lflag & LibC.LINUX_ICANON);
        Assert.AreEqual(0u, termios.c_lflag & LibC.LINUX_ECHO);
        Assert.AreEqual(0u, termios.c_lflag & LibC.LINUX_IEXTEN);
    }

    [TestMethod]
    public void ConfigureCbreakMac_DisablesCrlfMappingAndXonXoff()
    {
        var termios = new LibC.termios_macos
        {
            c_iflag = LibC.MACOS_ICRNL | LibC.MACOS_IXON | (nuint)0x8000_0000u,
            c_lflag = LibC.MACOS_ISIG | LibC.MACOS_ICANON | LibC.MACOS_ECHO | LibC.MACOS_IEXTEN | (nuint)0x4000_0000u,
        };

        UnixTermiosModes.ConfigureCbreak(ref termios, enableSignals: true);

        Assert.AreEqual((nuint)0x8000_0000u, termios.c_iflag);

        Assert.AreEqual((nuint)0, termios.c_lflag & LibC.MACOS_ICANON);
        Assert.AreEqual((nuint)0, termios.c_lflag & LibC.MACOS_ECHO);
        Assert.AreEqual((nuint)0, termios.c_lflag & LibC.MACOS_IEXTEN);
        Assert.AreNotEqual((nuint)0, termios.c_lflag & LibC.MACOS_ISIG);
        Assert.AreNotEqual((nuint)0, termios.c_lflag & (nuint)0x4000_0000u);

        LibC.termios_macos* p = &termios;
        Assert.AreEqual((byte)1, p->c_cc[LibC.MACOS_VMIN]);
        Assert.AreEqual((byte)0, p->c_cc[LibC.MACOS_VTIME]);
    }
}
