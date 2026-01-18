// Copyright (c) Alexandre Mutel. All rights reserved.
// Licensed under the BSD-Clause 2 license.
// See license.txt file in the project root for full license information.

using System.IO;
using System.Runtime.InteropServices;

namespace XenoAtom.Terminal;

public sealed partial class TerminalInstance
{
    private readonly record struct ReadLineEditSnapshot(string Text, int CursorIndex, int SelectionStart, int SelectionLength);

    private async ValueTask<string?> ReadLineSimpleAsync(TerminalReadLineOptions options, CancellationToken cancellationToken)
    {
        var promptPlain = options.Prompt ?? string.Empty;
        string promptMarkup;
        try
        {
            promptMarkup = options.PromptMarkup?.Invoke() ?? string.Empty;
        }
        catch
        {
            promptMarkup = string.Empty;
        }

        if (options.Echo)
        {
            WritePrompt(promptPlain, promptMarkup);
        }

        var buffer = new List<char>(64);

        while (true)
        {
            cancellationToken.ThrowIfCancellationRequested();

            TerminalEvent ev;
            try
            {
                ev = await ReadEventAsync(cancellationToken).ConfigureAwait(false);
            }
            catch (System.Threading.Channels.ChannelClosedException)
            {
                return null;
            }

            switch (ev)
            {
                case TerminalSignalEvent when options.CancelOnSignal:
                    throw new OperationCanceledException("ReadLine interrupted by terminal signal.");

                case TerminalPasteEvent paste:
                    if (!string.IsNullOrEmpty(paste.Text))
                    {
                        if (AppendText(paste.Text, buffer, options))
                        {
                            AddToHistoryIfEnabled(options, buffer);
                            return new string(CollectionsMarshal.AsSpan(buffer));
                        }
                    }
                    break;

                case TerminalTextEvent text:
                    if (!string.IsNullOrEmpty(text.Text))
                    {
                        if (AppendText(text.Text, buffer, options))
                        {
                            AddToHistoryIfEnabled(options, buffer);
                            return new string(CollectionsMarshal.AsSpan(buffer));
                        }
                    }
                    break;

                case TerminalKeyEvent { Key: TerminalKey.Enter }:
                    if (options.Echo && options.EmitNewLineOnAccept)
                    {
                        WriteLine();
                    }

                    AddToHistoryIfEnabled(options, buffer);
                    return new string(CollectionsMarshal.AsSpan(buffer));

                case TerminalKeyEvent { Key: TerminalKey.Backspace }:
                    if (RemoveLastTextElement(buffer) && options.Echo)
                    {
                        WriteAtomic(static (TextWriter w) => w.Write("\b \b"));
                    }
                    break;

                case TerminalKeyEvent key when key.Modifiers.HasFlag(TerminalModifiers.Ctrl) && key.Char is { } ch:
                    switch (ch)
                    {
                        case TerminalChar.CtrlC: // Ctrl+C
                            throw new OperationCanceledException("ReadLine canceled by Ctrl+C.");
                        case TerminalChar.CtrlV: // Ctrl+V
                            if (Clipboard.TryGetText(out var clipboardText) && !string.IsNullOrEmpty(clipboardText))
                            {
                                if (AppendText(clipboardText, buffer, options))
                                {
                                    AddToHistoryIfEnabled(options, buffer);
                                    return new string(CollectionsMarshal.AsSpan(buffer));
                                }
                            }
                            break;
                        case TerminalChar.CtrlX: // Ctrl+X
                            if (buffer.Count > 0)
                            {
                                var span = CollectionsMarshal.AsSpan(buffer);
                                _readLineKillBuffer = new string(span);
                                Clipboard.TrySetText(span);
                                buffer.Clear();

                                if (options.Echo && string.IsNullOrEmpty(promptMarkup))
                                {
                                    WriteAtomic((TextWriter w) =>
                                    {
                                        w.Write('\r');
                                        if (!string.IsNullOrEmpty(promptPlain))
                                        {
                                            w.Write(promptPlain);
                                        }
                                        for (var i = 0; i < _readLineKillBuffer.Length; i++)
                                        {
                                            w.Write(' ');
                                        }
                                        w.Write('\r');
                                        if (!string.IsNullOrEmpty(promptPlain))
                                        {
                                            w.Write(promptPlain);
                                        }
                                    });
                                }
                            }
                            break;
                    }
                    break;
            }
        }

        bool AppendText(string text, List<char> buffer, TerminalReadLineOptions options)
        {
            var completed = false;
            var max = options.MaxLength;

            for (var i = 0; i < text.Length; i++)
            {
                var ch = text[i];
                if (ch == '\r' || ch == '\n')
                {
                    completed = true;
                    break;
                }

                if (max is { } maxLen && buffer.Count >= maxLen)
                {
                    continue;
                }

                buffer.Add(ch);
            }

            if (options.Echo)
            {
                WriteAtomic((TextWriter w) =>
                {
                    for (var i = 0; i < text.Length; i++)
                    {
                        var ch = text[i];
                        if (ch == '\r' || ch == '\n')
                        {
                            break;
                        }
                        w.Write(ch);
                    }
                });

                if (completed && options.EmitNewLineOnAccept)
                {
                    WriteLine();
                }
            }

            return completed;
        }

        static bool RemoveLastTextElement(List<char> buffer)
        {
            if (buffer.Count == 0)
            {
                return false;
            }

            var span = CollectionsMarshal.AsSpan(buffer);
            var start = TerminalTextUtility.GetPreviousTextElementIndex(span, span.Length);
            start = Math.Clamp(start, 0, buffer.Count - 1);
            buffer.RemoveRange(start, buffer.Count - start);
            return true;
        }

        void WritePrompt(string prompt, string markup)
        {
            if (!string.IsNullOrEmpty(markup))
            {
                lock (_outputLock)
                {
                    _writerUnsafe!.Reset();
                    _markupUnsafe!.Write(markup.AsSpan());
                    _writerUnsafe!.ResetStyle();
                }
            }
            else if (!string.IsNullOrEmpty(prompt))
            {
                Write(prompt.AsSpan());
            }
        }
    }

