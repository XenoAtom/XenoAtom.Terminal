// Copyright (c) Alexandre Mutel. All rights reserved.
// Licensed under the BSD-Clause 2 license.
// See license.txt file in the project root for full license information.

namespace XenoAtom.Terminal.Tests;

[TestClass]
public sealed class EmojiWidthTests
{

    [TestMethod]
    public void TerminalTextUtility_GetWidth_Uses_Grapheme_Clusters()
    {
        // ZWJ sequence: runner + ZWJ + female sign + VS16.
        // Terminals typically render this as a single wide glyph (2 cells).
        Assert.AreEqual(2, TerminalTextUtility.GetWidth("🏃‍♀️".AsSpan()));

        // Flags are also grapheme clusters (regional indicator pairs).
        Assert.AreEqual(2, TerminalTextUtility.GetWidth("🇫🇷".AsSpan()));
    }
}
