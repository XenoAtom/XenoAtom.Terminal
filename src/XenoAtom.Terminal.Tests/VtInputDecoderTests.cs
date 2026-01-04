// Copyright (c) Alexandre Mutel. All rights reserved.
// Licensed under the BSD-Clause 2 license.
// See license.txt file in the project root for full license information.

using XenoAtom.Terminal.Internal;

namespace XenoAtom.Terminal.Tests;

[TestClass]
public sealed class VtInputDecoderTests
{
    [TestMethod]
    public void Decode_CsiArrowKey_ProducesKeyEvent()
    {
        using var decoder = new VtInputDecoder();
        using var broadcaster = new TerminalEventBroadcaster();

        decoder.Decode("\x1b[A".AsSpan(), isFinalChunk: true, options: new TerminalInputOptions(), broadcaster);

        Assert.IsTrue(broadcaster.TryReadEvent(out var ev));
        var key = (TerminalKeyEvent)ev;
        Assert.AreEqual(TerminalKey.Up, key.Key);
    }

    [TestMethod]
    public void Decode_EscapeKey_RequiresFinalFlushInStreamingMode()
    {
        using var decoder = new VtInputDecoder();
        using var broadcaster = new TerminalEventBroadcaster();

        decoder.Decode("\x1b".AsSpan(), isFinalChunk: false, options: new TerminalInputOptions(), broadcaster);
        Assert.IsFalse(broadcaster.TryReadEvent(out _));

        decoder.Decode(ReadOnlySpan<char>.Empty, isFinalChunk: true, options: new TerminalInputOptions(), broadcaster);

        Assert.IsTrue(broadcaster.TryReadEvent(out var ev));
        var key = (TerminalKeyEvent)ev;
        Assert.AreEqual(TerminalKey.Escape, key.Key);
    }

    [TestMethod]
    public void Decode_AltChar_EscToken_ProducesAltModifiedKey()
    {
        using var decoder = new VtInputDecoder();
        using var broadcaster = new TerminalEventBroadcaster();

        decoder.Decode(("\x1b" + "a").AsSpan(), isFinalChunk: true, options: new TerminalInputOptions(), broadcaster);

        Assert.IsTrue(broadcaster.TryReadEvent(out var ev));
        var key = (TerminalKeyEvent)ev;
        Assert.AreEqual(TerminalKey.Unknown, key.Key);
        Assert.AreEqual('a', key.Char);
        Assert.IsTrue(key.Modifiers.HasFlag(TerminalModifiers.Alt));
    }

    [TestMethod]
    public void Decode_BracketedPaste_ProducesPasteEvent()
    {
        using var decoder = new VtInputDecoder();
        using var broadcaster = new TerminalEventBroadcaster();

        decoder.Decode("\x1b[200~hello\x1b[201~".AsSpan(), isFinalChunk: true, options: new TerminalInputOptions(), broadcaster);

        Assert.IsTrue(broadcaster.TryReadEvent(out var ev));
        var paste = (TerminalPasteEvent)ev;
        Assert.AreEqual("hello", paste.Text);
    }

    [TestMethod]
    public void Decode_BracketedPaste_Chunked_ProducesPasteEvent()
    {
        using var decoder = new VtInputDecoder();
        using var broadcaster = new TerminalEventBroadcaster();

        // Windows VT input can deliver sequences character-by-character; ensure streaming decode works.
        const string input = "\x1b[200~hello\x1b[201~";
        Span<char> one = stackalloc char[1];
        foreach (var ch in input)
        {
            one[0] = ch;
            decoder.Decode(one, isFinalChunk: false, options: new TerminalInputOptions(), broadcaster);
        }

        decoder.Decode(ReadOnlySpan<char>.Empty, isFinalChunk: true, options: new TerminalInputOptions(), broadcaster);

        Assert.IsTrue(broadcaster.TryReadEvent(out var ev));
        Assert.IsInstanceOfType(ev, typeof(TerminalPasteEvent));
        Assert.AreEqual("hello", ((TerminalPasteEvent)ev).Text);
        Assert.IsFalse(broadcaster.TryReadEvent(out _));
    }

    [TestMethod]
    public void Decode_SgrMouseEvent_RequiresMouseEnabled()
    {
        using var decoder = new VtInputDecoder();

        using var broadcasterDisabled = new TerminalEventBroadcaster();
        decoder.Decode("\x1b[<0;10;5M".AsSpan(), isFinalChunk: true, options: new TerminalInputOptions { EnableMouseEvents = false }, broadcasterDisabled);
        Assert.IsFalse(broadcasterDisabled.TryReadEvent(out _));

        using var broadcasterEnabled = new TerminalEventBroadcaster();
        decoder.Decode("\x1b[<0;10;5M".AsSpan(), isFinalChunk: true, options: new TerminalInputOptions { EnableMouseEvents = true }, broadcasterEnabled);

        Assert.IsTrue(broadcasterEnabled.TryReadEvent(out var ev));
        var mouse = (TerminalMouseEvent)ev;
        Assert.AreEqual(TerminalMouseKind.Down, mouse.Kind);
        Assert.AreEqual(TerminalMouseButton.Left, mouse.Button);
        Assert.AreEqual(9, mouse.X);
        Assert.AreEqual(4, mouse.Y);
    }

    [TestMethod]
    public void Decode_CtrlC_ControlChar_PublishesSignal_WhenCaptureEnabled()
    {
        using var decoder = new VtInputDecoder();
        using var broadcaster = new TerminalEventBroadcaster();

        decoder.Decode("\x03".AsSpan(), isFinalChunk: true, options: new TerminalInputOptions { CaptureCtrlC = true, TreatControlCAsInput = false }, broadcaster);

        Assert.IsTrue(broadcaster.TryReadEvent(out var first));
        Assert.IsInstanceOfType(first, typeof(TerminalSignalEvent));
        Assert.AreEqual(TerminalSignalKind.Interrupt, ((TerminalSignalEvent)first).Kind);

        Assert.IsTrue(broadcaster.TryReadEvent(out var second));
        Assert.IsInstanceOfType(second, typeof(TerminalKeyEvent));
        Assert.AreEqual('\x03', ((TerminalKeyEvent)second).Char);
    }
}
