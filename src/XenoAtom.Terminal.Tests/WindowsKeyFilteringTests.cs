// Copyright (c) Alexandre Mutel. All rights reserved.
// Licensed under the BSD-Clause 2 license.
// See license.txt file in the project root for full license information.

using XenoAtom.Terminal.Internal;

namespace XenoAtom.Terminal.Tests;

[TestClass]
public sealed class WindowsKeyFilteringTests
{
    [TestMethod]
    public void IsStandaloneModifierKey_FiltersShiftCtrlAlt()
    {
        Assert.IsTrue(TerminalWindowsKeyFiltering.IsStandaloneModifierKey(0x10, ch: null)); // VK_SHIFT
        Assert.IsTrue(TerminalWindowsKeyFiltering.IsStandaloneModifierKey(0x11, ch: null)); // VK_CONTROL
        Assert.IsTrue(TerminalWindowsKeyFiltering.IsStandaloneModifierKey(0x12, ch: null)); // VK_MENU (Alt)
    }

    [TestMethod]
    public void IsStandaloneModifierKey_DoesNotFilterWhenCharIsPresent()
    {
        Assert.IsFalse(TerminalWindowsKeyFiltering.IsStandaloneModifierKey(0x10, ch: 'A'));
        Assert.IsFalse(TerminalWindowsKeyFiltering.IsStandaloneModifierKey(0x11, ch: TerminalChar.CtrlC));
        Assert.IsFalse(TerminalWindowsKeyFiltering.IsStandaloneModifierKey(0x12, ch: 'a'));
    }

    [TestMethod]
    public void IsStandaloneModifierKey_DoesNotFilterRegularKeys()
    {
        Assert.IsFalse(TerminalWindowsKeyFiltering.IsStandaloneModifierKey(0x41, ch: null)); // 'A'
        Assert.IsFalse(TerminalWindowsKeyFiltering.IsStandaloneModifierKey(0x0D, ch: null)); // Enter
        Assert.IsFalse(TerminalWindowsKeyFiltering.IsStandaloneModifierKey(0x26, ch: null)); // Up
    }
}

