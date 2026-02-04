// Copyright (c) Alexandre Mutel. All rights reserved.
// Licensed under the BSD-Clause 2 license.
// See license.txt file in the project root for full license information.

using System;
using System.Buffers.Binary;
using System.Collections.Generic;
using System.Diagnostics.CodeAnalysis;
using System.Globalization;
using System.IO;

namespace XenoAtom.Terminal.Backends;

internal sealed class UnixTerminfo
{
    private const ushort TerminfoMagic = 0x011A;
    private const ushort TerminfoMagicSwapped = 0x1A01;
    private const ushort TerminfoMagicExtended = 0x021E;
    private const ushort TerminfoMagicExtendedSwapped = 0x1E02;
    private const int ColorsNumberIndex = 13;

    private static readonly string[] DefaultDirectories =
    [
        "/etc/terminfo",
        "/lib/terminfo",
        "/usr/share/terminfo",
        "/usr/lib/terminfo",
        "/usr/local/share/terminfo",
        "/usr/local/lib/terminfo",
        "/usr/share/lib/terminfo",
    ];

    // Indices follow the standard terminfo string capability ordering (ncurses Caps).
    internal enum StandardStringCapability
    {
        Bell = 1,
        CursorAddress = 10,
        CursorInvisible = 13,
        CursorNormal = 16,
        CursorVisible = 20,
        EnterCaMode = 28,
        ExitCaMode = 40,
        SetForeground = 302,
        SetBackground = 303,
        SetAForeground = 359,
        SetABackground = 360,
    }

    private readonly int[] _numbers;
    private readonly int[] _stringOffsets;
    private readonly bool _hasExtendedTrueColor;

    private UnixTerminfo(string term, int[] numbers, int[] stringOffsets, bool hasExtendedTrueColor)
    {
        Term = term;
        _numbers = numbers;
        _stringOffsets = stringOffsets;
        _hasExtendedTrueColor = hasExtendedTrueColor;
    }

    public string Term { get; }

    public bool SupportsAlternateScreen
        => HasString(StandardStringCapability.EnterCaMode) && HasString(StandardStringCapability.ExitCaMode);

    public bool SupportsCursorVisibility
        => HasString(StandardStringCapability.CursorInvisible) &&
           (HasString(StandardStringCapability.CursorNormal) || HasString(StandardStringCapability.CursorVisible));

    public bool SupportsCursorPositioning
        => HasString(StandardStringCapability.CursorAddress);

    public bool SupportsAnsiColors
        => HasString(StandardStringCapability.SetAForeground) ||
           HasString(StandardStringCapability.SetABackground) ||
           HasString(StandardStringCapability.SetForeground) ||
           HasString(StandardStringCapability.SetBackground);

    public bool SupportsBell => HasString(StandardStringCapability.Bell);

    public TerminalColorLevel GetColorLevel(TerminalColorLevel fallback)
    {
        if (_hasExtendedTrueColor)
        {
            return TerminalColorLevel.TrueColor;
        }

        if (TryGetNumber(ColorsNumberIndex, out var colors) && colors > 0)
        {
            return colors >= 256 ? TerminalColorLevel.Color256 : TerminalColorLevel.Color16;
        }

        return fallback;
    }

    internal static bool TryLoad(string term, [NotNullWhen(true)] out UnixTerminfo? terminfo)
    {
        terminfo = null;
        if (!IsSafeTermName(term))
        {
            return false;
        }

        foreach (var path in EnumerateTerminfoFileCandidates(term))
        {
            if (!TryReadFile(path, out var data))
            {
                continue;
            }

            if (TryParse(data, term, out terminfo))
            {
                return true;
            }
        }

        return false;
    }

