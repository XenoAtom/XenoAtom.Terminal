// Copyright (c) Alexandre Mutel. All rights reserved.
// Licensed under the BSD-Clause 2 license.
// See license.txt file in the project root for full license information.

using System.Collections.Generic;
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
    public async Task ReadLineAsync_KeyBindings_CanDisableLeftArrow()
    {
        var backend = new InMemoryTerminalBackend();
        Terminal.Initialize(backend);
        Terminal.StartInput(new TerminalInputOptions { EnableMouseEvents = false, EnableResizeEvents = false });

        var bindings = TerminalReadLineKeyBindings.CreateDefault();
        bindings.Bind(TerminalKey.Left, TerminalModifiers.None, TerminalReadLineCommand.Ignore);

        var task = Terminal.ReadLineAsync(new TerminalReadLineOptions { Echo = false, EnableEditing = true, KeyBindings = bindings }).AsTask();

        backend.PushEvent(new TerminalTextEvent { Text = "ac" });
        backend.PushEvent(new TerminalKeyEvent { Key = TerminalKey.Left });
        backend.PushEvent(new TerminalTextEvent { Text = "b" });
        backend.PushEvent(new TerminalKeyEvent { Key = TerminalKey.Enter });

        var line = await task;
        Assert.AreEqual("acb", line);
    }

    [TestMethod]
    public async Task ReadLineAsync_UndoRedo_RestoresPriorStates()
    {
        var backend = new InMemoryTerminalBackend();
        Terminal.Initialize(backend);
        Terminal.StartInput(new TerminalInputOptions { EnableMouseEvents = false, EnableResizeEvents = false });

        var snapshots = new List<string>();
        var options = new TerminalReadLineOptions
        {
            Echo = false,
            EnableEditing = true,
            KeyHandler = (ctl, key) =>
            {
                if (key.Key == TerminalKey.F1)
                {
                    snapshots.Add(ctl.TextString);
                    ctl.Handled = true;
                }
            },
        };

        var task = Terminal.ReadLineAsync(options).AsTask();

        backend.PushEvent(new TerminalTextEvent { Text = "abc" });
        backend.PushEvent(new TerminalKeyEvent { Key = TerminalKey.Backspace });
        backend.PushEvent(new TerminalKeyEvent { Key = TerminalKey.Unknown, Char = '\x1A', Modifiers = TerminalModifiers.Ctrl }); // Ctrl+Z
        backend.PushEvent(new TerminalKeyEvent { Key = TerminalKey.F1 });
        backend.PushEvent(new TerminalKeyEvent { Key = TerminalKey.Unknown, Char = '\x19', Modifiers = TerminalModifiers.Ctrl }); // Ctrl+Y
        backend.PushEvent(new TerminalKeyEvent { Key = TerminalKey.F1 });
        backend.PushEvent(new TerminalKeyEvent { Key = TerminalKey.Enter });

        Assert.AreEqual("ab", await task);
        CollectionAssert.AreEqual(new[] { "abc", "ab" }, snapshots);
    }

    [TestMethod]
    public async Task ReadLineAsync_ReverseSearch_CtrlR_FindsHistory()
    {
        var backend = new InMemoryTerminalBackend();
        Terminal.Initialize(backend);
        Terminal.StartInput(new TerminalInputOptions { EnableMouseEvents = false, EnableResizeEvents = false });

        var options = new TerminalReadLineOptions { Echo = false, EnableEditing = true, EnableHistory = true };
        options.History.Add("first", options.HistoryCapacity);
        options.History.Add("second", options.HistoryCapacity);
        options.History.Add("foo", options.HistoryCapacity);

        var task = Terminal.ReadLineAsync(options).AsTask();
        backend.PushEvent(new TerminalKeyEvent { Key = TerminalKey.Unknown, Char = '\x12', Modifiers = TerminalModifiers.Ctrl }); // Ctrl+R
        backend.PushEvent(new TerminalTextEvent { Text = "se" });
        backend.PushEvent(new TerminalKeyEvent { Key = TerminalKey.Enter }); // accept match
        backend.PushEvent(new TerminalKeyEvent { Key = TerminalKey.Enter }); // accept line

        Assert.AreEqual("second", await task);
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
    public async Task ReadLineAsync_CtrlCCopiesSelection_AndCtrlVPastes()
    {
        var backend = new InMemoryTerminalBackend();
        Terminal.Initialize(backend);
        Terminal.StartInput(new TerminalInputOptions { EnableMouseEvents = false, EnableResizeEvents = false });

        var task = Terminal.ReadLineAsync(new TerminalReadLineOptions { Echo = false, EnableEditing = true }).AsTask();

        backend.PushEvent(new TerminalTextEvent { Text = "abc" });
        backend.PushEvent(new TerminalKeyEvent { Key = TerminalKey.Left, Modifiers = TerminalModifiers.Shift });
        backend.PushEvent(new TerminalKeyEvent { Key = TerminalKey.Left, Modifiers = TerminalModifiers.Shift });
        backend.PushEvent(new TerminalKeyEvent { Key = TerminalKey.Unknown, Char = '\x03', Modifiers = TerminalModifiers.Ctrl }); // Ctrl+C
        backend.PushEvent(new TerminalKeyEvent { Key = TerminalKey.End });
        backend.PushEvent(new TerminalKeyEvent { Key = TerminalKey.Unknown, Char = '\x16', Modifiers = TerminalModifiers.Ctrl }); // Ctrl+V
        backend.PushEvent(new TerminalKeyEvent { Key = TerminalKey.Enter });

        Assert.AreEqual("abcbc", await task);
        Assert.IsTrue(Terminal.Clipboard.TryGetText(out var clip));
        Assert.AreEqual("bc", clip);
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
    public async Task ReadLineAsync_Tab_CyclesCompletionCandidates_UntilOtherAction()
    {
        var backend = new InMemoryTerminalBackend();
        Terminal.Initialize(backend);
        Terminal.StartInput(new TerminalInputOptions { EnableMouseEvents = false, EnableResizeEvents = false });

        var calls = 0;
        var options = new TerminalReadLineOptions
        {
            Echo = false,
            EnableEditing = true,
            CompletionHandler = (text, cursor, selectionStart, selectionLength) =>
            {
                _ = selectionStart;
                _ = selectionLength;
                calls++;

                // Replace the token fragment to the left of the cursor.
                var start = cursor;
                while (start > 0 && !char.IsWhiteSpace(text[start - 1]))
                {
                    start--;
                }

                return new TerminalReadLineCompletion
                {
                    Handled = true,
                    Candidates = ["hello", "help"],
                    ReplaceStart = start,
                    ReplaceLength = cursor - start,
                };
            },
        };

        var task = Terminal.ReadLineAsync(options).AsTask();
        backend.PushEvent(new TerminalTextEvent { Text = "he" });
        backend.PushEvent(new TerminalKeyEvent { Key = TerminalKey.Tab }); // hello
        backend.PushEvent(new TerminalKeyEvent { Key = TerminalKey.Tab }); // help
        backend.PushEvent(new TerminalKeyEvent { Key = TerminalKey.Enter });

        Assert.AreEqual("help", await task);
        Assert.AreEqual(1, calls);
    }

    [TestMethod]
    public async Task ReadLineAsync_Tab_CycleSessionResets_OnTextInput()
    {
        var backend = new InMemoryTerminalBackend();
        Terminal.Initialize(backend);
        Terminal.StartInput(new TerminalInputOptions { EnableMouseEvents = false, EnableResizeEvents = false });

        var calls = 0;
        var options = new TerminalReadLineOptions
        {
            Echo = false,
            EnableEditing = true,
            CompletionHandler = (text, cursor, selectionStart, selectionLength) =>
            {
                _ = text;
                _ = cursor;
                _ = selectionStart;
                _ = selectionLength;
                calls++;

                return calls switch
                {
                    1 => new TerminalReadLineCompletion
                    {
                        Handled = true,
                        Candidates = ["hello", "help"],
                        ReplaceStart = 0,
                        ReplaceLength = 2,
                    },
                    _ => new TerminalReadLineCompletion
                    {
                        Handled = true,
                        InsertText = "Z",
                    },
                };
            },
        };

        var task = Terminal.ReadLineAsync(options).AsTask();
        backend.PushEvent(new TerminalTextEvent { Text = "he" });
        backend.PushEvent(new TerminalKeyEvent { Key = TerminalKey.Tab }); // hello
        backend.PushEvent(new TerminalTextEvent { Text = "!" }); // breaks completion session
        backend.PushEvent(new TerminalKeyEvent { Key = TerminalKey.Tab }); // handler called again -> insert Z
        backend.PushEvent(new TerminalKeyEvent { Key = TerminalKey.Enter });

        Assert.AreEqual("hello!Z", await task);
        Assert.AreEqual(2, calls);
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

    [TestMethod]
    public async Task ReadLineAsync_KeyHandler_CanEditLine_WithController()
    {
        var backend = new InMemoryTerminalBackend();
        Terminal.Initialize(backend);
        Terminal.StartInput(new TerminalInputOptions { EnableMouseEvents = false, EnableResizeEvents = false });

        var options = new TerminalReadLineOptions
        {
            Echo = false,
            EnableEditing = true,
            KeyHandler = static (controller, key) =>
            {
                if (key.Key == TerminalKey.F1)
                {
                    controller.Insert("X".AsSpan());
                }
            },
        };

        var task = Terminal.ReadLineAsync(options).AsTask();

        backend.PushEvent(new TerminalTextEvent { Text = "ab" });
        backend.PushEvent(new TerminalKeyEvent { Key = TerminalKey.Left });
        backend.PushEvent(new TerminalKeyEvent { Key = TerminalKey.F1 });
        backend.PushEvent(new TerminalKeyEvent { Key = TerminalKey.Enter });

        Assert.AreEqual("aXb", await task);
    }

    [TestMethod]
    public async Task ReadLineAsync_MouseEditing_CanSelectAndReplace()
    {
        var backend = new InMemoryTerminalBackend();
        Terminal.Initialize(backend);
        Terminal.StartInput(new TerminalInputOptions { EnableMouseEvents = true, EnableResizeEvents = false, MouseMode = TerminalMouseMode.Drag });

        var options = new TerminalReadLineOptions
        {
            Echo = false,
            EnableEditing = true,
            EnableMouseEditing = true,
        };

        var task = Terminal.ReadLineAsync(options).AsTask();

        backend.PushEvent(new TerminalTextEvent { Text = "abcd" });
        backend.PushEvent(new TerminalMouseEvent { Kind = TerminalMouseKind.Down, Button = TerminalMouseButton.Left, X = 1, Y = 0 });
        backend.PushEvent(new TerminalMouseEvent { Kind = TerminalMouseKind.Drag, Button = TerminalMouseButton.Left, X = 3, Y = 0 });
        backend.PushEvent(new TerminalMouseEvent { Kind = TerminalMouseKind.Up, Button = TerminalMouseButton.Left, X = 3, Y = 0 });
        backend.PushEvent(new TerminalTextEvent { Text = "X" });
        backend.PushEvent(new TerminalKeyEvent { Key = TerminalKey.Enter });

        Assert.AreEqual("aXd", await task);
    }

    [TestMethod]
    public async Task ReadLineAsync_MouseEditing_DoubleClickSelectsWord()
    {
        var backend = new InMemoryTerminalBackend();
        Terminal.Initialize(backend);
        Terminal.StartInput(new TerminalInputOptions { EnableMouseEvents = true, EnableResizeEvents = false, MouseMode = TerminalMouseMode.Drag });

        var options = new TerminalReadLineOptions
        {
            Echo = false,
            EnableEditing = true,
            EnableMouseEditing = true,
        };

        var task = Terminal.ReadLineAsync(options).AsTask();

        backend.PushEvent(new TerminalTextEvent { Text = "hello world" });

        // Double-click within "world" (x=8 => 'r').
        backend.PushEvent(new TerminalMouseEvent { Kind = TerminalMouseKind.Down, Button = TerminalMouseButton.Left, X = 8, Y = 0 });
        backend.PushEvent(new TerminalMouseEvent { Kind = TerminalMouseKind.Up, Button = TerminalMouseButton.Left, X = 8, Y = 0 });
        backend.PushEvent(new TerminalMouseEvent { Kind = TerminalMouseKind.Down, Button = TerminalMouseButton.Left, X = 8, Y = 0 });
        backend.PushEvent(new TerminalMouseEvent { Kind = TerminalMouseKind.Up, Button = TerminalMouseButton.Left, X = 8, Y = 0 });

        backend.PushEvent(new TerminalTextEvent { Text = "X" });
        backend.PushEvent(new TerminalKeyEvent { Key = TerminalKey.Enter });

        Assert.AreEqual("hello X", await task);
    }

    [TestMethod]
    public async Task ReadLineAsync_Render_DoesNotChangeCursorVisibility()
    {
        var backend = new InMemoryTerminalBackend();
        Terminal.Initialize(backend);
        Terminal.Cursor.Visible = false;
        Terminal.StartInput(new TerminalInputOptions { EnableMouseEvents = false, EnableResizeEvents = false });

        var task = Terminal.ReadLineAsync(new TerminalReadLineOptions { Echo = true, EnableEditing = true }).AsTask();
        backend.PushEvent(new TerminalTextEvent { Text = "abc" });
        backend.PushEvent(new TerminalKeyEvent { Key = TerminalKey.Enter });

        Assert.AreEqual("abc", await task);
        Assert.IsFalse(Terminal.Cursor.Visible);
    }
}
