// Copyright (c) Alexandre Mutel. All rights reserved.
// Licensed under the BSD-Clause 2 license.
// See license.txt file in the project root for full license information.

using System.Reflection;
using XenoAtom.Ansi;
using XenoAtom.Terminal.Backends;

namespace XenoAtom.Terminal.Tests;

[TestClass]
public class TerminalTests
{
    [TestCleanup]
    public void Cleanup()
    {
        Terminal.ResetForTests();
    }

    [TestMethod]
    public void Initialize_WithVirtualBackend_ExposesWriters()
    {
        var backend = new InMemoryTerminalBackend();
        Terminal.Initialize(backend);

        Terminal.Write("Hello");
        Terminal.WriteLine(" World");

        Assert.IsTrue(backend.GetOutText().Contains("Hello World", StringComparison.Ordinal));
    }

    [TestMethod]
    public void OpenSession_DisposesGlobalInstance_OnDispose()
    {
        var backend = new InMemoryTerminalBackend();
        using (Terminal.Open(backend, force: true))
        {
            Assert.IsTrue(Terminal.IsInitialized);
        }

        Assert.IsFalse(Terminal.IsInitialized);
    }

    [TestMethod]
    public void WriteMarkup_RendersAnsi_WhenEnabled()
    {
        var backend = new InMemoryTerminalBackend();
        Terminal.Initialize(backend);

        Terminal.WriteMarkup("[red]Hi[/]");

        var text = backend.GetOutText();
        Assert.IsTrue(text.Contains("Hi", StringComparison.Ordinal));
        Assert.IsTrue(text.Contains("\x1b[", StringComparison.Ordinal));
    }

    [TestMethod]
    public void MeasureStyledWidth_WorksForMarkupAndAnsi()
    {
        var backend = new InMemoryTerminalBackend();
        Terminal.Initialize(backend);

        Assert.AreEqual(2, Terminal.MeasureStyledWidth("[red]Hi[/]"));
        Assert.AreEqual(2, Terminal.MeasureStyledWidth("\x1b[31mHi\x1b[0m"));
    }

    [TestMethod]
    public async Task Cursor_GetPositionAsync_ReturnsCurrentPosition()
    {
        var backend = new InMemoryTerminalBackend();
        Terminal.Initialize(backend);

        Terminal.SetCursorPosition(3, 4);

        var pos = await Terminal.Cursor.GetPositionAsync();
        Assert.AreEqual(3, pos.Column);
        Assert.AreEqual(4, pos.Row);
    }

    [TestMethod]
    public async Task TerminalEventBroadcaster_DefaultBuffer_IsBoundedAndDropsOldest()
    {
        var assembly = typeof(Terminal).Assembly;
        var broadcasterType = assembly.GetType("XenoAtom.Terminal.Internal.TerminalEventBroadcaster", throwOnError: true)!;
        var broadcaster = Activator.CreateInstance(broadcasterType)!;
        try
        {
            var capacityField = broadcasterType.GetField("DefaultBufferCapacity", BindingFlags.NonPublic | BindingFlags.Static)!;
            var capacity = (int)capacityField.GetRawConstantValue()!;

            var publish = broadcasterType.GetMethod("Publish", BindingFlags.Public | BindingFlags.Instance)!;
            for (var i = 0; i < capacity + 10; i++)
            {
                publish.Invoke(broadcaster, [new TerminalTextEvent { Text = i.ToString() }]);
            }

            var readEventAsync = broadcasterType.GetMethod("ReadEventAsync", BindingFlags.Public | BindingFlags.Instance)!;
            var valueTask = (ValueTask<TerminalEvent>)readEventAsync.Invoke(broadcaster, [CancellationToken.None])!;
            var ev = await valueTask;

            var text = (TerminalTextEvent)ev;
            Assert.AreEqual("10", text.Text);
        }
        finally
        {
            ((IDisposable)broadcaster).Dispose();
        }
    }