    private static bool TryParse(ReadOnlySpan<byte> data, string term, [NotNullWhen(true)] out UnixTerminfo? terminfo)
    {
        terminfo = null;
        if (data.Length < 12)
        {
            return false;
        }

        var magic = BinaryPrimitives.ReadUInt16LittleEndian(data);
        var uses32BitNumbers = false;
        var swapped = false;
        if (magic == TerminfoMagicSwapped || magic == TerminfoMagicExtendedSwapped)
        {
            swapped = true;
            uses32BitNumbers = magic == TerminfoMagicExtendedSwapped;
        }
        else if (magic != TerminfoMagic && magic != TerminfoMagicExtended)
        {
            return false;
        }
        else
        {
            uses32BitNumbers = magic == TerminfoMagicExtended;
        }

        var offset = 2;
        if (!TryReadInt16(data, ref offset, swapped, out var namesSize) ||
            !TryReadInt16(data, ref offset, swapped, out var boolCount) ||
            !TryReadInt16(data, ref offset, swapped, out var numCount) ||
            !TryReadInt16(data, ref offset, swapped, out var strCount) ||
            !TryReadInt16(data, ref offset, swapped, out var strTableSize))
        {
            return false;
        }

        if (namesSize < 0 || boolCount < 0 || numCount < 0 || strCount < 0 || strTableSize < 0)
        {
            return false;
        }

        // Validate sizes up-front to avoid large allocations for malformed/corrupt data.
        if (!TryGetStandardSectionSize(namesSize, boolCount, numCount, strCount, strTableSize, uses32BitNumbers, out var standardSectionSize))
        {
            return false;
        }

        if ((uint)(offset + standardSectionSize) > (uint)data.Length)
        {
            return false;
        }

        if (!TrySkip(data, ref offset, namesSize))
        {
            return false;
        }

        if (!TrySkip(data, ref offset, boolCount))
        {
            return false;
        }

        if (((namesSize + boolCount) & 1) != 0 && !TrySkip(data, ref offset, 1))
        {
            return false;
        }

        var numbers = numCount == 0 ? Array.Empty<int>() : new int[numCount];
        for (var index = 0; index < numbers.Length; index++)
        {
            if (uses32BitNumbers)
            {
                if (!TryReadInt32(data, ref offset, swapped, out var value))
                {
                    return false;
                }

                numbers[index] = value;
            }
            else
            {
                if (!TryReadInt16(data, ref offset, swapped, out var value))
                {
                    return false;
                }

                numbers[index] = value;
            }
        }

        var stringOffsets = strCount == 0 ? Array.Empty<int>() : new int[strCount];
        for (var index = 0; index < stringOffsets.Length; index++)
        {
            if (!TryReadInt16(data, ref offset, swapped, out var value))
            {
                return false;
            }

            stringOffsets[index] = value;
        }

        if (!TrySkip(data, ref offset, strTableSize))
        {
            return false;
        }

        var hasExtendedTrueColor = false;
        _ = TryParseExtended(data, offset, swapped, uses32BitNumbers, out hasExtendedTrueColor);

        terminfo = new UnixTerminfo(term, numbers, stringOffsets, hasExtendedTrueColor);
        return true;
    }

    private bool HasString(StandardStringCapability capability)
    {
        var index = (int)capability;
        if ((uint)index >= (uint)_stringOffsets.Length)
        {
            return false;
        }

        return _stringOffsets[index] >= 0;
    }

    private bool TryGetNumber(int index, out int value)
    {
        if ((uint)index >= (uint)_numbers.Length)
        {
            value = -1;
            return false;
        }

        value = _numbers[index];
        return value >= 0;
    }

