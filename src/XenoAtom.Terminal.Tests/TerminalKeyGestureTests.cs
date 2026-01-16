// Copyright (c) Alexandre Mutel. All rights reserved.
// Licensed under the BSD-Clause 2 license.
// See license.txt file in the project root for full license information.

namespace XenoAtom.Terminal.Tests;

[TestClass]
public sealed class TerminalKeyGestureTests
{
    [TestMethod]
    public void ToString_FormatsCommonGestures()
    {
        var backspace = new TerminalKeyGesture(TerminalKey.Backspace, null, TerminalModifiers.None);
        Assert.AreEqual("backspace", backspace.ToString());

        var altB = new TerminalKeyGesture(TerminalKey.Unknown, 'b', TerminalModifiers.Alt);
        Assert.AreEqual("ALT+b", altB.ToString());

        var ctrlR = new TerminalKeyGesture(TerminalKey.Unknown, TerminalChar.CtrlR, TerminalModifiers.Ctrl);
        Assert.AreEqual("CTRL+R", ctrlR.ToString());
    }

    [TestMethod]
    public void Parse_ParsesCommonGestures()
    {
        var backspace = TerminalKeyGesture.Parse("backspace");
        Assert.AreEqual(TerminalKey.Backspace, backspace.Key);
        Assert.IsNull(backspace.Char);
        Assert.AreEqual(TerminalModifiers.None, backspace.Modifiers);

        var altB = TerminalKeyGesture.Parse("ALT+b");
        Assert.AreEqual(TerminalKey.Unknown, altB.Key);
        Assert.AreEqual('b', altB.Char);
        Assert.AreEqual(TerminalModifiers.Alt, altB.Modifiers);

        var ctrlR = TerminalKeyGesture.Parse("CTRL+R");
        Assert.AreEqual(TerminalKey.Unknown, ctrlR.Key);
        Assert.AreEqual(TerminalChar.CtrlR, ctrlR.Char);
        Assert.AreEqual(TerminalModifiers.Ctrl, ctrlR.Modifiers);
    }

    [TestMethod]
    public void TryParse_RejectsInvalidInput()
    {
        Assert.IsFalse(TerminalKeyGesture.TryParse("", out _));
        Assert.IsFalse(TerminalKeyGesture.TryParse("CTRL+", out _));
        Assert.IsFalse(TerminalKeyGesture.TryParse("CTRL+ALT+UNKNOWNKEY", out _));
    }
}
