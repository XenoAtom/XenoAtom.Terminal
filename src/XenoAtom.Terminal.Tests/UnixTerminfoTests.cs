// Copyright (c) Alexandre Mutel. All rights reserved.
// Licensed under the BSD-Clause 2 license.
// See license.txt file in the project root for full license information.

using System;
using System.Collections.Generic;
using System.Globalization;
using System.IO;
using XenoAtom.Terminal.Backends;

namespace XenoAtom.Terminal.Tests;

[TestClass]
public sealed class UnixTerminfoTests
{
    [TestMethod]
    [DataRow("xterm-256color.bin", "xterm-256color", TerminalColorLevel.Color256)]
    [DataRow("xterm.bin", "xterm", TerminalColorLevel.Color16)]
    public void GetColorLevel_UsesTerminfoColors(string fileName, string term, TerminalColorLevel expected)
    {
        var info = LoadFromFile(term, fileName);

        Assert.AreEqual(expected, info.GetColorLevel(TerminalColorLevel.None));
    }

    [TestMethod]
    public void GetColorLevel_DetectsTrueColor_FromExtendedTcBoolean()
    {
        var term = "xenoatom-truecolor-tc";
        var bytes = BuildTerminfoWithExtendedCapabilities(term, extendedBoolName: "Tc", extendedBoolValue: true);

        var info = LoadFromBytes(term, bytes);

        Assert.AreEqual(TerminalColorLevel.TrueColor, info.GetColorLevel(TerminalColorLevel.None));
    }

    [TestMethod]
    public void GetColorLevel_DetectsTrueColor_FromExtendedSetrgbfString()
    {
        var term = "xenoatom-truecolor-setrgbf";
        var bytes = BuildTerminfoWithExtendedCapabilities(
            term,
            extendedStringName: "setrgbf",
            extendedStringValue: "\u001b[38;2;%p1%d;%p2%d;%p3%dm");

        var info = LoadFromBytes(term, bytes);

        Assert.AreEqual(TerminalColorLevel.TrueColor, info.GetColorLevel(TerminalColorLevel.None));
    }

    [TestMethod]
    public void TryLoad_ReturnsFalse_ForTruncatedHeader()
    {
        var term = "xenoatom-bad-terminfo";
        var bytes = new byte[] { 0x1A, 0x01, 0x00 }; // magic + truncated

        var detected = TryLoadFromBytes(term, bytes, out var info);

        Assert.IsFalse(detected);
        Assert.IsNull(info);
    }

    [TestMethod]
    public void SupportsAlternateScreen_UsesXterm256Color()
    {
        var info = LoadFromFile("xterm-256color", "xterm-256color.bin");

        Assert.IsTrue(info.SupportsAlternateScreen);
    }

    [TestMethod]
    public void SupportsCursorVisibility_UsesXterm256Color()
    {
        var info = LoadFromFile("xterm-256color", "xterm-256color.bin");

        Assert.IsTrue(info.SupportsCursorVisibility);
    }

    [TestMethod]
    public void SupportsCursorPositioning_UsesXterm256Color()
    {
        var info = LoadFromFile("xterm-256color", "xterm-256color.bin");

        Assert.IsTrue(info.SupportsCursorPositioning);
    }

    [TestMethod]
    public void SupportsAnsiColors_UsesXterm256Color()
    {
        var info = LoadFromFile("xterm-256color", "xterm-256color.bin");

        Assert.IsTrue(info.SupportsAnsiColors);
    }

    [TestMethod]
    [DataRow("xterm-256color.bin", "xterm-256color")]
    [DataRow("xterm.bin", "xterm")]
    public void TryLoad_LoadsFromExplicitTerminfoFile(string fileName, string term)
    {
        var filePath = GetTestDataPath(fileName);
        var originalTerminfo = Environment.GetEnvironmentVariable("TERMINFO");
        try
        {
            Environment.SetEnvironmentVariable("TERMINFO", filePath);

            var detected = UnixTerminfo.TryLoad(term, out var info);

            Assert.IsTrue(detected);
            Assert.IsNotNull(info);
        }
        finally
        {
            Environment.SetEnvironmentVariable("TERMINFO", originalTerminfo);
        }
    }

