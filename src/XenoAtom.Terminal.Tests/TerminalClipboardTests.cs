// Copyright (c) Alexandre Mutel. All rights reserved.
// Licensed under the BSD-Clause 2 license.
// See license.txt file in the project root for full license information.

using System.Text;
using System.Runtime.Versioning;
using XenoAtom.Terminal.Backends;

namespace XenoAtom.Terminal.Tests;

[TestClass]
public sealed class TerminalClipboardTests
{
    private const string IntegrationPrefix = "XenoAtom.Terminal clipboard integration";

    [TestCleanup]
    public void Cleanup()
    {
        Terminal.ResetForTests();
    }

    [TestMethod]
    public async Task ClipboardAsync_GetAndSet_WorksWithInMemoryBackend()
    {
        var backend = new InMemoryTerminalBackend();
        Terminal.Initialize(backend);

        Assert.IsTrue(await Terminal.Clipboard.TrySetTextAsync("hello"));
        Assert.AreEqual("hello", Terminal.Clipboard.Text);

        var text = await Terminal.Clipboard.GetTextAsync();
        Assert.AreEqual("hello", text);
    }

    [TestMethod]
    public void Clipboard_GenericFormatRoundTrip_WorksWithInMemoryBackend()
    {
        var backend = new InMemoryTerminalBackend();
        Terminal.Initialize(backend);

        byte[] pngBytes = [0x89, 0x50, 0x4E, 0x47, 0x0D, 0x0A, 0x1A, 0x0A, 0x01, 0x02, 0x03];

        Assert.IsTrue(Terminal.Clipboard.TrySetData("public.png", pngBytes));
        Assert.IsTrue(Terminal.Clipboard.TryGetFormats(out var formats));
        CollectionAssert.AreEqual(new[] { TerminalClipboardFormats.Png }, formats.ToArray());

        Assert.IsTrue(Terminal.Clipboard.TryGetData("PNG", out var roundTripped));
        CollectionAssert.AreEqual(pngBytes, roundTripped);
        Assert.IsNull(Terminal.Clipboard.Text);
    }

    [TestMethod]
    public async Task Clipboard_TextAndDataApisInteroperate_WorksWithInMemoryBackend()
    {
        var backend = new InMemoryTerminalBackend();
        Terminal.Initialize(backend);

        var textBytes = Encoding.UTF8.GetBytes("hello 🌍");
        Assert.IsTrue(Terminal.Clipboard.TrySetData(TerminalClipboardFormats.Text, textBytes));
        Assert.AreEqual("hello 🌍", Terminal.Clipboard.Text);

        Assert.IsTrue(await Terminal.Clipboard.TrySetTextAsync("bonjour"));
        var data = await Terminal.Clipboard.GetDataAsync(TerminalClipboardFormats.Text);
        Assert.IsNotNull(data);
        Assert.AreEqual("bonjour", Encoding.UTF8.GetString(data));
    }

    [OSCondition(OperatingSystems.Windows)]
    [STATestMethod]
    [SupportedOSPlatform("windows")]
    public void Clipboard_TextRoundTrip_WorksWithWindowsBackend()
    {
        RunPlatformClipboardTextRoundTrip(CreateWindowsBackend());
    }

    [TestMethod]
    [OSCondition(OperatingSystems.Linux)]
    [SupportedOSPlatform("linux")]
    public void Clipboard_TextRoundTrip_WorksWithLinuxBackend()
    {
        RunPlatformClipboardTextRoundTrip(CreateUnixBackend());
    }

    [TestMethod]
    [OSCondition(OperatingSystems.OSX)]
    [SupportedOSPlatform("macos")]
    public void Clipboard_TextRoundTrip_WorksWithMacOsBackend()
    {
        RunPlatformClipboardTextRoundTrip(CreateUnixBackend());
    }

    [OSCondition(OperatingSystems.Windows)]
    [STATestMethod]
    [SupportedOSPlatform("windows")]
    public void Clipboard_FormatRoundTrip_WorksWithWindowsBackend()
    {
        RunPlatformClipboardFormatRoundTrip(CreateWindowsBackend());
    }

    [TestMethod]
    [OSCondition(OperatingSystems.Linux)]
    [SupportedOSPlatform("linux")]
    public void Clipboard_FormatRoundTrip_WorksWithLinuxBackend()
    {
        RunPlatformClipboardFormatRoundTrip(CreateUnixBackend());
    }

