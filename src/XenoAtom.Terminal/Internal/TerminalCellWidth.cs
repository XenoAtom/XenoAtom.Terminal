// Copyright (c) Alexandre Mutel. All rights reserved.
// Licensed under the BSD-Clause 2 license.
// See license.txt file in the project root for full license information.

using System.Buffers;
using System.Text;
using Wcwidth;

namespace XenoAtom.Terminal.Internal;

internal static class TerminalCellWidth
{
    public const int DefaultTabWidth = 4;

    public static int GetWidth(ReadOnlySpan<char> text, int tabWidth = DefaultTabWidth)
    {
        if (text.IsEmpty)
        {
            return 0;
        }

        var width = 0;
        var index = 0;
        while (index < text.Length)
        {
            if (Rune.DecodeFromUtf16(text[index..], out var rune, out var consumed) != OperationStatus.Done || consumed <= 0)
            {
                // Replace invalid sequences with U+FFFD.
                rune = Rune.ReplacementChar;
                consumed = 1;
            }

            width += GetRuneWidth(rune, tabWidth);
            index += consumed;
        }

        return width;
    }

    public static int GetWidth(ReadOnlySpan<char> text, int start, int length, int tabWidth = DefaultTabWidth)
        => GetWidth(text.Slice(start, length), tabWidth);

    public static int GetRuneWidth(Rune rune, int tabWidth = DefaultTabWidth)
    {
        if (rune.Value == '\t')
        {
            return tabWidth;
        }

        if (rune.Value is '\r' or '\n')
        {
            return 0;
        }

        var w = UnicodeCalculator.GetWidth(rune.Value);
        return w <= 0 ? 0 : w;
    }

    public static int GetPreviousRuneIndex(ReadOnlySpan<char> text, int index)
    {
        if (index <= 0)
        {
            return 0;
        }

        index--;
        if (index > 0 && char.IsLowSurrogate(text[index]) && char.IsHighSurrogate(text[index - 1]))
        {
            index--;
        }
        return index;
    }

    public static int GetNextRuneIndex(ReadOnlySpan<char> text, int index)
    {
        if ((uint)index >= (uint)text.Length)
        {
            return text.Length;
        }

        var ch = text[index];
        if (char.IsHighSurrogate(ch) && index + 1 < text.Length && char.IsLowSurrogate(text[index + 1]))
        {
            return index + 2;
        }

        return index + 1;
    }

    public static bool TryGetIndexAtCell(ReadOnlySpan<char> text, int cellOffset, out int index, int tabWidth = DefaultTabWidth)
    {
        if (cellOffset <= 0)
        {
            index = 0;
            return true;
        }

        var width = 0;
        var i = 0;
        while (i < text.Length)
        {
            if (Rune.DecodeFromUtf16(text[i..], out var rune, out var consumed) != OperationStatus.Done || consumed <= 0)
            {
                rune = Rune.ReplacementChar;
                consumed = 1;
            }

            var w = GetRuneWidth(rune, tabWidth);
            if (width + w > cellOffset)
            {
                index = i;
                return true;
            }

            width += w;
            i += consumed;
        }

        index = text.Length;
        return true;
    }
}
