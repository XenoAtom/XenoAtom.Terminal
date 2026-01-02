// Copyright (c) Alexandre Mutel. All rights reserved.
// Licensed under the BSD-Clause 2 license.
// See license.txt file in the project root for full license information.

using XenoAtom.Terminal.Internal;

namespace XenoAtom.Terminal.Tests;

[TestClass]
public sealed class KeyModifierNormalizationTests
{
    [TestMethod]
    public void Windows_NormalizeModifiers_StripsShift_ForUnknownTextKeys()
    {
        var mods = TerminalKeyModifierNormalization.NormalizeModifiersForPortableTextKeys(
            TerminalKey.Unknown,
            ch: 'A',
            TerminalModifiers.Shift);

        Assert.AreEqual(TerminalModifiers.None, mods);
    }

    [TestMethod]
    public void Windows_NormalizeModifiers_StripsShift_ButKeepsCtrlAlt_ForUnknownTextKeys()
    {
        var mods = TerminalKeyModifierNormalization.NormalizeModifiersForPortableTextKeys(
            TerminalKey.Unknown,
            ch: 'C',
            TerminalModifiers.Ctrl | TerminalModifiers.Shift | TerminalModifiers.Alt);

        Assert.AreEqual(TerminalModifiers.Ctrl | TerminalModifiers.Alt, mods);
    }

    [TestMethod]
    public void Windows_NormalizeModifiers_KeepsShift_ForTab()
    {
        var mods = TerminalKeyModifierNormalization.NormalizeModifiersForPortableTextKeys(
            TerminalKey.Tab,
            ch: '\t',
            TerminalModifiers.Shift);

        Assert.AreEqual(TerminalModifiers.Shift, mods);
    }

    [TestMethod]
    public void Windows_NormalizeModifiers_StripsShift_ForSpace()
    {
        var mods = TerminalKeyModifierNormalization.NormalizeModifiersForPortableTextKeys(
            TerminalKey.Space,
            ch: ' ',
            TerminalModifiers.Shift);

        Assert.AreEqual(TerminalModifiers.None, mods);
    }

    [TestMethod]
    public void Windows_NormalizeModifiers_KeepsShift_ForSpecialKeys()
    {
        var mods = TerminalKeyModifierNormalization.NormalizeModifiersForPortableTextKeys(
            TerminalKey.Up,
            ch: null,
            TerminalModifiers.Shift);

        Assert.AreEqual(TerminalModifiers.Shift, mods);
    }
}