    [TestMethod]
    public void TryLoad_FindsLetterDirectoryEntry()
    {
        var term = "xterm-256color";
        var bytes = File.ReadAllBytes(GetTestDataPath("xterm-256color.bin"));
        var tempRoot = CreateTempDirectory();
        var entryDirectory = Path.Combine(tempRoot, term[0].ToString());
        Directory.CreateDirectory(entryDirectory);
        var entryPath = Path.Combine(entryDirectory, term);
        File.WriteAllBytes(entryPath, bytes);

        var originalTerminfo = Environment.GetEnvironmentVariable("TERMINFO");
        try
        {
            Environment.SetEnvironmentVariable("TERMINFO", tempRoot);

            var detected = UnixTerminfo.TryLoad(term, out var info);

            Assert.IsTrue(detected);
            Assert.IsNotNull(info);
        }
        finally
        {
            Environment.SetEnvironmentVariable("TERMINFO", originalTerminfo);
            TryDeleteDirectory(tempRoot);
        }
    }

    [TestMethod]
    public void TryLoad_FindsHexDirectoryEntry()
    {
        var term = "xterm-256color";
        var bytes = File.ReadAllBytes(GetTestDataPath("xterm-256color.bin"));
        var tempRoot = CreateTempDirectory();
        var hexName = ((int)term[0]).ToString("x2", CultureInfo.InvariantCulture);
        var entryDirectory = Path.Combine(tempRoot, hexName);
        Directory.CreateDirectory(entryDirectory);
        var entryPath = Path.Combine(entryDirectory, term);
        File.WriteAllBytes(entryPath, bytes);

        var originalTerminfo = Environment.GetEnvironmentVariable("TERMINFO");
        try
        {
            Environment.SetEnvironmentVariable("TERMINFO", tempRoot);

            var detected = UnixTerminfo.TryLoad(term, out var info);

            Assert.IsTrue(detected);
            Assert.IsNotNull(info);
        }
        finally
        {
            Environment.SetEnvironmentVariable("TERMINFO", originalTerminfo);
            TryDeleteDirectory(tempRoot);
        }
    }

    private static UnixTerminfo LoadFromFile(string term, string fileName)
    {
        var filePath = GetTestDataPath(fileName);
        var tempRoot = CreateTempDirectory();
        var entryDirectory = Path.Combine(tempRoot, term[0].ToString());
        Directory.CreateDirectory(entryDirectory);
        var entryPath = Path.Combine(entryDirectory, term);
        File.Copy(filePath, entryPath, overwrite: true);

        var originalTerminfo = Environment.GetEnvironmentVariable("TERMINFO");
        try
        {
            Environment.SetEnvironmentVariable("TERMINFO", tempRoot);

            var detected = UnixTerminfo.TryLoad(term, out var info);

            Assert.IsTrue(detected);
            Assert.IsNotNull(info);
            return info;
        }
        finally
        {
            Environment.SetEnvironmentVariable("TERMINFO", originalTerminfo);
            TryDeleteDirectory(tempRoot);
        }
    }

    private static UnixTerminfo LoadFromBytes(string term, byte[] bytes)
    {
        var detected = TryLoadFromBytes(term, bytes, out var info);

        Assert.IsTrue(detected);
        Assert.IsNotNull(info);
        return info;
    }

    private static bool TryLoadFromBytes(string term, byte[] bytes, out UnixTerminfo? info)
    {
        var tempRoot = CreateTempDirectory();
        var entryDirectory = Path.Combine(tempRoot, term[0].ToString());
        Directory.CreateDirectory(entryDirectory);
        var entryPath = Path.Combine(entryDirectory, term);
        File.WriteAllBytes(entryPath, bytes);

        var originalTerminfo = Environment.GetEnvironmentVariable("TERMINFO");
        try
        {
            Environment.SetEnvironmentVariable("TERMINFO", tempRoot);
            return UnixTerminfo.TryLoad(term, out info);
        }
        finally
        {
            Environment.SetEnvironmentVariable("TERMINFO", originalTerminfo);
            TryDeleteDirectory(tempRoot);
        }
    }