    [TestMethod]
    public async Task ReadEventAsync_FiltersMouseMove_WhenMouseModeClicks()
    {
        var backend = new InMemoryTerminalBackend();
        Terminal.Initialize(backend);
        Terminal.StartInput(new TerminalInputOptions { EnableMouseEvents = true, MouseMode = TerminalMouseMode.Clicks });

        var task = Terminal.ReadEventAsync().AsTask();

        backend.PushEvent(new TerminalMouseEvent { X = 0, Y = 0, Button = TerminalMouseButton.None, Kind = TerminalMouseKind.Move });
        await Task.Delay(20);
        Assert.IsFalse(task.IsCompleted);

        backend.PushEvent(new TerminalMouseEvent { X = 0, Y = 0, Button = TerminalMouseButton.Left, Kind = TerminalMouseKind.Down });

        var ev = await task;
        var mouse = (TerminalMouseEvent)ev;
        Assert.AreEqual(TerminalMouseKind.Down, mouse.Kind);
        Assert.AreEqual(TerminalMouseButton.Left, mouse.Button);
    }

    [TestMethod]
    public async Task EnableMouseInput_OverridesMouseFiltering_AndRestores()
    {
        var backend = new InMemoryTerminalBackend();
        Terminal.Initialize(backend);
        Terminal.StartInput(new TerminalInputOptions { EnableMouseEvents = false, MouseMode = TerminalMouseMode.Off });

        using (Terminal.EnableMouseInput(TerminalMouseMode.Clicks))
        {
            var task = Terminal.ReadEventAsync().AsTask();
            backend.PushEvent(new TerminalMouseEvent { X = 1, Y = 2, Button = TerminalMouseButton.Left, Kind = TerminalMouseKind.Down });
            var ev = await task;
            Assert.AreEqual(TerminalMouseKind.Down, ((TerminalMouseEvent)ev).Kind);
        }

        using var cts = new CancellationTokenSource(50);
        var filtered = Terminal.ReadEventAsync(cts.Token).AsTask();
        backend.PushEvent(new TerminalMouseEvent { X = 1, Y = 2, Button = TerminalMouseButton.Left, Kind = TerminalMouseKind.Down });

        try
        {
            await filtered;
            Assert.Fail("Expected OperationCanceledException");
        }
        catch (OperationCanceledException)
        {
        }
    }

    [TestMethod]
    public async Task ResizeEvents_AreAlwaysPublished_EvenIfDisabledInInputOptions()
    {
        var backend = new InMemoryTerminalBackend();
        Terminal.Initialize(backend);
        Terminal.StartInput(new TerminalInputOptions { EnableMouseEvents = false, MouseMode = TerminalMouseMode.Off });

        backend.PushEvent(new TerminalResizeEvent { Size = new TerminalSize(123, 45) });
        var ev = await Terminal.ReadEventAsync();
        Assert.AreEqual(123, ((TerminalResizeEvent)ev).Size.Columns);
    }

    [TestMethod]
    public async Task ReadEventAsync_SynthesizesDoubleClick_FromMouseDown()
    {
        var backend = new InMemoryTerminalBackend();
        Terminal.Initialize(backend);
        Terminal.StartInput(new TerminalInputOptions { EnableMouseEvents = true, MouseMode = TerminalMouseMode.Clicks });

        backend.PushEvent(new TerminalMouseEvent { X = 5, Y = 2, Button = TerminalMouseButton.Left, Kind = TerminalMouseKind.Down });
        backend.PushEvent(new TerminalMouseEvent { X = 5, Y = 2, Button = TerminalMouseButton.Left, Kind = TerminalMouseKind.Up });
        backend.PushEvent(new TerminalMouseEvent { X = 5, Y = 2, Button = TerminalMouseButton.Left, Kind = TerminalMouseKind.Down });
        backend.PushEvent(new TerminalMouseEvent { X = 5, Y = 2, Button = TerminalMouseButton.Left, Kind = TerminalMouseKind.Up });

        Assert.AreEqual(TerminalMouseKind.Down, ((TerminalMouseEvent)await Terminal.ReadEventAsync()).Kind);
        Assert.AreEqual(TerminalMouseKind.Up, ((TerminalMouseEvent)await Terminal.ReadEventAsync()).Kind);
        Assert.AreEqual(TerminalMouseKind.Down, ((TerminalMouseEvent)await Terminal.ReadEventAsync()).Kind);

        var doubleClick = (TerminalMouseEvent)await Terminal.ReadEventAsync();
        Assert.AreEqual(TerminalMouseKind.DoubleClick, doubleClick.Kind);
        Assert.AreEqual(TerminalMouseButton.Left, doubleClick.Button);
        Assert.AreEqual(5, doubleClick.X);
        Assert.AreEqual(2, doubleClick.Y);

        Assert.AreEqual(TerminalMouseKind.Up, ((TerminalMouseEvent)await Terminal.ReadEventAsync()).Kind);
    }

