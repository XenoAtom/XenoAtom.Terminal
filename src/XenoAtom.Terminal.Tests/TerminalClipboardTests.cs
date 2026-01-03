// Copyright (c) Alexandre Mutel. All rights reserved.
// Licensed under the BSD-Clause 2 license.
// See license.txt file in the project root for full license information.

using System.Text;
using XenoAtom.Terminal.Backends;

namespace XenoAtom.Terminal.Tests;

[TestClass]
public sealed class TerminalClipboardTests
{
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
}
