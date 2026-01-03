// Copyright (c) Alexandre Mutel. All rights reserved.
// Licensed under the BSD-Clause 2 license.
// See license.txt file in the project root for full license information.

using System.Buffers;
using System.Text;
using Wcwidth;

namespace XenoAtom.Terminal;

/// <summary>
/// Utility methods for working with terminal text (cell width, rune navigation, word boundaries).
/// </summary>
public static class TerminalTextUtility
{
    /// <summary>
    /// The default tab width in terminal cells.
    /// </summary>
    public const int DefaultTabWidth = 4;

    /// <summary>
    /// Gets the terminal cell width of a UTF-16 span.
    /// </summary>
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

    /// <summary>
    /// Gets the terminal cell width of a slice of <paramref name="text"/> (UTF-16).
    /// </summary>
    public static int GetWidth(ReadOnlySpan<char> text, int start, int length, int tabWidth = DefaultTabWidth)
        => GetWidth(text.Slice(start, length), tabWidth);

    /// <summary>
    /// Gets the terminal cell width of a Unicode scalar value.
    /// </summary>
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

    /// <summary>
    /// Gets the index of the previous rune boundary (UTF-16).
    /// </summary>
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

    /// <summary>
    /// Gets the index of the next rune boundary (UTF-16).
    /// </summary>
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

    /// <summary>
    /// Attempts to map a terminal cell offset to a UTF-16 index in <paramref name="text"/>.
    /// </summary>
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

    /// <summary>
    /// Gets whether a character is considered part of a "word" (identifier-like) for word-boundary helpers.
    /// </summary>
    public static bool IsWordChar(char c) => char.IsLetterOrDigit(c) || c == '_';

    /// <summary>
    /// Gets whether <paramref name="index"/> is at the start of a word.
    /// </summary>
    public static bool IsWordStart(ReadOnlySpan<char> text, int index)
    {
        if ((uint)index >= (uint)text.Length)
        {
            return false;
        }

        if (!IsWordChar(text[index]))
        {
            return false;
        }

        if (index == 0)
        {
            return true;
        }

        return !IsWordChar(text[index - 1]);
    }

    /// <summary>
    /// Gets whether <paramref name="indexExclusive"/> is at the end of a word (end-exclusive boundary).
    /// </summary>
    public static bool IsWordEnd(ReadOnlySpan<char> text, int indexExclusive)
    {
        if ((uint)indexExclusive > (uint)text.Length)
        {
            return false;
        }

        if (indexExclusive == text.Length)
        {
            return true;
        }

        return !IsWordChar(text[indexExclusive]);
    }

    /// <summary>
    /// Gets the start index of the word around <paramref name="index"/> (best effort).
    /// </summary>
    public static int GetWordStart(ReadOnlySpan<char> text, int index)
    {
        index = Math.Clamp(index, 0, text.Length);
        if (index == text.Length || !IsWordChar(text[index]))
        {
            if (index > 0 && IsWordChar(text[index - 1]))
            {
                index--;
            }
            else
            {
                return index;
            }
        }

        var i = index;
        while (i > 0 && IsWordChar(text[i - 1]))
        {
            i--;
        }

        return i;
    }

    /// <summary>
    /// Gets the end index (exclusive) of the word around <paramref name="index"/> (best effort).
    /// </summary>
    public static int GetWordEnd(ReadOnlySpan<char> text, int index)
    {
        index = Math.Clamp(index, 0, text.Length);
        if (index < text.Length && !IsWordChar(text[index]))
        {
            return index;
        }

        var i = index;
        while (i < text.Length && IsWordChar(text[i]))
        {
            i++;
        }

        return i;
    }
}