    [TestMethod]
    public async Task ReadEventAsync_FiltersMouseMoveButKeepsDrag_WhenMouseModeDrag()
    {
        var backend = new InMemoryTerminalBackend();
        Terminal.Initialize(backend);
        Terminal.StartInput(new TerminalInputOptions { EnableMouseEvents = true, MouseMode = TerminalMouseMode.Drag });

        var task = Terminal.ReadEventAsync().AsTask();

        backend.PushEvent(new TerminalMouseEvent { X = 0, Y = 0, Button = TerminalMouseButton.None, Kind = TerminalMouseKind.Move });
        await Task.Delay(20);
        Assert.IsFalse(task.IsCompleted);

        backend.PushEvent(new TerminalMouseEvent { X = 1, Y = 2, Button = TerminalMouseButton.Left, Kind = TerminalMouseKind.Drag });

        var ev = await task;
        var mouse = (TerminalMouseEvent)ev;
        Assert.AreEqual(TerminalMouseKind.Drag, mouse.Kind);
        Assert.AreEqual(1, mouse.X);
        Assert.AreEqual(2, mouse.Y);
    }

    [TestMethod]
    public async Task ReadEventAsync_ReturnsResizeEvent_WhenPushed()
    {
        var backend = new InMemoryTerminalBackend();
        Terminal.Initialize(backend);
        Terminal.StartInput(new TerminalInputOptions { EnableMouseEvents = false, MouseMode = TerminalMouseMode.Off });
        backend.PushEvent(new TerminalResizeEvent { Size = new TerminalSize(100, 40) });
        var ev = await Terminal.ReadEventAsync();
        Assert.AreEqual(100, ((TerminalResizeEvent)ev).Size.Columns);
    }

    [TestMethod]
    public void WriteErrorAtomic_WritesToErrorStream()
    {
        var backend = new InMemoryTerminalBackend();
        Terminal.Initialize(backend);

        Terminal.WriteErrorAtomic(static w =>
        {
            w.Write("A");
            w.Write("B");
        });

        Assert.AreEqual("AB", backend.GetErrorText());
        Assert.AreEqual(string.Empty, backend.GetOutText());
    }

    [TestMethod]
    public void CursorPosition_SetGet_WorksWithVirtualBackend()
    {
        var backend = new InMemoryTerminalBackend();
        Terminal.Initialize(backend);

        Terminal.CursorLeft = 12;
        Terminal.CursorTop = 3;

        Assert.AreEqual(12, Terminal.CursorLeft);
        Assert.AreEqual(3, Terminal.CursorTop);
        Assert.AreEqual(new TerminalPosition(12, 3), Terminal.GetCursorPosition());
    }

    [TestMethod]
    public void CursorVisible_SetGet_WorksWithVirtualBackend()
    {
        var backend = new InMemoryTerminalBackend();
        Terminal.Initialize(backend);

        Terminal.Cursor.Visible = false;
        Assert.IsFalse(Terminal.Cursor.Visible);

        Terminal.Cursor.Visible = true;
        Assert.IsTrue(Terminal.Cursor.Visible);
    }

    [TestMethod]
    public void Title_SetGet_WorksWithVirtualBackend()
    {
        var backend = new InMemoryTerminalBackend();
        Terminal.Initialize(backend);

        Terminal.Title = "Hello";
        Assert.AreEqual("Hello", Terminal.Title);
    }