    private static bool TryParseExtended(ReadOnlySpan<byte> data, int offset, bool swapped, bool uses32BitNumbers, out bool hasTrueColor)
    {
        hasTrueColor = false;
        if (offset + 8 > data.Length)
        {
            return false;
        }

        var startOffset = offset;
        if (!TryReadInt16(data, ref offset, swapped, out var extBoolCount) ||
            !TryReadInt16(data, ref offset, swapped, out var extNumCount) ||
            !TryReadInt16(data, ref offset, swapped, out var extStrCount) ||
            !TryReadInt16(data, ref offset, swapped, out var extOffsetsOrTableSize))
        {
            return false;
        }

        if (extBoolCount < 0 || extNumCount < 0 || extStrCount < 0 || extOffsetsOrTableSize < 0)
        {
            return false;
        }

        // ncurses "extended" format adds an offsets count and then the extended string-table size:
        //   extBoolCount, extNumCount, extStrCount, extOffsetCount, extStrTableSize
        // Older variants only provide:
        //   extBoolCount, extNumCount, extStrCount, extStrTableSize
        var nameCount = extBoolCount + extNumCount + extStrCount;
        if (nameCount < 0)
        {
            return false;
        }

        var expectedOffsetCount = extStrCount + nameCount;
        var extOffsetsCount = expectedOffsetCount;
        var extStrTableSize = extOffsetsOrTableSize;

        // Prefer the 5-short header when it is consistent.
        if (offset + 2 <= data.Length &&
            extOffsetsOrTableSize == expectedOffsetCount &&
            TryReadInt16(data, ref offset, swapped, out var extStrTableSizeCandidate) &&
            extStrTableSizeCandidate >= 0)
        {
            extOffsetsCount = extOffsetsOrTableSize;
            extStrTableSize = extStrTableSizeCandidate;
        }

        var numberSize = uses32BitNumbers ? 4 : 2;
        var boolPad = (extBoolCount & 1) != 0 ? 1 : 0;
        if (!TryMultiplyToInt32(extNumCount, numberSize, out var numbersBytes) ||
            !TryMultiplyToInt32(extOffsetsCount, 2, out var offsetsBytes) ||
            !TryAddToInt32(extBoolCount, boolPad, out var boolBytes) ||
            !TryAddToInt32(boolBytes, numbersBytes, out var boolAndNumbersBytes) ||
            !TryAddToInt32(boolAndNumbersBytes, offsetsBytes, out var beforeStringTableBytes) ||
            !TryAddToInt32(beforeStringTableBytes, extStrTableSize, out var remainingBytes))
        {
            return false;
        }

        if ((uint)(offset + remainingBytes) > (uint)data.Length)
        {
            return false;
        }

        var bools = extBoolCount == 0 ? Array.Empty<bool>() : new bool[extBoolCount];
        for (var i = 0; i < bools.Length; i++)
        {
            if (!TryReadByte(data, ref offset, out var value))
            {
                return false;
            }

            bools[i] = value != 0;
        }

        if (((extBoolCount) & 1) != 0 && !TrySkip(data, ref offset, 1))
        {
            return false;
        }

        if (!TrySkipRepeated(data, ref offset, extNumCount, numberSize))
        {
            return false;
        }

        // In the ncurses extended format, both the extended string offsets and the extended capability name offsets
        // are stored before the extended string table. The extOffsetsCount provides an additional consistency check.
        if (extOffsetsCount != expectedOffsetCount)
        {
            return false;
        }

        var strOffsets = extStrCount == 0 ? Array.Empty<short>() : new short[extStrCount];
        for (var i = 0; i < strOffsets.Length; i++)
        {
            if (!TryReadInt16(data, ref offset, swapped, out var value))
            {
                return false;
            }

            strOffsets[i] = value;
        }

        var nameOffsets = nameCount == 0 ? Array.Empty<short>() : new short[nameCount];
        for (var i = 0; i < nameOffsets.Length; i++)
        {
            if (!TryReadInt16(data, ref offset, swapped, out var value))
            {
                return false;
            }

            nameOffsets[i] = value;
        }

        if (nameCount == 0 && extStrTableSize == 0)
        {
            return offset > startOffset;
        }

        if ((uint)(offset + extStrTableSize) > (uint)data.Length)
        {
            return false;
        }

        var stringTable = data.Slice(offset, extStrTableSize);
        offset += extStrTableSize;

        for (var i = 0; i < nameCount; i++)
        {
            var nameOffset = nameOffsets[i];

            if (nameOffset < 0)
            {
                continue;
            }

            if (i < extBoolCount)
            {
                if (MatchesName(stringTable, nameOffset, "Tc"u8) || MatchesName(stringTable, nameOffset, "RGB"u8))
                {
                    if (bools[i])
                    {
                        hasTrueColor = true;
                        return true;
                    }
                }

                continue;
            }

            if (i < extBoolCount + extNumCount)
            {
                continue;
            }

            var strIndex = i - extBoolCount - extNumCount;
            if (strIndex < 0 || strIndex >= strOffsets.Length)
            {
                continue;
            }

            if (!MatchesName(stringTable, nameOffset, "setrgbf"u8) && !MatchesName(stringTable, nameOffset, "setrgbb"u8))
            {
                continue;
            }

            if (HasStringValue(stringTable, strOffsets[strIndex]))
            {
                hasTrueColor = true;
                return true;
            }
        }

        return true;
    }

