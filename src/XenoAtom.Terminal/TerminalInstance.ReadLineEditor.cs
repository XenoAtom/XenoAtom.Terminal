// Copyright (c) Alexandre Mutel. All rights reserved.
// Licensed under the BSD-Clause 2 license.
// See license.txt file in the project root for full license information.

using System.IO;
using System.Runtime.InteropServices;
using XenoAtom.Terminal.Internal;

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

        var promptCells = !string.IsNullOrEmpty(promptMarkup)
            ? MeasureStyledWidth(promptMarkup)
            : TerminalCellWidth.GetWidth(promptPlain.AsSpan());

        var buffer = new List<char>(64);
        var cursorIndex = 0;
        var selectionAnchor = -1;
        var selectionStart = 0;
        var selectionLength = 0;

        var historyIndex = -1;
        string? historySnapshot = null;

        var origin = GetCursorPosition();
        var windowColumns = Math.Max(1, GetWindowSize().Columns);
        var availableCells = Math.Max(1, options.ViewWidth ?? Math.Max(1, windowColumns - origin.Column - promptCells));

        var baseStyle = _style;

        void MoveCursor(int newIndex, bool extendSelection)
        {
            newIndex = Math.Clamp(newIndex, 0, buffer.Count);
            if (!extendSelection)
            {
                cursorIndex = newIndex;
                ClearSelection();
                return;
            }

            if (selectionAnchor < 0)
            {
                selectionAnchor = cursorIndex;
            }

            cursorIndex = newIndex;
            var a = selectionAnchor;
            var b = cursorIndex;
            if (a <= b)
            {
                selectionStart = a;
                selectionLength = b - a;
            }
            else
            {
                selectionStart = b;
                selectionLength = a - b;
            }
        }

        void ClearSelection()
        {
            selectionAnchor = -1;
            selectionStart = 0;
            selectionLength = 0;
        }

        void DeleteSelectionIfAny()
        {
            if (selectionLength <= 0)
            {
                return;
            }

            buffer.RemoveRange(selectionStart, selectionLength);
            cursorIndex = selectionStart;
            ClearSelection();
        }

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
                    if (!string.IsNullOrEmpty(paste.Text))
                    {
                        if (InsertText(paste.Text.AsSpan(), out var accepted))
                        {
                            if (options.Echo)
                            {
                                cursorIndex = buffer.Count;
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
                    if (!string.IsNullOrEmpty(text.Text))
                    {
                        if (InsertText(text.Text.AsSpan(), out var accepted))
                        {
                            if (options.Echo)
                            {
                                cursorIndex = buffer.Count;
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

                case TerminalKeyEvent key:
                    if (key.Char is '\x04' && buffer.Count == 0)
                    {
                        // Ctrl+D on an empty line behaves like EOF.
                        return null;
                    }

                    if (options.KeyHandler is { } handler)
                    {
                        var handling = handler(key, CollectionsMarshal.AsSpan(buffer), cursorIndex, selectionStart, selectionLength);
                        if (handling.Cancel)
                        {
                            throw new OperationCanceledException("ReadLine canceled by key handler.");
                        }

                        if (handling.ReplaceText is not null)
                        {
                            SetBuffer(handling.ReplaceText);
                            cursorIndex = Math.Clamp(handling.CursorIndex ?? buffer.Count, 0, buffer.Count);
                            ResetHistoryNavigation();
                            ClearSelection();
                        }
                        else if (!string.IsNullOrEmpty(handling.InsertText))
                        {
                            InsertAtCursor(handling.InsertText.AsSpan());
                            cursorIndex = Math.Clamp(handling.CursorIndex ?? cursorIndex, 0, buffer.Count);
                            ResetHistoryNavigation();
                            ClearSelection();
                        }
                        else if (handling.CursorIndex is { } forcedCursor)
                        {
                            MoveCursor(Math.Clamp(forcedCursor, 0, buffer.Count), extendSelection: false);
                        }

                        if (handling.Accept)
                        {
                            if (options.Echo)
                            {
                                cursorIndex = buffer.Count;
                                Render();
                                if (options.EmitNewLineOnAccept)
                                {
                                    WriteLine();
                                }
                            }

                            AddToHistoryIfEnabled(options, buffer);
                            return new string(CollectionsMarshal.AsSpan(buffer));
                        }

                        if (handling.Handled)
                        {
                            RenderIfEcho();
                            break;
                        }
                    }

                    if (key.Key == TerminalKey.Enter)
                    {
                        if (options.Echo)
                        {
                            cursorIndex = buffer.Count;
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

        bool HandleEditorKey(TerminalKeyEvent key)
        {
            // Completion (Tab)
            if (key.Key == TerminalKey.Tab && options.CompletionHandler is { } completion)
            {
                var result = completion(CollectionsMarshal.AsSpan(buffer), cursorIndex, selectionStart, selectionLength);
                    if (result.Handled)
                    {
                        if (result.ReplaceText is not null)
                        {
                            SetBuffer(result.ReplaceText);
                            cursorIndex = Math.Clamp(result.CursorIndex ?? buffer.Count, 0, buffer.Count);
                            ClearSelection();
                        }
                        else if (!string.IsNullOrEmpty(result.InsertText))
                        {
                            InsertAtCursor(result.InsertText.AsSpan());
                            cursorIndex = Math.Clamp(result.CursorIndex ?? cursorIndex, 0, buffer.Count);
                            ClearSelection();
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
                        ? MoveWordLeft(CollectionsMarshal.AsSpan(buffer), cursorIndex)
                        : MoveWordRight(CollectionsMarshal.AsSpan(buffer), cursorIndex);
                    MoveCursor(newIndex, extendSelection: true);
                    return true;
                }

                if (wordModifier)
                {
                    var newIndex = key.Key == TerminalKey.Left
                        ? MoveWordLeft(CollectionsMarshal.AsSpan(buffer), cursorIndex)
                        : MoveWordRight(CollectionsMarshal.AsSpan(buffer), cursorIndex);
                    MoveCursor(newIndex, extendSelection: false);
                    return true;
                }

                if (shift)
                {
                    var newIndex = key.Key == TerminalKey.Left
                        ? TerminalCellWidth.GetPreviousRuneIndex(CollectionsMarshal.AsSpan(buffer), cursorIndex)
                        : TerminalCellWidth.GetNextRuneIndex(CollectionsMarshal.AsSpan(buffer), cursorIndex);
                    MoveCursor(newIndex, extendSelection: true);
                    return true;
                }
            }

            switch (key.Key)
            {
                case TerminalKey.Left:
                    MoveCursor(TerminalCellWidth.GetPreviousRuneIndex(CollectionsMarshal.AsSpan(buffer), cursorIndex), extendSelection: false);
                    return true;
                case TerminalKey.Right:
                    MoveCursor(TerminalCellWidth.GetNextRuneIndex(CollectionsMarshal.AsSpan(buffer), cursorIndex), extendSelection: false);
                    return true;
                case TerminalKey.Home:
                    MoveCursor(0, extendSelection: key.Modifiers.HasFlag(TerminalModifiers.Shift));
                    return true;
                case TerminalKey.End:
                    MoveCursor(buffer.Count, extendSelection: key.Modifiers.HasFlag(TerminalModifiers.Shift));
                    return true;
                case TerminalKey.Backspace:
                    if (selectionLength > 0)
                    {
                        DeleteSelectionIfAny();
                        ResetHistoryNavigation();
                        return true;
                    }
                    if ((key.Modifiers & (TerminalModifiers.Ctrl | TerminalModifiers.Alt)) != 0 && cursorIndex > 0)
                    {
                        var span = CollectionsMarshal.AsSpan(buffer);
                        var start = MoveWordLeft(span, cursorIndex);
                        if (start != cursorIndex)
                        {
                            buffer.RemoveRange(start, cursorIndex - start);
                            cursorIndex = start;
                            ResetHistoryNavigation();
                            ClearSelection();
                            return true;
                        }
                        return false;
                    }
                    if (cursorIndex > 0)
                    {
                        var span = CollectionsMarshal.AsSpan(buffer);
                        var prev = TerminalCellWidth.GetPreviousRuneIndex(span, cursorIndex);
                        buffer.RemoveRange(prev, cursorIndex - prev);
                        cursorIndex = prev;
                        ResetHistoryNavigation();
                        ClearSelection();
                        return true;
                    }
                    return false;
                case TerminalKey.Delete:
                    if (selectionLength > 0)
                    {
                        DeleteSelectionIfAny();
                        ResetHistoryNavigation();
                        return true;
                    }
                    if ((key.Modifiers & (TerminalModifiers.Ctrl | TerminalModifiers.Alt)) != 0 && cursorIndex < buffer.Count)
                    {
                        var span = CollectionsMarshal.AsSpan(buffer);
                        var end = MoveWordRight(span, cursorIndex);
                        if (end != cursorIndex)
                        {
                            buffer.RemoveRange(cursorIndex, end - cursorIndex);
                            ResetHistoryNavigation();
                            ClearSelection();
                            return true;
                        }
                        return false;
                    }
                    if (cursorIndex < buffer.Count)
                    {
                        var span = CollectionsMarshal.AsSpan(buffer);
                        var next = TerminalCellWidth.GetNextRuneIndex(span, cursorIndex);
                        buffer.RemoveRange(cursorIndex, next - cursorIndex);
                        ResetHistoryNavigation();
                        ClearSelection();
                        return true;
                    }
                    return false;
                case TerminalKey.Up:
                    ClearSelection();
                    return options.EnableHistory && NavigateHistory(-1);
                case TerminalKey.Down:
                    ClearSelection();
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
                        MoveCursor(MoveWordLeft(CollectionsMarshal.AsSpan(buffer), cursorIndex), extendSelection: extend);
                        return true;
                    case 'f': // Alt+F: word right
                        MoveCursor(MoveWordRight(CollectionsMarshal.AsSpan(buffer), cursorIndex), extendSelection: extend);
                        return true;
                    case 'd': // Alt+D: delete word right
                        ClearSelection();
                        if (cursorIndex < buffer.Count)
                        {
                            var end = MoveWordRight(CollectionsMarshal.AsSpan(buffer), cursorIndex);
                            _readLineClipboard = new string(CollectionsMarshal.AsSpan(buffer).Slice(cursorIndex, end - cursorIndex));
                            buffer.RemoveRange(cursorIndex, end - cursorIndex);
                            ResetHistoryNavigation();
                            return true;
                        }
                        return false;
                }
            }

            return false;
        }

        bool HandleCtrlKey(char ch)
        {
            switch (ch)
            {
                case '\x01': // Ctrl+A
                    cursorIndex = 0;
                    return true;
                case '\x05': // Ctrl+E
                    cursorIndex = buffer.Count;
                    return true;
                case '\x02': // Ctrl+B
                    MoveCursor(TerminalCellWidth.GetPreviousRuneIndex(CollectionsMarshal.AsSpan(buffer), cursorIndex), extendSelection: false);
                    return true;
                case '\x06': // Ctrl+F
                    MoveCursor(TerminalCellWidth.GetNextRuneIndex(CollectionsMarshal.AsSpan(buffer), cursorIndex), extendSelection: false);
                    return true;
                case '\x10': // Ctrl+P
                    return options.EnableHistory && NavigateHistory(-1);
                case '\x0E': // Ctrl+N
                    return options.EnableHistory && NavigateHistory(+1);
                case '\x0B': // Ctrl+K
                    ClearSelection();
                    if (cursorIndex < buffer.Count)
                    {
                        _readLineClipboard = new string(CollectionsMarshal.AsSpan(buffer)[cursorIndex..]);
                        buffer.RemoveRange(cursorIndex, buffer.Count - cursorIndex);
                        ResetHistoryNavigation();
                        return true;
                    }
                    return false;
                case '\x15': // Ctrl+U
                    ClearSelection();
                    if (cursorIndex > 0)
                    {
                        _readLineClipboard = new string(CollectionsMarshal.AsSpan(buffer)[..cursorIndex]);
                        buffer.RemoveRange(0, cursorIndex);
                        cursorIndex = 0;
                        ResetHistoryNavigation();
                        return true;
                    }
                    return false;
                case '\x17': // Ctrl+W
                    ClearSelection();
                    if (cursorIndex > 0)
                    {
                        var start = MoveWordLeft(CollectionsMarshal.AsSpan(buffer), cursorIndex);
                        _readLineClipboard = new string(CollectionsMarshal.AsSpan(buffer).Slice(start, cursorIndex - start));
                        buffer.RemoveRange(start, cursorIndex - start);
                        cursorIndex = start;
                        ResetHistoryNavigation();
                        return true;
                    }
                    return false;
                case '\x18': // Ctrl+X
                    if (buffer.Count > 0)
                    {
                        if (selectionLength > 0)
                        {
                            _readLineClipboard = new string(CollectionsMarshal.AsSpan(buffer).Slice(selectionStart, selectionLength));
                            buffer.RemoveRange(selectionStart, selectionLength);
                            cursorIndex = selectionStart;
                            ClearSelection();
                        }
                        else
                        {
                            _readLineClipboard = new string(CollectionsMarshal.AsSpan(buffer));
                            buffer.Clear();
                            cursorIndex = 0;
                        }
                        ResetHistoryNavigation();
                        return true;
                    }
                    return false;
                case '\x16': // Ctrl+V
                    if (!string.IsNullOrEmpty(_readLineClipboard))
                    {
                        InsertAtCursor(_readLineClipboard.AsSpan());
                        ResetHistoryNavigation();
                        ClearSelection();
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
                var prev = TerminalCellWidth.GetPreviousRuneIndex(text, i);
                if (!char.IsWhiteSpace(text[prev]))
                {
                    break;
                }
                i = prev;
            }

            while (i > 0)
            {
                var prev = TerminalCellWidth.GetPreviousRuneIndex(text, i);
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
                var next = TerminalCellWidth.GetNextRuneIndex(text, i);
                if (!char.IsWhiteSpace(text[i]))
                {
                    break;
                }
                i = next;
            }

            while (i < text.Length)
            {
                var next = TerminalCellWidth.GetNextRuneIndex(text, i);
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
            cursorIndex = buffer.Count;
            ClearSelection();
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
                    InsertAtCursor(text[..newlineIndex]);
                }

                acceptedLine = new string(CollectionsMarshal.AsSpan(buffer));
                return true;
            }

            InsertAtCursor(text);
            return false;
        }

        void InsertAtCursor(ReadOnlySpan<char> text)
        {
            if (text.IsEmpty)
            {
                return;
            }

            if (options.MaxLength is { } maxLen)
            {
                var remaining = maxLen - buffer.Count;
                if (remaining <= 0)
                {
                    return;
                }

                if (text.Length > remaining)
                {
                    text = text[..remaining];
                }
            }

            DeleteSelectionIfAny();

            var oldCount = buffer.Count;
            CollectionsMarshal.SetCount(buffer, oldCount + text.Length);
            var span = CollectionsMarshal.AsSpan(buffer);
            span.Slice(cursorIndex, oldCount - cursorIndex).CopyTo(span.Slice(cursorIndex + text.Length));
            text.CopyTo(span.Slice(cursorIndex, text.Length));
            cursorIndex += text.Length;
            ResetHistoryNavigation();
            ClearSelection();
        }

        void Render()
        {
            lock (_outputLock)
            {
                _backend!.SetCursorPosition(origin);

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
                var totalCells = TerminalCellWidth.GetWidth(lineSpan);
                var cursorCells = TerminalCellWidth.GetWidth(lineSpan[..cursorIndex]);

                var showEllipsis = options.ShowEllipsis && !string.IsNullOrEmpty(options.Ellipsis);
                var ellipsisCells = showEllipsis ? TerminalCellWidth.GetWidth(options.Ellipsis.AsSpan()) : 0;

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

                TerminalCellWidth.TryGetIndexAtCell(lineSpan, contentStartCell, out var viewStartIndex);
                TerminalCellWidth.TryGetIndexAtCell(lineSpan, contentStartCell + contentCells, out var viewEndIndex);

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
                    var markup = renderer(lineSpan, cursorIndex, viewStartIndex, viewLength, selectionStart, selectionLength);
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
