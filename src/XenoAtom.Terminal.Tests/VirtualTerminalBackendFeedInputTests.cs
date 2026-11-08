// Copyright (c) Alexandre Mutel. All rights reserved.
// Licensed under the BSD-Clause 2 license.
// See license.txt file in the project root for full license information.

using XenoAtom.Terminal.Backends;

namespace XenoAtom.Terminal.Tests;

[TestClass]
public sealed class VirtualTerminalBackendFeedInputTests
{
    [TestMethod]
    public void FeedInput_BeforeStartInput_Throws()
    {
        using var backend = new InMemoryTerminalBackend();

        Assert.ThrowsExactly<InvalidOperationException>(() => backend.FeedInput("\x1b[A"));
        Assert.IsFalse(backend.TryReadEvent(out _));
    }

    [TestMethod]
    public void FeedInput_AfterStopInput_Throws()
    {
        using var backend = new InMemoryTerminalBackend();
        backend.StartInput(new TerminalInputOptions());
        backend.StopInputAsync(CancellationToken.None).GetAwaiter().GetResult();

        Assert.ThrowsExactly<InvalidOperationException>(() => backend.FeedInput("\x1b[A"));
    }

    [TestMethod]
    public void FeedInput_AfterDispose_Throws()
    {
        var backend = new InMemoryTerminalBackend();
        backend.StartInput(new TerminalInputOptions());
        backend.Dispose();

        Assert.ThrowsExactly<ObjectDisposedException>(() => backend.FeedInput("a"));
    }

    [TestMethod]
    public void FeedInput_CsiArrow_PublishesKeyEvent()
    {
        using var backend = new InMemoryTerminalBackend();
        backend.StartInput(new TerminalInputOptions());

        backend.FeedInput("\x1b[A");

        var key = ReadSingle<TerminalKeyEvent>(backend);
        Assert.AreEqual(TerminalKey.Up, key.Key);
        Assert.AreEqual(TerminalModifiers.None, key.Modifiers);
    }

    [TestMethod]
    public void FeedInput_Text_PublishesKeyAndTextEvents()
    {
        using var backend = new InMemoryTerminalBackend();
        backend.StartInput(new TerminalInputOptions());

        backend.FeedInput("ab");

        var events = ReadAll(backend);
        Assert.HasCount(3, events);
        Assert.AreEqual('a', ((TerminalKeyEvent)events[0]).Char);
        Assert.AreEqual('b', ((TerminalKeyEvent)events[1]).Char);
        Assert.AreEqual("ab", ((TerminalTextEvent)events[2]).Text);
    }

    [TestMethod]
    public void FeedInput_LoneEscape_IsFlushedAsEscapeKey()
    {
        using var backend = new InMemoryTerminalBackend();
        backend.StartInput(new TerminalInputOptions());

        backend.FeedInput("\x1b");

        var key = ReadSingle<TerminalKeyEvent>(backend);
        Assert.AreEqual(TerminalKey.Escape, key.Key);
    }

    [TestMethod]
    public void FeedInput_NonFinalLoneEscape_IsHeldUntilFlushed()
    {
        using var backend = new InMemoryTerminalBackend();
        backend.StartInput(new TerminalInputOptions());

        backend.FeedInput("\x1b", isFinalChunk: false);
        Assert.IsFalse(backend.TryReadEvent(out _));

        backend.FeedInput(ReadOnlySpan<char>.Empty);

        var key = ReadSingle<TerminalKeyEvent>(backend);
        Assert.AreEqual(TerminalKey.Escape, key.Key);
    }

    [TestMethod]
    public void FeedInput_SequenceSplitAcrossChunks_IsDecodedAsOneKey()
    {
        using var backend = new InMemoryTerminalBackend();
        backend.StartInput(new TerminalInputOptions());

        backend.FeedInput("\x1b[", isFinalChunk: false);
        Assert.IsFalse(backend.TryReadEvent(out _));

        backend.FeedInput("A");

        var key = ReadSingle<TerminalKeyEvent>(backend);
        Assert.AreEqual(TerminalKey.Up, key.Key);
    }

