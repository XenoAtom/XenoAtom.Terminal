// Copyright (c) Alexandre Mutel. All rights reserved.
// Licensed under the BSD-Clause 2 license.
// See license.txt file in the project root for full license information.

using System.IO;
using System.Runtime.InteropServices;

namespace XenoAtom.Terminal;

public sealed partial class TerminalInstance
{
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
                    if (RemoveLastRune(buffer) && options.Echo)
                    {
                        WriteAtomic(static (TextWriter w) => w.Write("\b \b"));
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

        static bool RemoveLastRune(List<char> buffer)
        {
            if (buffer.Count == 0)
            {
                return false;
            }

            var lastIndex = buffer.Count - 1;
            var last = buffer[lastIndex];
            if (char.IsLowSurrogate(last) && lastIndex > 0 && char.IsHighSurrogate(buffer[lastIndex - 1]))
            {
                buffer.RemoveAt(lastIndex);
                buffer.RemoveAt(lastIndex - 1);
                return true;
            }

            buffer.RemoveAt(lastIndex);
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

        var historyIndex = -1;
        string? historySnapshot = null;
        var mouseSelecting = false;
        var suppressMouseUpSelectionUpdate = false;

        IReadOnlyList<string>? completionCandidates = null;
        int completionCandidateIndex = -1;
        int completionReplaceStart = -1;
        int completionReplaceLength = 0;

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

                        RenderIfEcho();
                    }
                    break;

                case TerminalTextEvent text:
                    ResetCompletionSession();
                    if (!string.IsNullOrEmpty(text.Text))
                    {
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
                    if (key.Char is '\x04' && buffer.Count == 0)
                    {
                        // Ctrl+D on an empty line behaves like EOF.
                        return null;
                    }

                    if (key.Key != TerminalKey.Tab)
                    {
                        ResetCompletionSession();
                    }

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
                            RenderIfEcho();
                            break;
                        }
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
                        RenderIfEcho();
                    }

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
                    probe = TerminalTextUtility.GetPreviousRuneIndex(text, probe);
                }
                else if (probe < text.Length && char.IsWhiteSpace(text[probe]) && probe > 0)
                {
                    probe = TerminalTextUtility.GetPreviousRuneIndex(text, probe);
                }

                probe = Math.Clamp(probe, 0, Math.Max(0, text.Length - 1));
                var isWhitespace = char.IsWhiteSpace(text[probe]);

                var start = probe;
                while (start > 0)
                {
                    var prev = TerminalTextUtility.GetPreviousRuneIndex(text, start);
                    if (char.IsWhiteSpace(text[prev]) != isWhitespace)
                    {
                        break;
                    }
                    start = prev;
                }

                var endExclusive = TerminalTextUtility.GetNextRuneIndex(text, probe);
                while (endExclusive < text.Length)
                {
                    if (char.IsWhiteSpace(text[endExclusive]) != isWhitespace)
                    {
                        break;
                    }
                    endExclusive = TerminalTextUtility.GetNextRuneIndex(text, endExclusive);
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
                        ? TerminalTextUtility.GetPreviousRuneIndex(CollectionsMarshal.AsSpan(buffer), controller.CursorIndex)
                        : TerminalTextUtility.GetNextRuneIndex(CollectionsMarshal.AsSpan(buffer), controller.CursorIndex);
                    controller.SetCursorIndex(newIndex, extendSelection: true);
                    return true;
                }
            }

            switch (key.Key)
            {
                case TerminalKey.Left:
                    controller.SetCursorIndex(TerminalTextUtility.GetPreviousRuneIndex(CollectionsMarshal.AsSpan(buffer), controller.CursorIndex), extendSelection: false);
                    return true;
                case TerminalKey.Right:
                    controller.SetCursorIndex(TerminalTextUtility.GetNextRuneIndex(CollectionsMarshal.AsSpan(buffer), controller.CursorIndex), extendSelection: false);
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
                        var prev = TerminalTextUtility.GetPreviousRuneIndex(span, end);
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
                        var next = TerminalTextUtility.GetNextRuneIndex(span, start);
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
            if (key.Char is { } ch && (key.Modifiers.HasFlag(TerminalModifiers.Ctrl) || (ch is >= '\x01' and <= '\x1A')))
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
                            _readLineClipboard = new string(CollectionsMarshal.AsSpan(buffer).Slice(start, end - start));
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

        bool HandleCtrlKey(char ch)
        {
            switch (ch)
            {
                case '\x01': // Ctrl+A
                    controller.SetCursorIndex(0, extendSelection: false);
                    return true;
                case '\x05': // Ctrl+E
                    controller.SetCursorIndex(buffer.Count, extendSelection: false);
                    return true;
                case '\x02': // Ctrl+B
                    controller.SetCursorIndex(TerminalTextUtility.GetPreviousRuneIndex(CollectionsMarshal.AsSpan(buffer), controller.CursorIndex), extendSelection: false);
                    return true;
                case '\x06': // Ctrl+F
                    controller.SetCursorIndex(TerminalTextUtility.GetNextRuneIndex(CollectionsMarshal.AsSpan(buffer), controller.CursorIndex), extendSelection: false);
                    return true;
                case '\x10': // Ctrl+P
                    return options.EnableHistory && NavigateHistory(-1);
                case '\x0E': // Ctrl+N
                    return options.EnableHistory && NavigateHistory(+1);
                case '\x0B': // Ctrl+K
                    controller.ClearSelection();
                    if (controller.CursorIndex < buffer.Count)
                    {
                        var start = controller.CursorIndex;
                        _readLineClipboard = new string(CollectionsMarshal.AsSpan(buffer)[start..]);
                        controller.Remove(start, buffer.Count - start);
                        ResetHistoryNavigation();
                        return true;
                    }
                    return false;
                case '\x15': // Ctrl+U
                    controller.ClearSelection();
                    if (controller.CursorIndex > 0)
                    {
                        var end = controller.CursorIndex;
                        _readLineClipboard = new string(CollectionsMarshal.AsSpan(buffer)[..end]);
                        controller.Remove(0, end);
                        controller.SetCursorIndex(0, extendSelection: false);
                        ResetHistoryNavigation();
                        return true;
                    }
                    return false;
                case '\x17': // Ctrl+W
                    controller.ClearSelection();
                    if (controller.CursorIndex > 0)
                    {
                        var end = controller.CursorIndex;
                        var start = MoveWordLeft(CollectionsMarshal.AsSpan(buffer), end);
                        _readLineClipboard = new string(CollectionsMarshal.AsSpan(buffer).Slice(start, end - start));
                        controller.Remove(start, end - start);
                        controller.SetCursorIndex(start, extendSelection: false);
                        ResetHistoryNavigation();
                        return true;
                    }
                    return false;
                case '\x18': // Ctrl+X
                    if (buffer.Count > 0)
                    {
                        if (controller.HasSelection)
                        {
                            _readLineClipboard = new string(CollectionsMarshal.AsSpan(buffer).Slice(controller.SelectionStart, controller.SelectionLength));
                            controller.DeleteSelection();
                        }
                        else
                        {
                            _readLineClipboard = new string(CollectionsMarshal.AsSpan(buffer));
                            controller.Clear();
                        }
                        ResetHistoryNavigation();
                        return true;
                    }
                    return false;
                case '\x16': // Ctrl+V
                    if (!string.IsNullOrEmpty(_readLineClipboard))
                    {
                        controller.Insert(_readLineClipboard.AsSpan());
                        ResetHistoryNavigation();
                        return true;
                    }
                    return false;
                case '\x03': // Ctrl+C
                    throw new OperationCanceledException("ReadLine canceled by Ctrl+C.");
                case '\x0C': // Ctrl+L
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
                var prev = TerminalTextUtility.GetPreviousRuneIndex(text, i);
                if (!char.IsWhiteSpace(text[prev]))
                {
                    break;
                }
                i = prev;
            }

            while (i > 0)
            {
                var prev = TerminalTextUtility.GetPreviousRuneIndex(text, i);
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
                var next = TerminalTextUtility.GetNextRuneIndex(text, i);
                if (!char.IsWhiteSpace(text[i]))
                {
                    break;
                }
                i = next;
            }

            while (i < text.Length)
            {
                var next = TerminalTextUtility.GetNextRuneIndex(text, i);
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