    [TestMethod]
    public void ForegroundBackgroundColor_AreSettable()
    {
        var backend = new InMemoryTerminalBackend();
        Terminal.Initialize(backend);

        Terminal.ForegroundColor = ConsoleColor.Red;
        Terminal.BackgroundColor = ConsoleColor.Black;

        Assert.AreEqual((AnsiColor)ConsoleColor.Red, Terminal.ForegroundColor);
        Assert.AreEqual((AnsiColor)ConsoleColor.Black, Terminal.BackgroundColor);
        Assert.IsTrue(backend.GetOutText().Contains("\x1b[", StringComparison.Ordinal));
    }

    [TestMethod]
    public void ForegroundColor_WritesAnsi_WhenOutputRedirectedButAnsiEnabled()
    {
        var ciCaps = new TerminalCapabilities
        {
            AnsiEnabled = true,
            ColorLevel = TerminalColorLevel.Color16,
            SupportsOsc8Links = false,
            SupportsAlternateScreen = false,
            SupportsCursorVisibility = false,
            SupportsMouse = false,
            SupportsBracketedPaste = false,
            SupportsRawMode = false,
            SupportsCursorPositionGet = false,
            SupportsCursorPositionSet = false,
            SupportsTitleGet = false,
            SupportsTitleSet = false,
            SupportsWindowSize = false,
            SupportsWindowSizeSet = false,
            SupportsBufferSize = false,
            SupportsBufferSizeSet = false,
            SupportsBeep = false,
            IsOutputRedirected = true,
            IsInputRedirected = true,
            TerminalName = "CI",
        };

        var backend = new InMemoryTerminalBackend(capabilities: ciCaps);
        Terminal.Initialize(backend);

        Terminal.ForegroundColor = ConsoleColor.Red;

        Assert.IsTrue(backend.GetOutText().Contains("\x1b[", StringComparison.Ordinal));
    }

    [TestMethod]
    public async Task ReadKeyAsync_ReadsFromVirtualEvents_AndKeyAvailablePeeks()
    {
        var backend = new InMemoryTerminalBackend();
        Terminal.Initialize(backend);

        Terminal.StartInput(new TerminalInputOptions { EnableMouseEvents = false, MouseMode = TerminalMouseMode.Off });
        Assert.IsFalse(Terminal.KeyAvailable);

        backend.PushEvent(new TerminalKeyEvent { Key = TerminalKey.Unknown, Char = 'x' });

        Assert.IsTrue(Terminal.KeyAvailable);

        var key = await Terminal.ReadKeyAsync(intercept: true);
        Assert.AreEqual('x', key.KeyChar);
        Assert.AreEqual(TerminalKey.Unknown, key.Key);
    }

    [TestMethod]
    public async Task TreatControlCAsInput_PreventsReadKeyFromThrowingOnSignal()
    {
        var backend = new InMemoryTerminalBackend();
        Terminal.Initialize(backend);

        Terminal.StartInput();
        Terminal.TreatControlCAsInput = true;

        var task = Terminal.ReadKeyAsync(intercept: true).AsTask();

        backend.PushEvent(new TerminalSignalEvent { Kind = TerminalSignalKind.Interrupt });
        backend.PushEvent(new TerminalKeyEvent { Key = TerminalKey.Enter });

        var key = await task;
        Assert.AreEqual(TerminalKey.Enter, key.Key);
    }

    [TestMethod]
    public async Task ReadLineAsync_ThrowsWhenImplicitStartInputDisabled()
    {
        var backend = new InMemoryTerminalBackend();
        Terminal.Initialize(backend, options: new TerminalOptions { ImplicitStartInput = false });

        try
        {
            await Terminal.ReadLineAsync(new TerminalReadLineOptions { Echo = false });
            Assert.Fail("Expected InvalidOperationException");
        }
        catch (InvalidOperationException)
        {
        }
    }