    private async ValueTask<string?> ReadLineEditorAsync(TerminalReadLineOptions options, CancellationToken cancellationToken)
    {
        var promptPlain = options.Prompt ?? string.Empty;
        string promptMarkup;
        try
        {
            promptMarkup = options.PromptMarkup?.Invoke() ?? string.Empty;
        }
        catch
        {
            promptMarkup = string.Empty;
        }

        var promptCells = !string.IsNullOrEmpty(promptMarkup) ? MeasureStyledWidth(promptMarkup) : TerminalTextUtility.GetWidth(promptPlain.AsSpan());

        var buffer = new List<char>(64);
        var controller = new TerminalReadLineController(buffer, options.MaxLength);
        controller.Activate();

        var keyBindings = options.KeyBindings;

        var historyIndex = -1;
        string? historySnapshot = null;
        var mouseSelecting = false;
        var suppressMouseUpSelectionUpdate = false;

        IReadOnlyList<string>? completionCandidates = null;
        int completionCandidateIndex = -1;
        int completionReplaceStart = -1;
        int completionReplaceLength = 0;

        List<ReadLineEditSnapshot>? undoStack = options.EnableUndoRedo ? new List<ReadLineEditSnapshot>() : null;
        List<ReadLineEditSnapshot>? redoStack = options.EnableUndoRedo ? new List<ReadLineEditSnapshot>() : null;

        var reverseSearchActive = false;
        var reverseSearchQuery = string.Empty;
        var reverseSearchCursor = 0;
        var reverseSearchMatchIndex = -1;
        var reverseSearchMatch = string.Empty;
        ReadLineEditSnapshot reverseSearchSnapshot = default;
        var hasReverseSearchSnapshot = false;

        var origin = GetCursorPosition();
        var windowColumns = Math.Max(1, GetWindowSize().Columns);
        var availableCells = Math.Max(1, options.ViewWidth ?? Math.Max(1, windowColumns - origin.Column - promptCells));

        var baseStyle = _style;

        if (options.Echo)
        {
            Render();
        }

        try
        {
            while (true)
            {
                cancellationToken.ThrowIfCancellationRequested();

                TerminalEvent ev;
                try
                {
                    ev = await ReadEventAsync(cancellationToken).ConfigureAwait(false);
                }
                catch (System.Threading.Channels.ChannelClosedException)
                {
                    return null;
                }

                controller.BeginCallback();
                switch (ev)
                {
                case TerminalResizeEvent resize:
                    windowColumns = Math.Max(1, resize.Size.Columns);
                    availableCells = Math.Max(1, options.ViewWidth ?? Math.Max(1, windowColumns - origin.Column - promptCells));
                    if (options.Echo)
                    {
                        Render();
                    }
                    break;

                case TerminalSignalEvent when options.CancelOnSignal:
                    throw new OperationCanceledException("ReadLine interrupted by terminal signal.");

                case TerminalPasteEvent paste:
                    ResetCompletionSession();
                    if (!string.IsNullOrEmpty(paste.Text))
                    {
                        if (reverseSearchActive)
                        {
                            if (TryAppendReverseSearchText(paste.Text.AsSpan(), out _))
                            {
                                RenderIfEcho();
                            }
                            break;
                        }

                        var before = CaptureUndoIfEnabledForTextInput();
                        if (InsertText(paste.Text.AsSpan(), out var accepted))
                        {
                            if (options.Echo)
                            {
                                controller.SetCursorIndex(buffer.Count, extendSelection: false);
                                Render();
                                if (options.EmitNewLineOnAccept)
                                {
                                    WriteLine();
                                }
                            }

                            AddToHistoryIfEnabled(options, buffer);
                            return accepted;
                        }

                        PushUndoIfChanged(before);
                        RenderIfEcho();
                    }
                    break;

                case TerminalTextEvent text:
                    ResetCompletionSession();
                    if (!string.IsNullOrEmpty(text.Text))
                    {
                        if (reverseSearchActive)
                        {
                            if (TryAppendReverseSearchText(text.Text.AsSpan(), out _))
                            {
                                RenderIfEcho();
                            }
                            break;
                        }

                        var before = CaptureUndoIfEnabledForTextInput();
                        if (InsertText(text.Text.AsSpan(), out var accepted))
                        {
                            if (options.Echo)
                            {
                                controller.SetCursorIndex(buffer.Count, extendSelection: false);
                                Render();
                                if (options.EmitNewLineOnAccept)
                                {
                                    WriteLine();
                                }
                            }

                            AddToHistoryIfEnabled(options, buffer);
                            return accepted;
                        }

                        PushUndoIfChanged(before);
                        RenderIfEcho();
                    }
                    break;

                case TerminalMouseEvent mouse:
                    if (mouse.Kind != TerminalMouseKind.Move)
                    {
                        ResetCompletionSession();
                    }
                    if (options.MouseHandler is { } mouseHandler)
                    {
                        controller.BeginCallback();
                        mouseHandler(controller, mouse);
                        controller.EndCallback();

                        if (controller.CancelRequested)
                        {
                            throw new OperationCanceledException("ReadLine canceled by mouse handler.");
                        }

                        if (controller.TextChanged)
                        {
                            ResetHistoryNavigation();
                        }

                        if (controller.AcceptRequested)
                        {
                            if (options.Echo)
                            {
                                controller.SetCursorIndex(buffer.Count, extendSelection: false);
                                Render();
                                if (options.EmitNewLineOnAccept)
                                {
                                    WriteLine();
                                }
                            }

                            AddToHistoryIfEnabled(options, buffer);
                            return new string(CollectionsMarshal.AsSpan(buffer));
                        }

                        if (controller.Handled)
                        {
                            ResetCompletionSession();
                            RenderIfEcho();
                            break;
                        }
                    }

                    if (options.EnableMouseEditing && HandleEditorMouse(mouse))
                    {
                        ResetCompletionSession();
                        RenderIfEcho();
                    }

                    break;

                case TerminalKeyEvent key:
                    if (key.Char is TerminalChar.CtrlD && buffer.Count == 0)
                    {
                        // Ctrl+D on an empty line behaves like EOF.
                        return null;
                    }

                    var hasCommand = TryResolveKeyBindingCommand(key, out var command);
                    var isCompletionGesture = (hasCommand && command == TerminalReadLineCommand.Complete) || (!hasCommand && key.Key == TerminalKey.Tab);

                    if (!isCompletionGesture)
                    {
                        ResetCompletionSession();
                    }

                    if (reverseSearchActive)
                    {
                        if (HandleReverseSearchKey(key, hasCommand, command))
                        {
                            RenderIfEcho();
                        }
                        break;
                    }

                    var beforeKey = CaptureUndoIfEnabledForKeyEvent(hasCommand, command, key);
                    if (options.KeyHandler is { } handler)
                    {
                        controller.BeginCallback();
                        handler(controller, key);
                        controller.EndCallback();

                        if (controller.CancelRequested)
                        {
                            throw new OperationCanceledException("ReadLine canceled by key handler.");
                        }

                        if (controller.TextChanged)
                        {
                            ResetHistoryNavigation();
                        }

                        if (controller.AcceptRequested)
                        {
                            if (options.Echo)
                            {
                                controller.SetCursorIndex(buffer.Count, extendSelection: false);
                                Render();
                                if (options.EmitNewLineOnAccept)
                                {
                                    WriteLine();
                                }
                            }

                            AddToHistoryIfEnabled(options, buffer);
                            return new string(CollectionsMarshal.AsSpan(buffer));
                        }

                        if (controller.Handled)
                        {
                            ResetCompletionSession();
                            PushUndoIfChanged(beforeKey);
                            RenderIfEcho();
                            break;
                        }
                    }

                    if (hasCommand)
                    {
                        if (command == TerminalReadLineCommand.AcceptLine)
                        {
                            if (options.Echo)
                            {
                                controller.SetCursorIndex(buffer.Count, extendSelection: false);
                                Render();
                                if (options.EmitNewLineOnAccept)
                                {
                                    WriteLine();
                                }
                            }

                            AddToHistoryIfEnabled(options, buffer);
                            return new string(CollectionsMarshal.AsSpan(buffer));
                        }

                        if (command == TerminalReadLineCommand.Cancel)
                        {
                            throw new OperationCanceledException("ReadLine canceled by key gesture.");
                        }

                        if (command == TerminalReadLineCommand.Ignore)
                        {
                            PushUndoIfChanged(beforeKey);
                            break;
                        }

                        if (HandleEditorCommand(key, command))
                        {
                            if (controller.AcceptRequested)
                            {
                                if (options.Echo)
                                {
                                    controller.SetCursorIndex(buffer.Count, extendSelection: false);
                                    Render();
                                    if (options.EmitNewLineOnAccept)
                                    {
                                        WriteLine();
                                    }
                                }

                                AddToHistoryIfEnabled(options, buffer);
                                return new string(CollectionsMarshal.AsSpan(buffer));
                            }

                            PushUndoIfChanged(beforeKey);
                            RenderIfEcho();
                            break;
                        }

                        PushUndoIfChanged(beforeKey);
                        break;
                    }

                    if (key.Key == TerminalKey.Enter)
                    {
                        if (options.Echo)
                        {
                            controller.SetCursorIndex(buffer.Count, extendSelection: false);
                            Render();
                            if (options.EmitNewLineOnAccept)
                            {
                                WriteLine();
                            }
                        }

                        AddToHistoryIfEnabled(options, buffer);
                        return new string(CollectionsMarshal.AsSpan(buffer));
                    }

                    if (HandleEditorKey(key))
                    {
                        if (controller.AcceptRequested)
                        {
                            if (options.Echo)
                            {
                                controller.SetCursorIndex(buffer.Count, extendSelection: false);
                                Render();
                                if (options.EmitNewLineOnAccept)
                                {
                                    WriteLine();
                                }
                            }

                            AddToHistoryIfEnabled(options, buffer);
                            return new string(CollectionsMarshal.AsSpan(buffer));
                        }

                        PushUndoIfChanged(beforeKey);
                        RenderIfEcho();
                        break;
                    }

                    PushUndoIfChanged(beforeKey);
                    break;
            }
            }
        }
        finally
        {
            RestoreStyle();
            controller.Deactivate();
        }

        void RenderIfEcho()
        {
            if (options.Echo)
            {
                Render();
            }
        }

        void RestoreStyle()
        {
            if (!baseStyle.Equals(_style))
            {
                SetStyleCore(baseStyle);
            }
        }

        void ResetHistoryNavigation()
        {
            historyIndex = -1;
            historySnapshot = null;
        }

        void ResetCompletionSession()
        {
            completionCandidates = null;
            completionCandidateIndex = -1;
            completionReplaceStart = -1;
            completionReplaceLength = 0;
        }

        bool TryResolveKeyBindingCommand(TerminalKeyEvent key, out TerminalReadLineCommand command)
        {
            if (keyBindings is null)
            {
                command = TerminalReadLineCommand.None;
                return false;
            }

            return keyBindings.TryGetCommandWithShiftFallback(TerminalKeyGesture.From(key), out command)
                   && command != TerminalReadLineCommand.None;
        }

        ReadLineEditSnapshot? CaptureUndoIfEnabledForTextInput()
        {
            if (undoStack is null || options.UndoCapacity <= 0)
            {
                return null;
            }

            return CaptureSnapshot();
        }

        ReadLineEditSnapshot? CaptureUndoIfEnabledForKeyEvent(bool hasCommand, TerminalReadLineCommand command, TerminalKeyEvent key)
        {
            if (undoStack is null || options.UndoCapacity <= 0)
            {
                return null;
            }

            if (options.KeyHandler is not null)
            {
                return CaptureSnapshot();
            }

            if (hasCommand)
            {
                return command switch
                {
                    TerminalReadLineCommand.DeleteBackward or
                    TerminalReadLineCommand.DeleteForward or
                    TerminalReadLineCommand.DeleteWordBackward or
                    TerminalReadLineCommand.DeleteWordForward or
                    TerminalReadLineCommand.CutSelectionOrAll or
                    TerminalReadLineCommand.Paste or
                    TerminalReadLineCommand.KillToEnd or
                    TerminalReadLineCommand.KillToStart or
                    TerminalReadLineCommand.KillWordLeft or
                    TerminalReadLineCommand.KillWordRight or
                    TerminalReadLineCommand.Complete => CaptureSnapshot(),
                    _ => null,
                };
            }

            if (key.Key is TerminalKey.Backspace or TerminalKey.Delete or TerminalKey.Tab)
            {
                return CaptureSnapshot();
            }

            if (key.Modifiers.HasFlag(TerminalModifiers.Ctrl) && key.Char is >= TerminalChar.CtrlA and <= TerminalChar.CtrlZ)
            {
                return CaptureSnapshot();
            }

            if (key.Modifiers.HasFlag(TerminalModifiers.Alt) && key.Char is not null)
            {
                return CaptureSnapshot();
            }

            return null;
        }

        void PushUndoIfChanged(ReadLineEditSnapshot? before)
        {
            if (undoStack is null || before is null)
            {
                return;
            }

            if (!controller.TextChanged)
            {
                return;
            }

            if (undoStack.Count == options.UndoCapacity)
            {
                undoStack.RemoveAt(0);
            }

            undoStack.Add(before.Value);
            redoStack?.Clear();
        }

        ReadLineEditSnapshot CaptureSnapshot()
            => new ReadLineEditSnapshot(new string(CollectionsMarshal.AsSpan(buffer)), controller.CursorIndex, controller.SelectionStart, controller.SelectionLength);

        void ApplySnapshot(ReadLineEditSnapshot snapshot)
        {
            SetBuffer(snapshot.Text);
            controller.RestoreSelectionState(snapshot.CursorIndex, snapshot.SelectionStart, snapshot.SelectionLength);
        }

        bool Undo()
        {
            if (undoStack is null || undoStack.Count == 0)
            {
                return false;
            }

            redoStack?.Add(CaptureSnapshot());
            var snap = undoStack[^1];
            undoStack.RemoveAt(undoStack.Count - 1);
            ApplySnapshot(snap);
            return true;
        }

        bool Redo()
        {
            if (undoStack is null || redoStack is null || redoStack.Count == 0)
            {
                return false;
            }

            if (undoStack.Count == options.UndoCapacity)
            {
                undoStack.RemoveAt(0);
            }

            undoStack.Add(CaptureSnapshot());
            var snap = redoStack[^1];
            redoStack.RemoveAt(redoStack.Count - 1);
            ApplySnapshot(snap);
            return true;
        }

        bool StartReverseSearch()
        {
            if (!options.EnableReverseSearch || !options.EnableHistory || options.History.Count == 0)
            {
                return false;
            }

            reverseSearchActive = true;
            reverseSearchQuery = string.Empty;
            reverseSearchCursor = 0;
            reverseSearchMatchIndex = -1;
            reverseSearchMatch = string.Empty;
            reverseSearchSnapshot = CaptureSnapshot();
            hasReverseSearchSnapshot = true;
            FindReverseSearchMatch(startIndexInclusive: options.History.Count - 1);
            return true;
        }

        void AcceptReverseSearchMatch()
        {
            reverseSearchActive = false;

            if (string.IsNullOrEmpty(reverseSearchMatch))
            {
                return;
            }

            if (undoStack is not null && hasReverseSearchSnapshot && !string.Equals(reverseSearchSnapshot.Text, reverseSearchMatch, StringComparison.Ordinal))
            {
                if (undoStack.Count == options.UndoCapacity)
                {
                    undoStack.RemoveAt(0);
                }

                undoStack.Add(reverseSearchSnapshot);
                redoStack?.Clear();
            }

            SetBuffer(reverseSearchMatch);
            controller.RestoreSelectionState(buffer.Count, 0, 0);
            ResetHistoryNavigation();
        }

        bool HandleReverseSearchKey(TerminalKeyEvent key, bool hasCommand, TerminalReadLineCommand command)
        {
            if (!reverseSearchActive)
            {
                return false;
            }

            if (hasCommand && command == TerminalReadLineCommand.ReverseSearch)
            {
                var start = reverseSearchMatchIndex >= 0 ? reverseSearchMatchIndex - 1 : options.History.Count - 1;
                FindReverseSearchMatch(start);
                return true;
            }

            if (key.Key == TerminalKey.Escape || (hasCommand && command == TerminalReadLineCommand.Cancel))
            {
                reverseSearchActive = false;
                if (hasReverseSearchSnapshot)
                {
                    ApplySnapshot(reverseSearchSnapshot);
                }
                return true;
            }

            if (key.Key == TerminalKey.Enter || (hasCommand && command == TerminalReadLineCommand.AcceptLine))
            {
                AcceptReverseSearchMatch();
                return true;
            }

                    if (key.Key == TerminalKey.Backspace || (key.Char is '\b' && key.Key == TerminalKey.Unknown))
                    {
                        if (reverseSearchCursor > 0 && reverseSearchQuery.Length > 0)
                        {
                            var prev = TerminalTextUtility.GetPreviousTextElementIndex(reverseSearchQuery.AsSpan(), reverseSearchCursor);
                            reverseSearchQuery = reverseSearchQuery.Remove(prev, reverseSearchCursor - prev);
                            reverseSearchCursor = prev;
                            FindReverseSearchMatch(options.History.Count - 1);
                        }
                        return true;
                    }

            return false;
        }

        bool TryAppendReverseSearchText(ReadOnlySpan<char> text, out bool accept)
        {
            accept = false;
            if (!reverseSearchActive)
            {
                return false;
            }

            var newline = text.IndexOfAny('\r', '\n');
            if (newline >= 0)
            {
                text = text[..newline];
                accept = true;
            }

            if (!text.IsEmpty)
            {
                reverseSearchQuery += text.ToString();
                reverseSearchCursor = reverseSearchQuery.Length;
            }

            FindReverseSearchMatch(options.History.Count - 1);
            if (accept)
            {
                AcceptReverseSearchMatch();
            }

            return true;
        }

        void FindReverseSearchMatch(int startIndexInclusive)
        {
            reverseSearchMatchIndex = -1;
            reverseSearchMatch = string.Empty;

            if (options.History.Count == 0)
            {
                return;
            }

            if (string.IsNullOrEmpty(reverseSearchQuery))
            {
                var last = options.History.GetAt(options.History.Count - 1);
                if (!string.IsNullOrEmpty(last))
                {
                    reverseSearchMatchIndex = options.History.Count - 1;
                    reverseSearchMatch = last;
                }
                return;
            }

            for (var i = Math.Min(startIndexInclusive, options.History.Count - 1); i >= 0; i--)
            {
                var entry = options.History.GetAt(i);
                if (string.IsNullOrEmpty(entry))
                {
                    continue;
                }

                if (entry.IndexOf(reverseSearchQuery, StringComparison.OrdinalIgnoreCase) >= 0)
                {
                    reverseSearchMatchIndex = i;
                    reverseSearchMatch = entry;
                    return;
                }
            }
        }

        void ComputeView(ReadOnlySpan<char> lineSpan, out int contentStartCell, out int contentCells, out int viewStartIndex, out int viewEndIndex, out bool left, out bool right, out int ellipsisCells)
        {
            var totalCells = TerminalTextUtility.GetWidth(lineSpan);
            var cursorCells = TerminalTextUtility.GetWidth(lineSpan[..controller.CursorIndex]);

            var showEllipsis = options.ShowEllipsis && !string.IsNullOrEmpty(options.Ellipsis);
            ellipsisCells = showEllipsis ? TerminalTextUtility.GetWidth(options.Ellipsis.AsSpan()) : 0;

            contentStartCell = 0;
            contentCells = availableCells;

            if (totalCells > availableCells && showEllipsis && ellipsisCells > 0)
            {
                contentStartCell = Math.Max(0, cursorCells - (availableCells / 2));

                for (var i = 0; i < 2; i++)
                {
                    var showLeft = contentStartCell > 0;
                    var remaining = totalCells - contentStartCell;
                    var showRight = remaining > (availableCells - (showLeft ? ellipsisCells : 0));
                    contentCells = availableCells - (showLeft ? ellipsisCells : 0) - (showRight ? ellipsisCells : 0);
                    if (contentCells < 1)
                    {
                        contentCells = 1;
                    }

                    if (!showRight)
                    {
                        contentStartCell = Math.Max(0, totalCells - contentCells);
                    }

                    if (cursorCells < contentStartCell)
                    {
                        contentStartCell = cursorCells;
                    }
                    else if (cursorCells > contentStartCell + contentCells)
                    {
                        contentStartCell = cursorCells - contentCells;
                    }
                }
            }

            TerminalTextUtility.TryGetIndexAtCell(lineSpan, contentStartCell, out viewStartIndex);
            TerminalTextUtility.TryGetIndexAtCell(lineSpan, contentStartCell + contentCells, out viewEndIndex);

            viewStartIndex = Math.Clamp(viewStartIndex, 0, lineSpan.Length);
            viewEndIndex = Math.Clamp(viewEndIndex, viewStartIndex, lineSpan.Length);

            left = showEllipsis && ellipsisCells > 0 && viewStartIndex > 0;
            right = showEllipsis && ellipsisCells > 0 && viewEndIndex < lineSpan.Length;
        }

        bool HandleEditorMouse(TerminalMouseEvent mouse)
        {
            if (mouse.Button != TerminalMouseButton.Left && mouse.Button != TerminalMouseButton.None)
            {
                return false;
            }

            var lineSpan = CollectionsMarshal.AsSpan(buffer);
            ComputeView(lineSpan, out var contentStartCell, out var contentCells, out var viewStartIndex, out var viewEndIndex, out var left, out _, out var ellipsisCells);

            var promptStart = origin.Column + promptCells;
            var textStart = promptStart + (left ? ellipsisCells : 0);

            int index;
            if (mouse.X < promptStart)
            {
                index = 0;
            }
            else if (mouse.X < textStart)
            {
                index = viewStartIndex;
            }
            else if (mouse.X >= textStart + contentCells)
            {
                index = viewEndIndex;
            }
            else
            {
                var inViewCell = mouse.X - textStart;
                var globalCell = contentStartCell + inViewCell;
                TerminalTextUtility.TryGetIndexAtCell(lineSpan, globalCell, out index);
                index = Math.Clamp(index, 0, lineSpan.Length);
            }

            switch (mouse.Kind)
            {
                case TerminalMouseKind.Down when mouse.Button == TerminalMouseButton.Left:
                    if (mouse.Y != origin.Row)
                    {
                        return false;
                    }
                    mouseSelecting = true;
                    suppressMouseUpSelectionUpdate = false;
                    controller.BeginSelection(index);
                    return true;

                case TerminalMouseKind.DoubleClick when mouse.Button == TerminalMouseButton.Left:
                    if (mouse.Y != origin.Row)
                    {
                        return false;
                    }
                    mouseSelecting = true;
                    suppressMouseUpSelectionUpdate = true;
                    SelectWord(lineSpan, index);
                    return true;

                case TerminalMouseKind.Drag:
                case TerminalMouseKind.Move:
                    if (!mouseSelecting)
                    {
                        return false;
                    }
                    controller.SetCursorIndex(index, extendSelection: true);
                    return true;

                case TerminalMouseKind.Up:
                    if (!mouseSelecting)
                    {
                        return false;
                    }
                    mouseSelecting = false;
                    if (suppressMouseUpSelectionUpdate)
                    {
                        suppressMouseUpSelectionUpdate = false;
                        return true;
                    }
                    controller.SetCursorIndex(index, extendSelection: true);
                    return true;
            }

            return false;

            void SelectWord(ReadOnlySpan<char> text, int clickIndex)
            {
                if (text.IsEmpty)
                {
                    return;
                }

                var probe = Math.Clamp(clickIndex, 0, text.Length);
                if (probe == text.Length && probe > 0)
                {
                    probe = TerminalTextUtility.GetPreviousTextElementIndex(text, probe);
                }
                else if (probe < text.Length && char.IsWhiteSpace(text[probe]) && probe > 0)
                {
                    probe = TerminalTextUtility.GetPreviousTextElementIndex(text, probe);
                }

                probe = Math.Clamp(probe, 0, Math.Max(0, text.Length - 1));
                var isWhitespace = char.IsWhiteSpace(text[probe]);

                var start = probe;
                while (start > 0)
                {
                    var prev = TerminalTextUtility.GetPreviousTextElementIndex(text, start);
                    if (char.IsWhiteSpace(text[prev]) != isWhitespace)
                    {
                        break;
                    }
                    start = prev;
                }

                var endExclusive = TerminalTextUtility.GetNextTextElementIndex(text, probe);
                while (endExclusive < text.Length)
                {
                    if (char.IsWhiteSpace(text[endExclusive]) != isWhitespace)
                    {
                        break;
                    }
                    endExclusive = TerminalTextUtility.GetNextTextElementIndex(text, endExclusive);
                }

                controller.Select(start, endExclusive);
            }
        }

        bool HandleEditorKey(TerminalKeyEvent key)
        {
            // Completion (Tab) with cycling candidates.
            if (key.Key == TerminalKey.Tab)
            {
                var reverse = key.Modifiers.HasFlag(TerminalModifiers.Shift);
                if (completionCandidates is { Count: > 0 } && completionReplaceStart >= 0)
                {
                    completionCandidateIndex = reverse
                        ? (completionCandidateIndex - 1 + completionCandidates.Count) % completionCandidates.Count
                        : (completionCandidateIndex + 1) % completionCandidates.Count;

                    ApplyCompletionCandidate(completionCandidates[completionCandidateIndex]);
                    return true;
                }

                if (options.CompletionHandler is { } completion)
                {
                    var span = CollectionsMarshal.AsSpan(buffer);
                    var result = completion(span, controller.CursorIndex, controller.SelectionStart, controller.SelectionLength);
                    if (!result.Handled)
                    {
                        return false;
                    }

                    if (result.Candidates is { Count: > 0 } candidates)
                    {
                        var replaceStart = result.ReplaceStart;
                        var replaceLength = result.ReplaceLength;
                        if (replaceStart is null || replaceLength is null)
                        {
                            if (controller.SelectionLength > 0)
                            {
                                replaceStart = controller.SelectionStart;
                                replaceLength = controller.SelectionLength;
                            }
                            else
                            {
                                var start = MoveWordLeft(span, controller.CursorIndex);
                                replaceStart = start;
                                replaceLength = controller.CursorIndex - start;
                            }
                        }

                        completionCandidates = candidates;
                        completionCandidateIndex = 0;
                        completionReplaceStart = Math.Clamp(replaceStart.Value, 0, buffer.Count);
                        completionReplaceLength = Math.Clamp(replaceLength.Value, 0, buffer.Count - completionReplaceStart);

                        ApplyCompletionCandidate(candidates[0]);
                        ResetHistoryNavigation();
                        return true;
                    }

                    if (result.ReplaceText is not null)
                    {
                        SetBuffer(result.ReplaceText);
                        controller.SetCursorIndex(Math.Clamp(result.CursorIndex ?? buffer.Count, 0, buffer.Count), extendSelection: false);
                    }
                    else if (!string.IsNullOrEmpty(result.InsertText))
                    {
                        controller.Insert(result.InsertText.AsSpan());
                        controller.SetCursorIndex(Math.Clamp(result.CursorIndex ?? controller.CursorIndex, 0, buffer.Count), extendSelection: false);
                    }

                    ResetHistoryNavigation();
                    return true;
                }
            }

            // Selection and word navigation (when modifiers are available for special keys).
            if (key.Key is TerminalKey.Left or TerminalKey.Right)
            {
                var ctrl = key.Modifiers.HasFlag(TerminalModifiers.Ctrl);
                var alt = key.Modifiers.HasFlag(TerminalModifiers.Alt);
                var shift = key.Modifiers.HasFlag(TerminalModifiers.Shift);
                var wordModifier = ctrl || alt;

                if (wordModifier && shift)
                {
                    var newIndex = key.Key == TerminalKey.Left
                        ? MoveWordLeft(CollectionsMarshal.AsSpan(buffer), controller.CursorIndex)
                        : MoveWordRight(CollectionsMarshal.AsSpan(buffer), controller.CursorIndex);
                    controller.SetCursorIndex(newIndex, extendSelection: true);
                    return true;
                }

                if (wordModifier)
                {
                    var newIndex = key.Key == TerminalKey.Left
                        ? MoveWordLeft(CollectionsMarshal.AsSpan(buffer), controller.CursorIndex)
                        : MoveWordRight(CollectionsMarshal.AsSpan(buffer), controller.CursorIndex);
                    controller.SetCursorIndex(newIndex, extendSelection: false);
                    return true;
                }

                if (shift)
                {
                    var newIndex = key.Key == TerminalKey.Left
                        ? TerminalTextUtility.GetPreviousTextElementIndex(CollectionsMarshal.AsSpan(buffer), controller.CursorIndex)
                        : TerminalTextUtility.GetNextTextElementIndex(CollectionsMarshal.AsSpan(buffer), controller.CursorIndex);
                    controller.SetCursorIndex(newIndex, extendSelection: true);
                    return true;
                }
            }

            switch (key.Key)
            {
                case TerminalKey.Left:
                    controller.SetCursorIndex(TerminalTextUtility.GetPreviousTextElementIndex(CollectionsMarshal.AsSpan(buffer), controller.CursorIndex), extendSelection: false);
                    return true;
                case TerminalKey.Right:
                    controller.SetCursorIndex(TerminalTextUtility.GetNextTextElementIndex(CollectionsMarshal.AsSpan(buffer), controller.CursorIndex), extendSelection: false);
                    return true;
                case TerminalKey.Home:
                    controller.SetCursorIndex(0, extendSelection: key.Modifiers.HasFlag(TerminalModifiers.Shift));
                    return true;
                case TerminalKey.End:
                    controller.SetCursorIndex(buffer.Count, extendSelection: key.Modifiers.HasFlag(TerminalModifiers.Shift));
                    return true;
                case TerminalKey.Backspace:
                    if (controller.HasSelection)
                    {
                        controller.DeleteSelection();
                        ResetHistoryNavigation();
                        return true;
                    }
                    if ((key.Modifiers & (TerminalModifiers.Ctrl | TerminalModifiers.Alt)) != 0 && controller.CursorIndex > 0)
                    {
                        var span = CollectionsMarshal.AsSpan(buffer);
                        var end = controller.CursorIndex;
                        var start = MoveWordLeft(span, end);
                        if (start != end)
                        {
                            controller.Remove(start, end - start);
                            controller.SetCursorIndex(start, extendSelection: false);
                            ResetHistoryNavigation();
                            return true;
                        }
                        return false;
                    }
                    if (controller.CursorIndex > 0)
                    {
                        var span = CollectionsMarshal.AsSpan(buffer);
                        var end = controller.CursorIndex;
                        var prev = TerminalTextUtility.GetPreviousTextElementIndex(span, end);
                        controller.Remove(prev, end - prev);
                        controller.SetCursorIndex(prev, extendSelection: false);
                        ResetHistoryNavigation();
                        return true;
                    }
                    return false;
                case TerminalKey.Delete:
                    if (controller.HasSelection)
                    {
                        controller.DeleteSelection();
                        ResetHistoryNavigation();
                        return true;
                    }
                    if ((key.Modifiers & (TerminalModifiers.Ctrl | TerminalModifiers.Alt)) != 0 && controller.CursorIndex < buffer.Count)
                    {
                        var span = CollectionsMarshal.AsSpan(buffer);
                        var start = controller.CursorIndex;
                        var end = MoveWordRight(span, start);
                        if (end != start)
                        {
                            controller.Remove(start, end - start);
                            ResetHistoryNavigation();
                            return true;
                        }
                        return false;
                    }
                    if (controller.CursorIndex < buffer.Count)
                    {
                        var span = CollectionsMarshal.AsSpan(buffer);
                        var start = controller.CursorIndex;
                        var next = TerminalTextUtility.GetNextTextElementIndex(span, start);
                        controller.Remove(start, next - start);
                        ResetHistoryNavigation();
                        return true;
                    }
                    return false;
                case TerminalKey.Up:
                    controller.ClearSelection();
                    return options.EnableHistory && NavigateHistory(-1);
                case TerminalKey.Down:
                    controller.ClearSelection();
                    return options.EnableHistory && NavigateHistory(+1);
            }

            // Ctrl-modified behavior.
            if (key.Char is { } ch && (key.Modifiers.HasFlag(TerminalModifiers.Ctrl) || (ch is >= TerminalChar.CtrlA and <= TerminalChar.CtrlZ)))
            {
                return HandleCtrlKey(ch);
            }

            // Readline-style Alt bindings (common on Unix/macOS terminals).
            if (key.Modifiers.HasFlag(TerminalModifiers.Alt) && key.Char is { } altChar)
            {
                var lower = char.ToLowerInvariant(altChar);
                var extend = key.Modifiers.HasFlag(TerminalModifiers.Shift);

                switch (lower)
                {
                    case 'b': // Alt+B: word left
                        controller.SetCursorIndex(MoveWordLeft(CollectionsMarshal.AsSpan(buffer), controller.CursorIndex), extendSelection: extend);
                        return true;
                    case 'f': // Alt+F: word right
                        controller.SetCursorIndex(MoveWordRight(CollectionsMarshal.AsSpan(buffer), controller.CursorIndex), extendSelection: extend);
                        return true;
                    case 'd': // Alt+D: delete word right
                        controller.ClearSelection();
                            if (controller.CursorIndex < buffer.Count)
                            {
                                var start = controller.CursorIndex;
                                var end = MoveWordRight(CollectionsMarshal.AsSpan(buffer), start);
                                _readLineKillBuffer = new string(CollectionsMarshal.AsSpan(buffer).Slice(start, end - start));
                                controller.Remove(start, end - start);
                                ResetHistoryNavigation();
                                return true;
                            }
                        return false;
                }
            }

            return false;

            void ApplyCompletionCandidate(string candidate)
            {
                var start = completionReplaceStart;
                var length = completionReplaceLength;

                start = Math.Clamp(start, 0, buffer.Count);
                length = Math.Clamp(length, 0, buffer.Count - start);

                controller.Select(start, start + length);
                controller.Insert(candidate.AsSpan());

                completionReplaceStart = start;
                completionReplaceLength = candidate.Length;
            }
        }

        bool HandleEditorCommand(TerminalKeyEvent key, TerminalReadLineCommand command)
        {
            switch (command)
            {
                case TerminalReadLineCommand.Complete:
                    return HandleEditorKey(new TerminalKeyEvent { Key = TerminalKey.Tab, Char = '\t', Modifiers = key.Modifiers });

                case TerminalReadLineCommand.CursorLeft:
                    controller.SetCursorIndex(TerminalTextUtility.GetPreviousTextElementIndex(CollectionsMarshal.AsSpan(buffer), controller.CursorIndex), extendSelection: key.Modifiers.HasFlag(TerminalModifiers.Shift));
                    return true;
                case TerminalReadLineCommand.CursorRight:
                    controller.SetCursorIndex(TerminalTextUtility.GetNextTextElementIndex(CollectionsMarshal.AsSpan(buffer), controller.CursorIndex), extendSelection: key.Modifiers.HasFlag(TerminalModifiers.Shift));
                    return true;
                case TerminalReadLineCommand.CursorHome:
                    controller.SetCursorIndex(0, extendSelection: key.Modifiers.HasFlag(TerminalModifiers.Shift));
                    return true;
                case TerminalReadLineCommand.CursorEnd:
                    controller.SetCursorIndex(buffer.Count, extendSelection: key.Modifiers.HasFlag(TerminalModifiers.Shift));
                    return true;
                case TerminalReadLineCommand.CursorWordLeft:
                    controller.SetCursorIndex(MoveWordLeft(CollectionsMarshal.AsSpan(buffer), controller.CursorIndex), extendSelection: key.Modifiers.HasFlag(TerminalModifiers.Shift));
                    return true;
                case TerminalReadLineCommand.CursorWordRight:
                    controller.SetCursorIndex(MoveWordRight(CollectionsMarshal.AsSpan(buffer), controller.CursorIndex), extendSelection: key.Modifiers.HasFlag(TerminalModifiers.Shift));
                    return true;

                case TerminalReadLineCommand.ClearSelection:
                    controller.ClearSelection();
                    return true;

                case TerminalReadLineCommand.HistoryPrevious:
                    controller.ClearSelection();
                    return options.EnableHistory && NavigateHistory(-1);
                case TerminalReadLineCommand.HistoryNext:
                    controller.ClearSelection();
                    return options.EnableHistory && NavigateHistory(+1);

                case TerminalReadLineCommand.DeleteBackward:
                    if (controller.HasSelection)
                    {
                        controller.DeleteSelection();
                        ResetHistoryNavigation();
                        return true;
                    }
                    if (controller.CursorIndex > 0)
                    {
                        var span = CollectionsMarshal.AsSpan(buffer);
                        var end = controller.CursorIndex;
                        var prev = TerminalTextUtility.GetPreviousTextElementIndex(span, end);
                        controller.Remove(prev, end - prev);
                        controller.SetCursorIndex(prev, extendSelection: false);
                        ResetHistoryNavigation();
                        return true;
                    }
                    return false;

                case TerminalReadLineCommand.DeleteForward:
                    if (controller.HasSelection)
                    {
                        controller.DeleteSelection();
                        ResetHistoryNavigation();
                        return true;
                    }
                    if (controller.CursorIndex < buffer.Count)
                    {
                        var span = CollectionsMarshal.AsSpan(buffer);
                        var start = controller.CursorIndex;
                        var next = TerminalTextUtility.GetNextTextElementIndex(span, start);
                        controller.Remove(start, next - start);
                        ResetHistoryNavigation();
                        return true;
                    }
                    return false;

                case TerminalReadLineCommand.DeleteWordBackward:
                    if (controller.HasSelection)
                    {
                        controller.DeleteSelection();
                        ResetHistoryNavigation();
                        return true;
                    }
                    if (controller.CursorIndex > 0)
                    {
                        var span = CollectionsMarshal.AsSpan(buffer);
                        var end = controller.CursorIndex;
                        var start = MoveWordLeft(span, end);
                        if (start != end)
                        {
                            controller.Remove(start, end - start);
                            controller.SetCursorIndex(start, extendSelection: false);
                            ResetHistoryNavigation();
                            return true;
                        }
                    }
                    return false;

                case TerminalReadLineCommand.DeleteWordForward:
                    if (controller.HasSelection)
                    {
                        controller.DeleteSelection();
                        ResetHistoryNavigation();
                        return true;
                    }
                    if (controller.CursorIndex < buffer.Count)
                    {
                        var span = CollectionsMarshal.AsSpan(buffer);
                        var start = controller.CursorIndex;
                        var end = MoveWordRight(span, start);
                        if (end != start)
                        {
                            controller.Remove(start, end - start);
                            ResetHistoryNavigation();
                            return true;
                        }
                    }
                    return false;

                case TerminalReadLineCommand.CopySelection:
                    if (controller.HasSelection)
                    {
                        var selection = CollectionsMarshal.AsSpan(buffer).Slice(controller.SelectionStart, controller.SelectionLength);
                        _readLineKillBuffer = selection.Length == 0 ? string.Empty : new string(selection);
                        Clipboard.TrySetText(selection);
                        return true;
                    }
                    return false;

                case TerminalReadLineCommand.CopySelectionOrCancel:
                    if (controller.HasSelection)
                    {
                        var selection = CollectionsMarshal.AsSpan(buffer).Slice(controller.SelectionStart, controller.SelectionLength);
                        _readLineKillBuffer = selection.Length == 0 ? string.Empty : new string(selection);
                        Clipboard.TrySetText(selection);
                        return true;
                    }
                    throw new OperationCanceledException("ReadLine canceled by Ctrl+C.");

                case TerminalReadLineCommand.CutSelectionOrAll:
                    if (buffer.Count > 0)
                    {
                        ReadOnlySpan<char> cutSpan;
                        if (controller.HasSelection)
                        {
                            cutSpan = CollectionsMarshal.AsSpan(buffer).Slice(controller.SelectionStart, controller.SelectionLength);
                            controller.DeleteSelection();
                        }
                        else
                        {
                            cutSpan = CollectionsMarshal.AsSpan(buffer);
                            controller.Clear();
                        }
                        _readLineKillBuffer = cutSpan.Length == 0 ? string.Empty : new string(cutSpan);
                        Clipboard.TrySetText(cutSpan);
                        ResetHistoryNavigation();
                        return true;
                    }
                    return false;

                case TerminalReadLineCommand.Paste:
                    if (Clipboard.TryGetText(out var clipboardText) && !string.IsNullOrEmpty(clipboardText))
                    {
                        if (InsertText(clipboardText.AsSpan(), out _))
                        {
                            controller.Accept();
                        }
                        ResetHistoryNavigation();
                        return true;
                    }

                    if (!string.IsNullOrEmpty(_readLineKillBuffer))
                    {
                        if (InsertText(_readLineKillBuffer.AsSpan(), out _))
                        {
                            controller.Accept();
                        }
                        ResetHistoryNavigation();
                        return true;
                    }

                    return false;

                case TerminalReadLineCommand.KillToEnd:
                    controller.ClearSelection();
                    if (controller.CursorIndex < buffer.Count)
                    {
                        var start = controller.CursorIndex;
                        _readLineKillBuffer = new string(CollectionsMarshal.AsSpan(buffer)[start..]);
                        controller.Remove(start, buffer.Count - start);
                        ResetHistoryNavigation();
                        return true;
                    }
                    return false;

                case TerminalReadLineCommand.KillToStart:
                    controller.ClearSelection();
                    if (controller.CursorIndex > 0)
                    {
                        var end = controller.CursorIndex;
                        _readLineKillBuffer = new string(CollectionsMarshal.AsSpan(buffer)[..end]);
                        controller.Remove(0, end);
                        controller.SetCursorIndex(0, extendSelection: false);
                        ResetHistoryNavigation();
                        return true;
                    }
                    return false;

                case TerminalReadLineCommand.KillWordLeft:
                    controller.ClearSelection();
                    if (controller.CursorIndex > 0)
                    {
                        var end = controller.CursorIndex;
                        var start = MoveWordLeft(CollectionsMarshal.AsSpan(buffer), end);
                        _readLineKillBuffer = new string(CollectionsMarshal.AsSpan(buffer).Slice(start, end - start));
                        controller.Remove(start, end - start);
                        controller.SetCursorIndex(start, extendSelection: false);
                        ResetHistoryNavigation();
                        return true;
                    }
                    return false;

                case TerminalReadLineCommand.KillWordRight:
                    controller.ClearSelection();
                    if (controller.CursorIndex < buffer.Count)
                    {
                        var start = controller.CursorIndex;
                        var end = MoveWordRight(CollectionsMarshal.AsSpan(buffer), start);
                        _readLineKillBuffer = new string(CollectionsMarshal.AsSpan(buffer).Slice(start, end - start));
                        controller.Remove(start, end - start);
                        ResetHistoryNavigation();
                        return true;
                    }
                    return false;

                case TerminalReadLineCommand.Undo:
                    return Undo();
                case TerminalReadLineCommand.Redo:
                    return Redo();

                case TerminalReadLineCommand.ReverseSearch:
                    return StartReverseSearch();

                case TerminalReadLineCommand.ClearScreen:
                    Clear(TerminalClearKind.Screen);
                    return true;
            }

            return false;
        }

        bool HandleCtrlKey(char ch)
        {
            switch (ch)
            {
                case TerminalChar.CtrlA: // Ctrl+A
                    controller.SetCursorIndex(0, extendSelection: false);
                    return true;
                case TerminalChar.CtrlE: // Ctrl+E
                    controller.SetCursorIndex(buffer.Count, extendSelection: false);
                    return true;
                case TerminalChar.CtrlB: // Ctrl+B
                    controller.SetCursorIndex(TerminalTextUtility.GetPreviousTextElementIndex(CollectionsMarshal.AsSpan(buffer), controller.CursorIndex), extendSelection: false);
                    return true;
                case TerminalChar.CtrlF: // Ctrl+F
                    controller.SetCursorIndex(TerminalTextUtility.GetNextTextElementIndex(CollectionsMarshal.AsSpan(buffer), controller.CursorIndex), extendSelection: false);
                    return true;
                case TerminalChar.CtrlP: // Ctrl+P
                    return options.EnableHistory && NavigateHistory(-1);
                case TerminalChar.CtrlN: // Ctrl+N
                    return options.EnableHistory && NavigateHistory(+1);
                case TerminalChar.CtrlK: // Ctrl+K
                    controller.ClearSelection();
                    if (controller.CursorIndex < buffer.Count)
                    {
                        var start = controller.CursorIndex;
                        _readLineKillBuffer = new string(CollectionsMarshal.AsSpan(buffer)[start..]);
                        controller.Remove(start, buffer.Count - start);
                        ResetHistoryNavigation();
                        return true;
                    }
                    return false;
                case TerminalChar.CtrlU: // Ctrl+U
                    controller.ClearSelection();
                    if (controller.CursorIndex > 0)
                    {
                        var end = controller.CursorIndex;
                        _readLineKillBuffer = new string(CollectionsMarshal.AsSpan(buffer)[..end]);
                        controller.Remove(0, end);
                        controller.SetCursorIndex(0, extendSelection: false);
                        ResetHistoryNavigation();
                        return true;
                    }
                    return false;
                case TerminalChar.CtrlW: // Ctrl+W
                    controller.ClearSelection();
                    if (controller.CursorIndex > 0)
                    {
                        var end = controller.CursorIndex;
                        var start = MoveWordLeft(CollectionsMarshal.AsSpan(buffer), end);
                        _readLineKillBuffer = new string(CollectionsMarshal.AsSpan(buffer).Slice(start, end - start));
                        controller.Remove(start, end - start);
                        controller.SetCursorIndex(start, extendSelection: false);
                        ResetHistoryNavigation();
                        return true;
                    }
                    return false;
                case TerminalChar.CtrlX: // Ctrl+X
                    if (buffer.Count > 0)
                    {
                        ReadOnlySpan<char> cutSpan;
                        if (controller.HasSelection)
                        {
                            cutSpan = CollectionsMarshal.AsSpan(buffer).Slice(controller.SelectionStart, controller.SelectionLength);
                            controller.DeleteSelection();
                        }
                        else
                        {
                            cutSpan = CollectionsMarshal.AsSpan(buffer);
                            controller.Clear();
                        }
                        _readLineKillBuffer = cutSpan.Length == 0 ? string.Empty : new string(cutSpan);
                        Clipboard.TrySetText(cutSpan);
                        ResetHistoryNavigation();
                        return true;
                    }
                    return false;
                case TerminalChar.CtrlV: // Ctrl+V
                    if (Clipboard.TryGetText(out var clipboardText) && !string.IsNullOrEmpty(clipboardText))
                    {
                        if (InsertText(clipboardText.AsSpan(), out _))
                        {
                            controller.Accept();
                        }
                        return true;
                    }

                    if (!string.IsNullOrEmpty(_readLineKillBuffer))
                    {
                        if (InsertText(_readLineKillBuffer.AsSpan(), out _))
                        {
                            controller.Accept();
                        }
                        return true;
                    }

                    return false;
                case TerminalChar.CtrlC: // Ctrl+C
                    if (controller.HasSelection)
                    {
                        var selection = CollectionsMarshal.AsSpan(buffer).Slice(controller.SelectionStart, controller.SelectionLength);
                        _readLineKillBuffer = selection.Length == 0 ? string.Empty : new string(selection);
                        Clipboard.TrySetText(selection);
                        return true;
                    }
                    throw new OperationCanceledException("ReadLine canceled by Ctrl+C.");
                case TerminalChar.CtrlL: // Ctrl+L
                    Clear(TerminalClearKind.Screen);
                    RenderIfEcho();
                    return true;
            }

            return false;
        }

        static int MoveWordLeft(ReadOnlySpan<char> text, int index)
        {
            if (index <= 0)
            {
                return 0;
            }

            var i = index;
            while (i > 0)
            {
                var prev = TerminalTextUtility.GetPreviousTextElementIndex(text, i);
                if (!char.IsWhiteSpace(text[prev]))
                {
                    break;
                }
                i = prev;
            }

            while (i > 0)
            {
                var prev = TerminalTextUtility.GetPreviousTextElementIndex(text, i);
                if (char.IsWhiteSpace(text[prev]))
                {
                    break;
                }
                i = prev;
            }

            return i;
        }

        static int MoveWordRight(ReadOnlySpan<char> text, int index)
        {
            if (index >= text.Length)
            {
                return text.Length;
            }

            var i = index;
            while (i < text.Length)
            {
                var next = TerminalTextUtility.GetNextTextElementIndex(text, i);
                if (!char.IsWhiteSpace(text[i]))
                {
                    break;
                }
                i = next;
            }

            while (i < text.Length)
            {
                var next = TerminalTextUtility.GetNextTextElementIndex(text, i);
                if (char.IsWhiteSpace(text[i]))
                {
                    break;
                }
                i = next;
            }

            return i;
        }

        bool NavigateHistory(int delta)
        {
            var count = options.History.Count;
            if (count == 0)
            {
                return false;
            }

            if (historyIndex < 0)
            {
                historySnapshot = new string(CollectionsMarshal.AsSpan(buffer));
                historyIndex = count;
            }

            var newIndex = Math.Clamp(historyIndex + delta, 0, count);
            if (newIndex == historyIndex)
            {
                return false;
            }

            historyIndex = newIndex;
            var newText = historyIndex == count ? historySnapshot ?? string.Empty : (options.History.GetAt(historyIndex) ?? string.Empty);
            SetBuffer(newText);
            controller.SetCursorIndex(buffer.Count, extendSelection: false);
            return true;
        }

        void SetBuffer(string text)
        {
            buffer.Clear();
            if (string.IsNullOrEmpty(text))
            {
                return;
            }

            CollectionsMarshal.SetCount(buffer, text.Length);
            text.AsSpan().CopyTo(CollectionsMarshal.AsSpan(buffer));
        }

        bool InsertText(ReadOnlySpan<char> text, out string acceptedLine)
        {
            acceptedLine = string.Empty;

            var newlineIndex = text.IndexOfAny('\r', '\n');
            if (newlineIndex >= 0)
            {
                if (newlineIndex > 0)
                {
                    controller.Insert(text[..newlineIndex]);
                    ResetHistoryNavigation();
                }

                acceptedLine = new string(CollectionsMarshal.AsSpan(buffer));
                return true;
            }

            controller.Insert(text);
            ResetHistoryNavigation();
            return false;
        }

        void Render()
        {
            lock (_outputLock)
            {
                var restoreCursorVisible = false;
                if (_backend!.TryGetCursorVisible(out var cursorVisible) && cursorVisible)
                {
                    _backend.SetCursorVisible(false);
                    restoreCursorVisible = true;
                }

                try
                {
                    _backend.SetCursorPosition(origin);

                    _writerUnsafe!.Reset();

                    if (reverseSearchActive)
                    {
                        const string prefix = "(reverse-i-search) '";
                        const string separator = "': ";

                        _writerUnsafe.Foreground(ConsoleColor.DarkGray);
                        _writerUnsafe.Write(prefix);
                        _writerUnsafe.ResetStyle();

                        if (!string.IsNullOrEmpty(reverseSearchQuery))
                        {
                            _writerUnsafe.Write(reverseSearchQuery);
                        }

                        _writerUnsafe.Foreground(ConsoleColor.DarkGray);
                        _writerUnsafe.Write(separator);
                        _writerUnsafe.ResetStyle();

                        if (!string.IsNullOrEmpty(reverseSearchMatch))
                        {
                            _writerUnsafe.Write(reverseSearchMatch);
                        }
                        else
                        {
                            _writerUnsafe.Foreground(ConsoleColor.DarkGray);
                            _writerUnsafe.Write("no match");
                            _writerUnsafe.ResetStyle();
                        }

                        _writerUnsafe.EraseLine(0);

                        var searchCursorColumn = origin.Column
                                                 + TerminalTextUtility.GetWidth(prefix.AsSpan())
                                                 + TerminalTextUtility.GetWidth(reverseSearchQuery.AsSpan());
                        _backend.SetCursorPosition(new TerminalPosition(searchCursorColumn, origin.Row));
                        return;
                    }

                    if (!string.IsNullOrEmpty(promptMarkup))
                    {
                        _markupUnsafe!.Write(promptMarkup.AsSpan());
                        _writerUnsafe!.ResetStyle();
                    }
                    else if (!string.IsNullOrEmpty(promptPlain))
                    {
                        _writerUnsafe.Write(promptPlain);
                    }

                    var lineSpan = CollectionsMarshal.AsSpan(buffer);
                    var totalCells = TerminalTextUtility.GetWidth(lineSpan);
                    var cursorCells = TerminalTextUtility.GetWidth(lineSpan[..controller.CursorIndex]);

                    var showEllipsis = options.ShowEllipsis && !string.IsNullOrEmpty(options.Ellipsis);
                    var ellipsisCells = showEllipsis ? TerminalTextUtility.GetWidth(options.Ellipsis.AsSpan()) : 0;

                    var contentStartCell = 0;
                    var contentCells = availableCells;

                    if (totalCells > availableCells && showEllipsis && ellipsisCells > 0)
                    {
                        contentStartCell = Math.Max(0, cursorCells - (availableCells / 2));

                        for (var i = 0; i < 2; i++)
                        {
                            var showLeft = contentStartCell > 0;
                            var remaining = totalCells - contentStartCell;
                            var showRight = remaining > (availableCells - (showLeft ? ellipsisCells : 0));
                            contentCells = availableCells - (showLeft ? ellipsisCells : 0) - (showRight ? ellipsisCells : 0);
                            if (contentCells < 1)
                            {
                                contentCells = 1;
                            }

                            if (!showRight)
                            {
                                contentStartCell = Math.Max(0, totalCells - contentCells);
                            }

                            if (cursorCells < contentStartCell)
                            {
                                contentStartCell = cursorCells;
                            }
                            else if (cursorCells > contentStartCell + contentCells)
                            {
                                contentStartCell = cursorCells - contentCells;
                            }
                        }
                    }

                    TerminalTextUtility.TryGetIndexAtCell(lineSpan, contentStartCell, out var viewStartIndex);
                    TerminalTextUtility.TryGetIndexAtCell(lineSpan, contentStartCell + contentCells, out var viewEndIndex);

                    viewStartIndex = Math.Clamp(viewStartIndex, 0, lineSpan.Length);
                    viewEndIndex = Math.Clamp(viewEndIndex, viewStartIndex, lineSpan.Length);

                    var left = showEllipsis && ellipsisCells > 0 && viewStartIndex > 0;
                    var right = showEllipsis && ellipsisCells > 0 && viewEndIndex < lineSpan.Length;

                    if (left)
                    {
                        _writerUnsafe.Foreground(ConsoleColor.DarkGray);
                        _writerUnsafe.Write(options.Ellipsis);
                        _writerUnsafe.ResetStyle();
                    }

                    var viewLength = viewEndIndex - viewStartIndex;
                    if (options.MarkupRenderer is { } renderer)
                    {
                        var markup = renderer(lineSpan, controller.CursorIndex, viewStartIndex, viewLength, controller.SelectionStart, controller.SelectionLength);
                        _markupUnsafe!.Write(markup);
                    }
                    else if (viewLength > 0)
                    {
                        _markupUnsafe!.WriteEscape(lineSpan.Slice(viewStartIndex, viewLength));
                    }

                    if (right)
                    {
                        _writerUnsafe.Foreground(ConsoleColor.DarkGray);
                        _writerUnsafe.Write(options.Ellipsis);
                        _writerUnsafe.ResetStyle();
                    }

                    _writerUnsafe.EraseLine(0);

                    var cursorInViewCells = cursorCells - contentStartCell;
                    if (cursorInViewCells < 0) cursorInViewCells = 0;
                    if (cursorInViewCells > contentCells) cursorInViewCells = contentCells;

                    var cursorColumn = origin.Column + promptCells + (left ? ellipsisCells : 0) + cursorInViewCells;
                    _backend.SetCursorPosition(new TerminalPosition(cursorColumn, origin.Row));
                }
                finally
                {
                    if (restoreCursorVisible)
                    {
                        _backend.SetCursorVisible(true);
                    }
                }
            }
        }
    }

    private void AddToHistoryIfEnabled(TerminalReadLineOptions options, List<char> buffer)
    {
        if (!options.EnableHistory || !options.AddToHistory)
        {
            return;
        }

        if (options.HistoryCapacity <= 0)
        {
            return;
        }

        var text = new string(CollectionsMarshal.AsSpan(buffer));
        if (string.IsNullOrEmpty(text))
        {
            return;
        }

        options.History.Add(text, options.HistoryCapacity);
    }

}