    private static bool HasStringValue(ReadOnlySpan<byte> table, short offset)
    {
        if (offset < 0)
        {
            return false;
        }

        if ((uint)offset >= (uint)table.Length)
        {
            return false;
        }

        // The terminfo format stores NUL-terminated strings. For our purposes we treat an empty string
        // as absent, and require a terminator to consider the entry well-formed.
        if (table[offset] == 0)
        {
            return false;
        }

        return table.Slice(offset).IndexOf((byte)0) >= 0;
    }

    private static bool MatchesName(ReadOnlySpan<byte> table, int offset, ReadOnlySpan<byte> name)
    {
        if ((uint)offset >= (uint)table.Length)
        {
            return false;
        }

        if (offset + name.Length >= table.Length)
        {
            return false;
        }

        if (!table.Slice(offset, name.Length).SequenceEqual(name))
        {
            return false;
        }

        return table[offset + name.Length] == 0;
    }

    private static bool TryReadInt16(ReadOnlySpan<byte> data, ref int offset, bool swapped, out short value)
    {
        if ((uint)(offset + 2) > (uint)data.Length)
        {
            value = 0;
            return false;
        }

        value = swapped
            ? BinaryPrimitives.ReadInt16BigEndian(data.Slice(offset, 2))
            : BinaryPrimitives.ReadInt16LittleEndian(data.Slice(offset, 2));
        offset += 2;
        return true;
    }

    private static bool TryReadInt32(ReadOnlySpan<byte> data, ref int offset, bool swapped, out int value)
    {
        if ((uint)(offset + 4) > (uint)data.Length)
        {
            value = 0;
            return false;
        }

        value = swapped
            ? BinaryPrimitives.ReadInt32BigEndian(data.Slice(offset, 4))
            : BinaryPrimitives.ReadInt32LittleEndian(data.Slice(offset, 4));
        offset += 4;
        return true;
    }

    private static bool TryReadByte(ReadOnlySpan<byte> data, ref int offset, out byte value)
    {
        if ((uint)offset >= (uint)data.Length)
        {
            value = 0;
            return false;
        }

        value = data[offset];
        offset++;
        return true;
    }

    private static bool TrySkip(ReadOnlySpan<byte> data, ref int offset, int count)
    {
        if (count < 0)
        {
            return false;
        }

        if ((uint)(offset + count) > (uint)data.Length)
        {
            return false;
        }

        offset += count;
        return true;
    }

    private static bool TrySkipRepeated(ReadOnlySpan<byte> data, ref int offset, int count, int elementSize)
    {
        if (count < 0 || elementSize <= 0)
        {
            return false;
        }

        if (!TryMultiplyToInt32(count, elementSize, out var bytesToSkip))
        {
            return false;
        }

        return TrySkip(data, ref offset, bytesToSkip);
    }

