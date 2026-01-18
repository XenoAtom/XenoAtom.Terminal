// Copyright (c) Alexandre Mutel. All rights reserved.
// Licensed under the BSD-Clause 2 license.
// See license.txt file in the project root for full license information.

namespace XenoAtom.Terminal.Tests;

[TestClass]
public sealed class EmojiWidthTests
{
    [TestMethod]
    public void TerminalTextUtility_GetWidth_UsesGraphemeClusters()
    {
        // Base emoji + VS16 (emoji presentation selector).
        Assert.AreEqual(2, TerminalTextUtility.GetWidth("\U0001F5C3\uFE0F".AsSpan())); // 🗃️

        // ZWJ sequence: runner + ZWJ + female sign + VS16.
        // Terminals typically render this as a single wide glyph (2 cells).
        Assert.AreEqual(2, TerminalTextUtility.GetWidth("\U0001F3C3\u200D\u2640\uFE0F".AsSpan())); // 🏃‍♀️

        // Flags are also grapheme clusters (regional indicator pairs).
        Assert.AreEqual(2, TerminalTextUtility.GetWidth("\U0001F1FA\U0001F1F8".AsSpan())); // 🇺🇸

        // Keycap sequences are grapheme clusters (digit + VS16 + combining keycap).
        Assert.AreEqual(2, TerminalTextUtility.GetWidth("1\uFE0F\u20E3".AsSpan())); // 1️⃣

        // Combining marks are part of the same grapheme cluster and should not add width.
        Assert.AreEqual(1, TerminalTextUtility.GetWidth("e\u0301".AsSpan())); // é
    }

    [TestMethod]
    public void TerminalTextUtility_TryGetIndexAtCell_IsGraphemeAware()
    {
        var text = "a\U0001F5C3\uFE0Fb".AsSpan(); // a🗃️b
        Assert.AreEqual(4, TerminalTextUtility.GetWidth(text));

        Assert.IsTrue(TerminalTextUtility.TryGetIndexAtCell(text, cellOffset: 0, out var i0));
        Assert.AreEqual(0, i0);

        Assert.IsTrue(TerminalTextUtility.TryGetIndexAtCell(text, cellOffset: 1, out var i1));
        Assert.AreEqual(1, i1);

        Assert.IsTrue(TerminalTextUtility.TryGetIndexAtCell(text, cellOffset: 2, out var i2));
        Assert.AreEqual(1, i2);

        Assert.IsTrue(TerminalTextUtility.TryGetIndexAtCell(text, cellOffset: 3, out var i3));
        Assert.AreEqual(4, i3);

        Assert.IsTrue(TerminalTextUtility.TryGetIndexAtCell(text, cellOffset: 4, out var i4));
        Assert.AreEqual(5, i4);
    }
}