    [TestMethod]
    public void FeedInput_SgrMouse_PublishesMouseEvents_WhenMouseEventsEnabled()
    {
        using var backend = new InMemoryTerminalBackend();
        backend.StartInput(new TerminalInputOptions { EnableMouseEvents = true, MouseMode = TerminalMouseMode.Clicks });

        backend.FeedInput("\x1b[<0;3;2M\x1b[<0;3;2m\x1b[<64;10;5M");

        var events = ReadAll(backend);
        Assert.HasCount(3, events);

        var down = (TerminalMouseEvent)events[0];
        Assert.AreEqual(TerminalMouseKind.Down, down.Kind);
        Assert.AreEqual(TerminalMouseButton.Left, down.Button);
        Assert.AreEqual(2, down.X);
        Assert.AreEqual(1, down.Y);

        var up = (TerminalMouseEvent)events[1];
        Assert.AreEqual(TerminalMouseKind.Up, up.Kind);
        Assert.AreEqual(TerminalMouseButton.Left, up.Button);

        var wheel = (TerminalMouseEvent)events[2];
        Assert.AreEqual(TerminalMouseKind.Wheel, wheel.Kind);
        Assert.AreEqual(9, wheel.X);
        Assert.AreEqual(4, wheel.Y);
        Assert.AreNotEqual(0, wheel.WheelDelta);
    }

    [TestMethod]
    public void FeedInput_SgrMouse_IsIgnored_WhenMouseEventsDisabled()
    {
        using var backend = new InMemoryTerminalBackend();
        backend.StartInput(new TerminalInputOptions { EnableMouseEvents = false });

        backend.FeedInput("\x1b[<0;3;2M");

        var events = ReadAll(backend);
        Assert.IsFalse(events.Any(static ev => ev is TerminalMouseEvent));
    }

    [TestMethod]
    public void FeedInput_UsesOptions_FromLatestStartInput()
    {
        using var backend = new InMemoryTerminalBackend();
        backend.StartInput(new TerminalInputOptions { EnableMouseEvents = false });
        backend.FeedInput("\x1b[<0;3;2M");
        Assert.IsFalse(ReadAll(backend).Any(static ev => ev is TerminalMouseEvent));

        // A StartInput while running updates the options, as with the real backends.
        backend.StartInput(new TerminalInputOptions { EnableMouseEvents = true, MouseMode = TerminalMouseMode.Clicks });
        backend.FeedInput("\x1b[<0;3;2M");

        var mouse = ReadSingle<TerminalMouseEvent>(backend);
        Assert.AreEqual(TerminalMouseKind.Down, mouse.Kind);
    }

    [TestMethod]
    public void FeedInput_BracketedPasteAcrossChunks_PublishesOnePasteEvent()
    {
        using var backend = new InMemoryTerminalBackend();
        backend.StartInput(new TerminalInputOptions());

        backend.FeedInput("\x1b[200~hel", isFinalChunk: false);
        Assert.IsFalse(backend.TryReadEvent(out _));

        backend.FeedInput("lo\r\nworld\x1b[201~");

        var paste = ReadSingle<TerminalPasteEvent>(backend);
        Assert.AreEqual("hello\nworld", paste.Text);
    }

    [TestMethod]
    public void StopInput_FlushesAndResetsDecoderState()
    {
        using var backend = new InMemoryTerminalBackend();
        backend.StartInput(new TerminalInputOptions());
        backend.FeedInput("\x1b", isFinalChunk: false);

        backend.StopInputAsync(CancellationToken.None).GetAwaiter().GetResult();

        // The pending ESC is flushed when input stops, as when a real input loop ends.
        var key = ReadSingle<TerminalKeyEvent>(backend);
        Assert.AreEqual(TerminalKey.Escape, key.Key);

        // A new run starts from a clean decoder: "[A" is plain text, not the tail of a CSI.
        backend.StartInput(new TerminalInputOptions());
        backend.FeedInput("[A");

        var events = ReadAll(backend);
        Assert.IsFalse(events.OfType<TerminalKeyEvent>().Any(static k => k.Key == TerminalKey.Up));
        Assert.AreEqual("[A", events.OfType<TerminalTextEvent>().Single().Text);
    }

    [TestMethod]
    public void FeedInput_EventsReachTheTerminalInstance()
    {
        var backend = new InMemoryTerminalBackend();
        using var terminal = new TerminalInstance(backend);
        terminal.StartInput();

        backend.FeedInput("\x1b[B");

        Assert.IsTrue(terminal.TryReadEvent(out var ev));
        var key = (TerminalKeyEvent)ev;
        Assert.AreEqual(TerminalKey.Down, key.Key);
        Assert.IsFalse(terminal.TryReadEvent(out _));
    }

    private static T ReadSingle<T>(InMemoryTerminalBackend backend) where T : TerminalEvent
    {
        var events = ReadAll(backend);
        Assert.HasCount(1, events, $"Expected a single event but got: {string.Join(", ", events.Select(static e => e.GetType().Name))}");
        return (T)events[0];
    }

    private static List<TerminalEvent> ReadAll(InMemoryTerminalBackend backend)
    {
        var events = new List<TerminalEvent>();
        while (backend.TryReadEvent(out var ev))
        {
            events.Add(ev);
        }

        return events;
    }
}
