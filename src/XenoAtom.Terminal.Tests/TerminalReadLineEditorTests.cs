// Copyright (c) Alexandre Mutel. All rights reserved.
// Licensed under the BSD-Clause 2 license.
// See license.txt file in the project root for full license information.

using XenoAtom.Terminal.Backends;

namespace XenoAtom.Terminal.Tests;

[TestClass]
public sealed class TerminalReadLineEditorTests
{
    [TestCleanup]
    public void Cleanup()
    {
        Terminal.ResetForTests();
    }

    [TestMethod]
    public async Task ReadLineAsync_AllowsInsertInMiddle_WithLeftRight()
    {
        var backend = new InMemoryTerminalBackend();
        Terminal.Initialize(backend);
        Terminal.StartInput(new TerminalInputOptions { EnableMouseEvents = false, EnableResizeEvents = false });

        var task = Terminal.ReadLineAsync(new TerminalReadLineOptions { Echo = false, EnableEditing = true }).AsTask();

        backend.PushEvent(new TerminalTextEvent { Text = "ac" });
        backend.PushEvent(new TerminalKeyEvent { Key = TerminalKey.Left });
        backend.PushEvent(new TerminalTextEvent { Text = "b" });
        backend.PushEvent(new TerminalKeyEvent { Key = TerminalKey.Enter });

        var line = await task;
        Assert.AreEqual("abc", line);
    }

    [TestMethod]
    public async Task ReadLineAsync_UpDown_NavigatesHistory()
    {
        var backend = new InMemoryTerminalBackend();
        Terminal.Initialize(backend);
        Terminal.StartInput(new TerminalInputOptions { EnableMouseEvents = false, EnableResizeEvents = false });

        var options = new TerminalReadLineOptions { Echo = false, EnableEditing = true, EnableHistory = true };

        var firstTask = Terminal.ReadLineAsync(options).AsTask();
        backend.PushEvent(new TerminalTextEvent { Text = "first" });
        backend.PushEvent(new TerminalKeyEvent { Key = TerminalKey.Enter });
        Assert.AreEqual("first", await firstTask);

        var recallTask = Terminal.ReadLineAsync(options).AsTask();
        backend.PushEvent(new TerminalKeyEvent { Key = TerminalKey.Up });
        backend.PushEvent(new TerminalKeyEvent { Key = TerminalKey.Enter });
        Assert.AreEqual("first", await recallTask);
    }

    [TestMethod]
    public async Task ReadLineAsync_CtrlXCtrlV_CutsAndPastes_InternalClipboard()
    {
        var backend = new InMemoryTerminalBackend();
        Terminal.Initialize(backend);
        Terminal.StartInput(new TerminalInputOptions { EnableMouseEvents = false, EnableResizeEvents = false });

        var task = Terminal.ReadLineAsync(new TerminalReadLineOptions { Echo = false, EnableEditing = true }).AsTask();

        backend.PushEvent(new TerminalTextEvent { Text = "abc" });
        backend.PushEvent(new TerminalKeyEvent { Key = TerminalKey.Unknown, Char = '\x18', Modifiers = TerminalModifiers.Ctrl }); // Ctrl+X
        backend.PushEvent(new TerminalKeyEvent { Key = TerminalKey.Unknown, Char = '\x16', Modifiers = TerminalModifiers.Ctrl }); // Ctrl+V
        backend.PushEvent(new TerminalKeyEvent { Key = TerminalKey.Enter });

        Assert.AreEqual("abc", await task);
    }

    [TestMethod]
    public async Task ReadLineAsync_Tab_CallsCompletionHandler()
    {
        var backend = new InMemoryTerminalBackend();
        Terminal.Initialize(backend);
        Terminal.StartInput(new TerminalInputOptions { EnableMouseEvents = false, EnableResizeEvents = false });

        var options = new TerminalReadLineOptions
        {
            Echo = false,
            EnableEditing = true,
            CompletionHandler = static (text, cursor, selectionStart, selectionLength) => new TerminalReadLineCompletion
            {
                Handled = true,
                InsertText = "lo",
            },
        };

        var task = Terminal.ReadLineAsync(options).AsTask();
        backend.PushEvent(new TerminalTextEvent { Text = "hel" });
        backend.PushEvent(new TerminalKeyEvent { Key = TerminalKey.Tab });
        backend.PushEvent(new TerminalKeyEvent { Key = TerminalKey.Enter });

        Assert.AreEqual("hello", await task);
    }

    [TestMethod]
    public async Task ReadLineAsync_ViewWidth_ShowsEllipsis_WhenEchoEnabled()
    {
        var backend = new InMemoryTerminalBackend(initialSize: new TerminalSize(10, 5));
        Terminal.Initialize(backend);
        Terminal.StartInput(new TerminalInputOptions { EnableMouseEvents = false, EnableResizeEvents = false });

        var options = new TerminalReadLineOptions
        {
            Echo = true,
            EnableEditing = true,
            ViewWidth = 5,
            Ellipsis = "…",
        };

        var task = Terminal.ReadLineAsync(options).AsTask();
        backend.PushEvent(new TerminalTextEvent { Text = "abcdefgh" });
        backend.PushEvent(new TerminalKeyEvent { Key = TerminalKey.Enter });

        Assert.AreEqual("abcdefgh", await task);
        Assert.IsTrue(backend.GetOutText().Contains("…", StringComparison.Ordinal));
    }