    [TestMethod]
    [OSCondition(OperatingSystems.OSX)]
    [SupportedOSPlatform("macos")]
    public void Clipboard_FormatRoundTrip_WorksWithMacOsBackend()
    {
        RunPlatformClipboardFormatRoundTrip(CreateUnixBackend());
    }

    [TestMethod]
    public void Clipboard_Osc52Fallback_EmitsSequence_WhenSetClipboardUnavailable()
    {
        var output = new StringWriter();

        var caps = new TerminalCapabilities
        {
            AnsiEnabled = true,
            ColorLevel = TerminalColorLevel.TrueColor,
            SupportsOsc52Clipboard = true,
            SupportsClipboard = false,
            SupportsClipboardGet = false,
            SupportsClipboardSet = false,
            SupportsClipboardFormatsGet = false,
            SupportsClipboardFormatsSet = false,
            IsOutputRedirected = false,
            IsInputRedirected = true,
            TerminalName = "Test",
        };

        var backend = new VirtualTerminalBackend(outWriter: output, errorWriter: TextWriter.Null, capabilities: caps);
        Terminal.Initialize(backend, new TerminalOptions { EnableOsc52Clipboard = true, Osc52ClipboardMaxBytes = 100_000 });

        Assert.IsTrue(Terminal.Clipboard.TrySetText("hello".AsSpan()));

        var content = output.ToString();
        var start = content.IndexOf("\x1b]52;c;", StringComparison.Ordinal);
        Assert.IsGreaterThanOrEqualTo(0, start, "Expected OSC 52 prefix.");
        start += "\x1b]52;c;".Length;
        var end = content.IndexOf('\x07', start);
        Assert.IsGreaterThan(start, end, "Expected OSC 52 terminator.");

        var payload = content.AsSpan(start, end - start);
        var bytes = Convert.FromBase64String(payload.ToString());
        Assert.AreEqual("hello", Encoding.UTF8.GetString(bytes));
    }

    private static void RunPlatformClipboardTextRoundTrip(ITerminalBackend backend)
    {
        Terminal.Initialize(backend, new TerminalOptions { EnableOsc52Clipboard = false });

        if (!Terminal.Clipboard.CanGetText || !Terminal.Clipboard.CanSetText)
        {
            Assert.Inconclusive($"Text clipboard is not available for backend {backend.GetType().Name} on this machine.");
        }

        var marker = $"{IntegrationPrefix} text {Environment.ProcessId} {Guid.NewGuid():N}";
        Assert.IsTrue(Terminal.Clipboard.TrySetText(marker), "Expected writing text to the platform clipboard to succeed.");

        var roundTripped = Terminal.Clipboard.GetTextAsync(timeoutMs: 2000).AsTask().GetAwaiter().GetResult();
        Assert.AreEqual(marker, roundTripped);
    }

    private static void RunPlatformClipboardFormatRoundTrip(ITerminalBackend backend)
    {
        Terminal.Initialize(backend, new TerminalOptions { EnableOsc52Clipboard = false });

        if (!Terminal.Clipboard.CanGetFormats || !Terminal.Clipboard.CanSetFormats)
        {
            Assert.Inconclusive($"Named clipboard formats are not available for backend {backend.GetType().Name} on this machine.");
        }

        byte[] pngBytes = [0x89, 0x50, 0x4E, 0x47, 0x0D, 0x0A, 0x1A, 0x0A, 0x10, 0x20, 0x30, 0x40];

        Assert.IsTrue(Terminal.Clipboard.TrySetData(TerminalClipboardFormats.Png, pngBytes), "Expected writing PNG bytes to the platform clipboard to succeed.");
        Assert.IsTrue(Terminal.Clipboard.TryGetFormats(out var formats), "Expected the platform clipboard to expose formats after setting data.");
        CollectionAssert.Contains(formats.ToArray(), TerminalClipboardFormats.Png);

        Assert.IsTrue(Terminal.Clipboard.TryGetData(TerminalClipboardFormats.Png, out var roundTripped), "Expected reading PNG bytes from the platform clipboard to succeed.");
        CollectionAssert.AreEqual(pngBytes, roundTripped);
    }

    [SupportedOSPlatform("windows")]
    private static ITerminalBackend CreateWindowsBackend() => new WindowsConsoleTerminalBackend();

    [SupportedOSPlatform("linux")]
    [SupportedOSPlatform("macos")]
    private static ITerminalBackend CreateUnixBackend() => new UnixTerminalBackend();
}
