// Copyright (c) Alexandre Mutel. All rights reserved.
// Licensed under the BSD-Clause 2 license.
// See license.txt file in the project root for full license information.

namespace XenoAtom.Terminal.Tests;

[TestClass]
public sealed class TerminalTextUtilityTests
{
    [TestMethod]
    public void GetWidth_BasicAscii_Works()
    {
        Assert.AreEqual(0, TerminalTextUtility.GetWidth(ReadOnlySpan<char>.Empty));
        Assert.AreEqual(5, TerminalTextUtility.GetWidth("hello".AsSpan()));
    }

    [TestMethod]
    public void GetWidth_TabAndNewlines_Works()
    {
        Assert.AreEqual(4, TerminalTextUtility.GetWidth("\t".AsSpan()));
        Assert.AreEqual(2 + 4 + 2, TerminalTextUtility.GetWidth("hi\tok".AsSpan()));
        Assert.AreEqual(0, TerminalTextUtility.GetWidth("\r".AsSpan()));
        Assert.AreEqual(0, TerminalTextUtility.GetWidth("\n".AsSpan()));
        Assert.AreEqual(0, TerminalTextUtility.GetWidth("\r\n".AsSpan()));
    }

    [TestMethod]
    public void GetWidth_WideCharacters_AreAccountedFor()
    {
        // Typical terminals treat CJK and emoji as double-width.
        Assert.AreEqual(2, TerminalTextUtility.GetWidth("中".AsSpan()));
        Assert.AreEqual(2, TerminalTextUtility.GetWidth("😃".AsSpan()));
        Assert.AreEqual(1 + 2 + 1, TerminalTextUtility.GetWidth("A中B".AsSpan()));
    }

    [TestMethod]
    public void TryGetIndexAtCell_MapsToUtf16Indices()
    {
        var text = "A中B".AsSpan(); // widths: 1,2,1 => total 4

        Assert.IsTrue(TerminalTextUtility.TryGetIndexAtCell(text, 0, out var index0));
        Assert.AreEqual(0, index0);

        Assert.IsTrue(TerminalTextUtility.TryGetIndexAtCell(text, 1, out var index1));
        Assert.AreEqual(1, index1);

        Assert.IsTrue(TerminalTextUtility.TryGetIndexAtCell(text, 2, out var index2));
        Assert.AreEqual(1, index2);

        Assert.IsTrue(TerminalTextUtility.TryGetIndexAtCell(text, 3, out var index3));
        Assert.AreEqual(2, index3);

        Assert.IsTrue(TerminalTextUtility.TryGetIndexAtCell(text, 4, out var index4));
        Assert.AreEqual(3, index4);
    }

    [TestMethod]
    public void RuneNavigation_HandlesSurrogatePairs()
    {
        var text = "A😃B".AsSpan();

        Assert.AreEqual(1, TerminalTextUtility.GetNextRuneIndex(text, 0));
        Assert.AreEqual(3, TerminalTextUtility.GetNextRuneIndex(text, 1));
        Assert.AreEqual(4, TerminalTextUtility.GetNextRuneIndex(text, 3));

        Assert.AreEqual(3, TerminalTextUtility.GetPreviousRuneIndex(text, 4));
        Assert.AreEqual(1, TerminalTextUtility.GetPreviousRuneIndex(text, 3));
        Assert.AreEqual(0, TerminalTextUtility.GetPreviousRuneIndex(text, 1));
    }

    [TestMethod]
    public void WordBoundaries_WorkForIdentifierLikeWords()
    {
        var ident = "hello_world".AsSpan();
        Assert.IsTrue(TerminalTextUtility.IsWordChar('_'));
        Assert.IsTrue(TerminalTextUtility.IsWordStart(ident, 0));
        Assert.IsTrue(TerminalTextUtility.IsWordEnd(ident, ident.Length));
        Assert.AreEqual(0, TerminalTextUtility.GetWordStart(ident, 8));
        Assert.AreEqual(ident.Length, TerminalTextUtility.GetWordEnd(ident, 8));

        var text = "hello world".AsSpan();
        Assert.AreEqual(6, TerminalTextUtility.GetWordStart(text, 6));
        Assert.AreEqual(11, TerminalTextUtility.GetWordEnd(text, 6));
        Assert.AreEqual(0, TerminalTextUtility.GetWordStart(text, 5)); // space uses previous word (best effort)
        Assert.AreEqual(5, TerminalTextUtility.GetWordEnd(text, 5)); // space stays on boundary
    }
}