    [TestMethod]
    public async Task ReadLineAsync_ShiftLeft_SelectsAndTypingReplacesSelection()
    {
        var backend = new InMemoryTerminalBackend();
        Terminal.Initialize(backend);
        Terminal.StartInput(new TerminalInputOptions { EnableMouseEvents = false, EnableResizeEvents = false });

        var task = Terminal.ReadLineAsync(new TerminalReadLineOptions { Echo = false, EnableEditing = true }).AsTask();

        backend.PushEvent(new TerminalTextEvent { Text = "abcd" });
        backend.PushEvent(new TerminalKeyEvent { Key = TerminalKey.Left, Modifiers = TerminalModifiers.Shift });
        backend.PushEvent(new TerminalTextEvent { Text = "X" });
        backend.PushEvent(new TerminalKeyEvent { Key = TerminalKey.Enter });

        Assert.AreEqual("abcX", await task);
    }

    [TestMethod]
    public async Task ReadLineAsync_CtrlLeft_MovesByWord_WhenModifiersAvailable()
    {
        var backend = new InMemoryTerminalBackend();
        Terminal.Initialize(backend);
        Terminal.StartInput(new TerminalInputOptions { EnableMouseEvents = false, EnableResizeEvents = false });

        var task = Terminal.ReadLineAsync(new TerminalReadLineOptions { Echo = false, EnableEditing = true }).AsTask();

        backend.PushEvent(new TerminalTextEvent { Text = "hello world" });
        backend.PushEvent(new TerminalKeyEvent { Key = TerminalKey.Left, Modifiers = TerminalModifiers.Ctrl });
        backend.PushEvent(new TerminalTextEvent { Text = "X" });
        backend.PushEvent(new TerminalKeyEvent { Key = TerminalKey.Enter });

        Assert.AreEqual("hello Xworld", await task);
    }

    [TestMethod]
    public async Task ReadLineAsync_CtrlShiftLeft_SelectsWordAndBackspaceDeletes()
    {
        var backend = new InMemoryTerminalBackend();
        Terminal.Initialize(backend);
        Terminal.StartInput(new TerminalInputOptions { EnableMouseEvents = false, EnableResizeEvents = false });

        var task = Terminal.ReadLineAsync(new TerminalReadLineOptions { Echo = false, EnableEditing = true }).AsTask();

        backend.PushEvent(new TerminalTextEvent { Text = "hello world" });
        backend.PushEvent(new TerminalKeyEvent { Key = TerminalKey.Left, Modifiers = TerminalModifiers.Ctrl | TerminalModifiers.Shift });
        backend.PushEvent(new TerminalKeyEvent { Key = TerminalKey.Backspace });
        backend.PushEvent(new TerminalKeyEvent { Key = TerminalKey.Enter });

        Assert.AreEqual("hello ", await task);
    }

    [TestMethod]
    public async Task ReadLineAsync_CtrlBackspace_DeletesWordLeft_WhenModifiersAvailable()
    {
        var backend = new InMemoryTerminalBackend();
        Terminal.Initialize(backend);
        Terminal.StartInput(new TerminalInputOptions { EnableMouseEvents = false, EnableResizeEvents = false });

        var task = Terminal.ReadLineAsync(new TerminalReadLineOptions { Echo = false, EnableEditing = true }).AsTask();

        backend.PushEvent(new TerminalTextEvent { Text = "hello world" });
        backend.PushEvent(new TerminalKeyEvent { Key = TerminalKey.Backspace, Modifiers = TerminalModifiers.Ctrl });
        backend.PushEvent(new TerminalKeyEvent { Key = TerminalKey.Enter });

        Assert.AreEqual("hello ", await task);
    }

    [TestMethod]
    public async Task ReadLineAsync_CtrlDelete_DeletesWordRight_WhenModifiersAvailable()
    {
        var backend = new InMemoryTerminalBackend();
        Terminal.Initialize(backend);
        Terminal.StartInput(new TerminalInputOptions { EnableMouseEvents = false, EnableResizeEvents = false });

        var task = Terminal.ReadLineAsync(new TerminalReadLineOptions { Echo = false, EnableEditing = true }).AsTask();

        backend.PushEvent(new TerminalTextEvent { Text = "hello world" });
        backend.PushEvent(new TerminalKeyEvent { Key = TerminalKey.Left, Modifiers = TerminalModifiers.Ctrl });
        backend.PushEvent(new TerminalKeyEvent { Key = TerminalKey.Delete, Modifiers = TerminalModifiers.Ctrl });
        backend.PushEvent(new TerminalKeyEvent { Key = TerminalKey.Enter });

        Assert.AreEqual("hello ", await task);
    }
}