    private static byte[] BuildTerminfoWithExtendedCapabilities(
        string term,
        string? extendedBoolName = null,
        bool extendedBoolValue = false,
        string? extendedStringName = null,
        string? extendedStringValue = null)
    {
        if (extendedBoolName is null && extendedStringName is null)
        {
            throw new ArgumentException("At least one extended capability must be provided.", nameof(extendedBoolName));
        }

        if (extendedBoolName is not null && extendedStringName is not null)
        {
            throw new ArgumentException("Only one extended capability is supported by this helper.", nameof(extendedBoolName));
        }

        // We use the ncurses extended format header (magic 0x021E) to exercise the parser.
        // The file is intentionally minimal: no standard booleans/numbers/strings, only an extended block.
        var bytes = new List<byte>(128);

        WriteUInt16(bytes, 0x021E); // magic
        var namesBytes = System.Text.Encoding.ASCII.GetBytes(term + "\0");
        WriteUInt16(bytes, (ushort)namesBytes.Length); // namesSize
        WriteUInt16(bytes, 0); // boolCount
        WriteUInt16(bytes, 0); // numCount
        WriteUInt16(bytes, 0); // strCount
        WriteUInt16(bytes, 0); // strTableSize

        bytes.AddRange(namesBytes);
        if ((namesBytes.Length & 1) != 0)
        {
            bytes.Add(0); // pad to short boundary
        }

        var extBoolCount = extendedBoolName is not null ? 1 : 0;
        var extNumCount = 0;
        var extStrCount = extendedStringName is not null ? 1 : 0;
        var nameCount = extBoolCount + extNumCount + extStrCount;
        var offsetCount = extStrCount + nameCount;

        // Build the extended string table as a single NUL-terminated blob containing any string values first,
        // then the capability name. Both the string offsets and name offsets index into the same table.
        var extStringTable = new List<byte>(64);
        short stringOffset = -1;
        short nameOffset;

        if (extendedStringName is not null)
        {
            var value = extendedStringValue ?? string.Empty;
            var valueBytes = System.Text.Encoding.ASCII.GetBytes(value + "\0");
            stringOffset = 0;
            extStringTable.AddRange(valueBytes);
            nameOffset = checked((short)extStringTable.Count);
            extStringTable.AddRange(System.Text.Encoding.ASCII.GetBytes(extendedStringName + "\0"));
        }
        else
        {
            nameOffset = 0;
            extStringTable.AddRange(System.Text.Encoding.ASCII.GetBytes(extendedBoolName + "\0"));
        }

        // ncurses extended header:
        //   extBoolCount, extNumCount, extStrCount, offsetCount, extStrTableSize
        WriteInt16(bytes, (short)extBoolCount);
        WriteInt16(bytes, (short)extNumCount);
        WriteInt16(bytes, (short)extStrCount);
        WriteInt16(bytes, (short)offsetCount);
        WriteInt16(bytes, checked((short)extStringTable.Count));

        if (extBoolCount == 1)
        {
            bytes.Add(extendedBoolValue ? (byte)1 : (byte)0);
            bytes.Add(0); // pad to short boundary (extBoolCount is odd)
        }

        if (extStrCount == 1)
        {
            WriteInt16(bytes, stringOffset);
        }

        WriteInt16(bytes, nameOffset);

        bytes.AddRange(extStringTable);
        return bytes.ToArray();
    }

    private static void WriteUInt16(List<byte> bytes, ushort value)
    {
        bytes.Add((byte)(value & 0xFF));
        bytes.Add((byte)((value >> 8) & 0xFF));
    }

    private static void WriteInt16(List<byte> bytes, short value) => WriteUInt16(bytes, unchecked((ushort)value));

    private static string GetTestDataPath(string fileName)
    {
        var baseDir = AppContext.BaseDirectory;
        var path = Path.Combine(baseDir, "TestData", "terminfo", fileName);
        if (!File.Exists(path))
        {
            Assert.Fail($"Terminfo test data not found: {path}");
        }

        return path;
    }

    private static string CreateTempDirectory()
    {
        var path = Path.Combine(Path.GetTempPath(), $"xenoatom-terminfo-{Guid.NewGuid():N}");
        Directory.CreateDirectory(path);
        return path;
    }

    private static void TryDeleteDirectory(string path)
    {
        try
        {
            if (Directory.Exists(path))
            {
                Directory.Delete(path, recursive: true);
            }
        }
        catch
        {
            // Best-effort cleanup.
        }
    }
}
