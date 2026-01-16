// Copyright (c) Alexandre Mutel. All rights reserved.
// Licensed under the BSD-Clause 2 license.
// See license.txt file in the project root for full license information.

namespace XenoAtom.Terminal.Tests;

[TestClass]
public sealed class TerminalCharTests
{
    [TestMethod]
    public void Ctrl_ReturnsExpectedValues_ForUppercaseLetters()
    {
        Assert.AreEqual(TerminalChar.CtrlA, TerminalChar.Ctrl('A'));
        Assert.AreEqual(TerminalChar.CtrlC, TerminalChar.Ctrl('C'));
        Assert.AreEqual(TerminalChar.CtrlZ, TerminalChar.Ctrl('Z'));
    }

    [TestMethod]
    public void Ctrl_AcceptsLowercaseLetters()
    {
        Assert.AreEqual(TerminalChar.CtrlA, TerminalChar.Ctrl('a'));
        Assert.AreEqual(TerminalChar.CtrlM, TerminalChar.Ctrl('m'));
        Assert.AreEqual(TerminalChar.CtrlZ, TerminalChar.Ctrl('z'));
    }

    [TestMethod]
    public void Ctrl_ThrowsForInvalidLetters()
    {
        Assert.Throws<ArgumentOutOfRangeException>(() => TerminalChar.Ctrl('1'));
        Assert.Throws<ArgumentOutOfRangeException>(() => TerminalChar.Ctrl('@'));
        Assert.Throws<ArgumentOutOfRangeException>(() => TerminalChar.Ctrl('['));
    }
}
