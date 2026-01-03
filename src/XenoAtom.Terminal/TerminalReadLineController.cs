// Copyright (c) Alexandre Mutel. All rights reserved.
// Licensed under the BSD-Clause 2 license.
// See license.txt file in the project root for full license information.

using System.Runtime.InteropServices;

namespace XenoAtom.Terminal;

/// <summary>
/// Controller passed to ReadLine callbacks to inspect and edit the current line.
/// </summary>
/// <remarks>
/// The controller is only valid during a ReadLine operation. Using it after ReadLine completes throws.
/// </remarks>
public sealed class TerminalReadLineController
{
    private readonly List<char> _buffer;
    private readonly int? _maxLength;

    private bool _isActive;

    private int _selectionAnchor = -1;

    internal TerminalReadLineController(List<char> buffer, int? maxLength)
    {
        _buffer = buffer ?? throw new ArgumentNullException(nameof(buffer));
        _maxLength = maxLength;
    }

    /// <summary>
    /// Gets or sets whether the current input event should skip default handling.
    /// </summary>
    public bool Handled { get; set; }

    /// <summary>
    /// Gets the current text being edited (UTF-16).
    /// </summary>
    public ReadOnlySpan<char> Text
    {
        get
        {
            ThrowIfNotActive();
            return CollectionsMarshal.AsSpan(_buffer);
        }
    }

    /// <summary>
    /// Gets the current text as a string.
    /// </summary>
    public string TextString
    {
        get
        {
            ThrowIfNotActive();
            return new string(CollectionsMarshal.AsSpan(_buffer));
        }
    }

    /// <summary>
    /// Gets the number of UTF-16 code units in the current text.
    /// </summary>
    public int Length
    {
        get
        {
            ThrowIfNotActive();
            return _buffer.Count;
        }
    }

    /// <summary>
    /// Gets the cursor index in UTF-16 code units (0..Length).
    /// </summary>
    public int CursorIndex { get; private set; }

    /// <summary>
    /// Gets the selection start (UTF-16 code units).
    /// </summary>
    public int SelectionStart { get; private set; }

    /// <summary>
    /// Gets the selection length (UTF-16 code units).
    /// </summary>
    public int SelectionLength { get; private set; }

    /// <summary>
    /// Gets whether there is an active selection.
    /// </summary>
    public bool HasSelection => SelectionLength > 0;

    internal bool TextChanged { get; private set; }
    internal bool StateChanged { get; private set; }
    internal bool AcceptRequested { get; private set; }
    internal bool CancelRequested { get; private set; }

    /// <summary>
    /// Moves the cursor to the specified position.
    /// </summary>
    public void SetCursorIndex(int index, bool extendSelection = false)
    {
        ThrowIfNotActive();

        index = Math.Clamp(index, 0, _buffer.Count);
        if (!extendSelection)
        {
            CursorIndex = index;
            ClearSelection();
            StateChanged = true;
            return;
        }

        if (_selectionAnchor < 0)
        {
            _selectionAnchor = CursorIndex;
        }

        CursorIndex = index;
        UpdateSelectionFromAnchor();
        Handled = true;
        StateChanged = true;
    }

    /// <summary>
    /// Clears the selection.
    /// </summary>
    public void ClearSelection()
    {
        ThrowIfNotActive();
        _selectionAnchor = -1;
        SelectionStart = 0;
        SelectionLength = 0;
        StateChanged = true;
    }

    /// <summary>
    /// Selects the specified range (start..endExclusive) and sets the cursor at <paramref name="endExclusive"/>.
    /// </summary>
    public void Select(int start, int endExclusive)
    {
        ThrowIfNotActive();

        start = Math.Clamp(start, 0, _buffer.Count);
        endExclusive = Math.Clamp(endExclusive, 0, _buffer.Count);

        _selectionAnchor = start;
        CursorIndex = endExclusive;
        UpdateSelectionFromAnchor();
        Handled = true;
        StateChanged = true;
    }

    /// <summary>
    /// Starts a selection anchor at the specified index (no selection yet).
    /// </summary>
    public void BeginSelection(int anchorIndex)
    {
        ThrowIfNotActive();

        anchorIndex = Math.Clamp(anchorIndex, 0, _buffer.Count);
        CursorIndex = anchorIndex;
        _selectionAnchor = anchorIndex;
        SelectionStart = 0;
        SelectionLength = 0;
        Handled = true;
        StateChanged = true;
    }

    /// <summary>
    /// Deletes the current selection, if any.
    /// </summary>
    public void DeleteSelection()
    {
        ThrowIfNotActive();
        if (SelectionLength <= 0)
        {
            return;
        }

        _buffer.RemoveRange(SelectionStart, SelectionLength);
        CursorIndex = SelectionStart;
        ClearSelection();
        Handled = true;
        TextChanged = true;
        StateChanged = true;
    }