    [TestMethod]
    public void UseAlternateScreen_IsNestable()
    {
        var backend = new InMemoryTerminalBackend();
        Terminal.Initialize(backend);

        using (Terminal.UseAlternateScreen())
        {
            using (Terminal.UseAlternateScreen())
            {
            }
        }

        var text = backend.GetOutText();
        Assert.AreEqual(1, CountOccurrences(text, "\x1b[?1049h"));
        Assert.AreEqual(1, CountOccurrences(text, "\x1b[?1049l"));
    }

    [TestMethod]
    public async Task ReadEventAsync_ReceivesVirtualEvents()
    {
        var backend = new InMemoryTerminalBackend();
        Terminal.Initialize(backend);

        backend.PushEvent(new TerminalResizeEvent { Size = new TerminalSize(100, 40) });

        var ev = await Terminal.ReadEventAsync();
        var resize = (TerminalResizeEvent)ev;
        Assert.AreEqual(new TerminalSize(100, 40), resize.Size);
    }

    [TestMethod]
    public async Task ReadEventsAsync_ConsumesDefaultStream_AndDoesNotReplayOnReadLine()
    {
        var backend = new InMemoryTerminalBackend();
        Terminal.Initialize(backend);
        Terminal.StartInput(new TerminalInputOptions { EnableMouseEvents = false, MouseMode = TerminalMouseMode.Off });

        backend.PushEvent(new TerminalKeyEvent { Key = TerminalKey.Unknown, Char = 'a' });
        backend.PushEvent(new TerminalKeyEvent { Key = TerminalKey.Enter });

        var received = new List<TerminalEvent>();
        await foreach (var ev in Terminal.ReadEventsAsync())
        {
            received.Add(ev);
            if (received.Count == 2)
            {
                break;
            }
        }

        Assert.HasCount(2, received);
        Assert.IsInstanceOfType(received[0], typeof(TerminalKeyEvent));
        Assert.IsInstanceOfType(received[1], typeof(TerminalKeyEvent));

        // No events should be replayed from an internal buffer.
        Assert.IsFalse(Terminal.TryReadEvent(out _));
    }

    [TestMethod]
    public async Task ReadLineAsync_ReadsTextUntilEnter()
    {
        var backend = new InMemoryTerminalBackend();
        Terminal.Initialize(backend);

        var task = Terminal.ReadLineAsync(new TerminalReadLineOptions { Echo = false }).AsTask();

        backend.PushEvent(new TerminalTextEvent { Text = "ab" });
        backend.PushEvent(new TerminalKeyEvent { Key = TerminalKey.Backspace });
        backend.PushEvent(new TerminalTextEvent { Text = "c" });
        backend.PushEvent(new TerminalKeyEvent { Key = TerminalKey.Enter });

        var line = await task;
        Assert.AreEqual("ac", line);
    }

    [TestMethod]
    public async Task ReadLineAsync_PasteWithNewlineCompletes()
    {
        var backend = new InMemoryTerminalBackend();
        Terminal.Initialize(backend);

        var task = Terminal.ReadLineAsync(new TerminalReadLineOptions { Echo = false }).AsTask();

        backend.PushEvent(new TerminalPasteEvent { Text = "hello\nworld" });

        var line = await task;
        Assert.AreEqual("hello", line);
    }

    [TestMethod]
    public async Task ReadLineAsync_CtrlCThrowsWhenEnabled()
    {
        var backend = new InMemoryTerminalBackend();
        Terminal.Initialize(backend);

        var task = Terminal.ReadLineAsync(new TerminalReadLineOptions { Echo = false, CancelOnSignal = true }).AsTask();

        backend.PushEvent(new TerminalSignalEvent { Kind = TerminalSignalKind.Interrupt });

        try
        {
            await task;
            Assert.Fail("Expected OperationCanceledException");
        }
        catch (OperationCanceledException)
        {
        }
    }

    private static int CountOccurrences(string text, string needle)
    {
        var count = 0;
        var index = 0;
        while (true)
        {
            index = text.IndexOf(needle, index, StringComparison.Ordinal);
            if (index < 0)
            {
                return count;
            }

            count++;
            index += needle.Length;
        }
    }
}