    private static bool TryGetStandardSectionSize(
        int namesSize,
        int boolCount,
        int numCount,
        int strCount,
        int strTableSize,
        bool uses32BitNumbers,
        out int size)
    {
        size = 0;
        var pad = ((namesSize + boolCount) & 1) != 0 ? 1 : 0;
        var numberSize = uses32BitNumbers ? 4 : 2;
        if (!TryMultiplyToInt32(numCount, numberSize, out var numbersBytes) ||
            !TryMultiplyToInt32(strCount, 2, out var stringOffsetsBytes))
        {
            return false;
        }

        return TryAddToInt32(namesSize, boolCount, out var namesAndBools) &&
               TryAddToInt32(namesAndBools, pad, out var withPad) &&
               TryAddToInt32(withPad, numbersBytes, out var withNumbers) &&
               TryAddToInt32(withNumbers, stringOffsetsBytes, out var withOffsets) &&
               TryAddToInt32(withOffsets, strTableSize, out size);
    }

    private static bool TryMultiplyToInt32(int a, int b, out int result)
    {
        var value = (long)a * b;
        if (value < 0 || value > int.MaxValue)
        {
            result = 0;
            return false;
        }

        result = (int)value;
        return true;
    }

    private static bool TryAddToInt32(int a, int b, out int result)
    {
        var value = (long)a + b;
        if (value < 0 || value > int.MaxValue)
        {
            result = 0;
            return false;
        }

        result = (int)value;
        return true;
    }

    private static bool TryReadFile(string path, out byte[] data)
    {
        data = Array.Empty<byte>();

        try
        {
            if (!File.Exists(path))
            {
                return false;
            }

            // Terminfo entries are expected to be small. Guard against accidentally reading very large files
            // via TERMINFO/TERMINFO_DIRS misconfiguration.
            const long maxSizeBytes = 1024 * 1024;
            var fileInfo = new FileInfo(path);
            if (fileInfo.Length <= 0 || fileInfo.Length > maxSizeBytes)
            {
                return false;
            }

            data = File.ReadAllBytes(path);
            return data.Length > 0;
        }
        catch
        {
            return false;
        }
    }

    private static bool IsSafeTermName(string term)
    {
        if (string.IsNullOrWhiteSpace(term))
        {
            return false;
        }

        if (term.IndexOf('\0') >= 0)
        {
            return false;
        }

        if (term.Contains(Path.DirectorySeparatorChar) || term.Contains(Path.AltDirectorySeparatorChar))
        {
            return false;
        }

        return true;
    }

    private static IEnumerable<string> EnumerateTerminfoFileCandidates(string term)
    {
        var seen = new HashSet<string>(StringComparer.Ordinal);
        foreach (var directory in EnumerateTerminfoDirectories())
        {
            if (string.IsNullOrWhiteSpace(directory))
            {
                continue;
            }

            if (File.Exists(directory))
            {
                if (seen.Add(directory))
                {
                    yield return directory;
                }

                continue;
            }

            var letterDirectory = Path.Combine(directory, term[0].ToString());
            var letterPath = Path.Combine(letterDirectory, term);
            if (seen.Add(letterPath))
            {
                yield return letterPath;
            }

            var hexDirectoryName = ((int)term[0]).ToString("x2", CultureInfo.InvariantCulture);
            var hexDirectory = Path.Combine(directory, hexDirectoryName);
            var hexPath = Path.Combine(hexDirectory, term);
            if (seen.Add(hexPath))
            {
                yield return hexPath;
            }
        }
    }

    private static IEnumerable<string> EnumerateTerminfoDirectories()
    {
        var terminfo = Environment.GetEnvironmentVariable("TERMINFO");
        if (!string.IsNullOrEmpty(terminfo))
        {
            yield return terminfo;
        }

        var home = Environment.GetEnvironmentVariable("HOME");
        if (!string.IsNullOrEmpty(home))
        {
            yield return Path.Combine(home, ".terminfo");
        }

        var terminfoDirs = Environment.GetEnvironmentVariable("TERMINFO_DIRS");
        if (!string.IsNullOrEmpty(terminfoDirs))
        {
            foreach (var entry in terminfoDirs.Split(':'))
            {
                if (entry.Length == 0)
                {
                    foreach (var directory in DefaultDirectories)
                    {
                        yield return directory;
                    }
                }
                else
                {
                    yield return entry;
                }
            }

            yield break;
        }

        foreach (var directory in DefaultDirectories)
        {
            yield return directory;
        }
    }
}