    /// <summary>
    /// Inserts text at the current cursor position (replaces selection if any).
    /// </summary>
    public void Insert(ReadOnlySpan<char> text)
    {
        ThrowIfNotActive();
        if (text.IsEmpty)
        {
            return;
        }

        if (_maxLength is { } max && _buffer.Count >= max)
        {
            return;
        }

        DeleteSelection();

        var allowed = text.Length;
        if (_maxLength is { } maxLen)
        {
            allowed = Math.Min(allowed, maxLen - _buffer.Count);
        }

        if (allowed <= 0)
        {
            return;
        }

        InsertAtCore(CursorIndex, text.Slice(0, allowed));
        CursorIndex += allowed;
        ClearSelection();
        Handled = true;
        TextChanged = true;
        StateChanged = true;
    }

    /// <summary>
    /// Inserts text at the specified index and sets the cursor after the inserted text.
    /// </summary>
    public void InsertAt(int index, ReadOnlySpan<char> text)
    {
        ThrowIfNotActive();
        if (text.IsEmpty)
        {
            return;
        }

        index = Math.Clamp(index, 0, _buffer.Count);

        var allowed = text.Length;
        if (_maxLength is { } maxLen)
        {
            allowed = Math.Min(allowed, maxLen - _buffer.Count);
        }

        if (allowed <= 0)
        {
            return;
        }

        InsertAtCore(index, text.Slice(0, allowed));
        CursorIndex = index + allowed;
        ClearSelection();
        Handled = true;
        TextChanged = true;
        StateChanged = true;
    }

    /// <summary>
    /// Removes a range of characters.
    /// </summary>
    public void Remove(int index, int length)
    {
        ThrowIfNotActive();

        if (length <= 0 || _buffer.Count == 0)
        {
            return;
        }

        index = Math.Clamp(index, 0, _buffer.Count);
        length = Math.Clamp(length, 0, _buffer.Count - index);
        if (length == 0)
        {
            return;
        }

        _buffer.RemoveRange(index, length);
        CursorIndex = Math.Clamp(CursorIndex, 0, _buffer.Count);
        ClearSelection();
        Handled = true;
        TextChanged = true;
        StateChanged = true;
    }

    /// <summary>
    /// Replaces a range of characters with the specified text.
    /// </summary>
    public void Replace(int index, int length, ReadOnlySpan<char> text)
    {
        ThrowIfNotActive();

        index = Math.Clamp(index, 0, _buffer.Count);
        length = Math.Clamp(length, 0, _buffer.Count - index);

        if (length > 0)
        {
            _buffer.RemoveRange(index, length);
        }

        var allowed = text.Length;
        if (_maxLength is { } maxLen)
        {
            allowed = Math.Min(allowed, maxLen - _buffer.Count);
        }

        if (allowed > 0)
        {
            InsertAtCore(index, text.Slice(0, allowed));
            CursorIndex = index + allowed;
        }
        else
        {
            CursorIndex = index;
        }

        ClearSelection();
        Handled = true;
        TextChanged = true;
        StateChanged = true;
    }

    /// <summary>
    /// Clears the current line.
    /// </summary>
    public void Clear()
    {
        ThrowIfNotActive();

        if (_buffer.Count == 0)
        {
            return;
        }

        _buffer.Clear();
        CursorIndex = 0;
        ClearSelection();
        Handled = true;
        TextChanged = true;
        StateChanged = true;
    }

    /// <summary>
    /// Requests accepting the current line.
    /// </summary>
    public void Accept()
    {
        ThrowIfNotActive();
        AcceptRequested = true;
        Handled = true;
        StateChanged = true;
    }

    /// <summary>
    /// Requests canceling the current ReadLine.
    /// </summary>
    public void Cancel()
    {
        ThrowIfNotActive();
        CancelRequested = true;
        Handled = true;
        StateChanged = true;
    }

    internal void BeginCallback()
    {
        ThrowIfNotActive();
        Handled = false;
        TextChanged = false;
        StateChanged = false;
        AcceptRequested = false;
        CancelRequested = false;
    }

    internal void EndCallback()
    {
        // No-op. The controller stays active for the duration of a ReadLine operation.
    }

    internal void Activate() => _isActive = true;

    internal void Deactivate() => _isActive = false;

    private void UpdateSelectionFromAnchor()
    {
        var a = _selectionAnchor;
        var b = CursorIndex;
        if (a < 0)
        {
            SelectionStart = 0;
            SelectionLength = 0;
            return;
        }

        if (a <= b)
        {
            SelectionStart = a;
            SelectionLength = b - a;
        }
        else
        {
            SelectionStart = b;
            SelectionLength = a - b;
        }
    }

    private void InsertAtCore(int index, ReadOnlySpan<char> text)
    {
        var oldCount = _buffer.Count;
        CollectionsMarshal.SetCount(_buffer, oldCount + text.Length);
        var span = CollectionsMarshal.AsSpan(_buffer);
        span.Slice(index, oldCount - index).CopyTo(span.Slice(index + text.Length));
        text.CopyTo(span.Slice(index, text.Length));
    }

    private void ThrowIfNotActive()
    {
        if (!_isActive)
        {
            throw new InvalidOperationException("The TerminalReadLineController is only valid during a ReadLine callback.");
        }
    }
}
