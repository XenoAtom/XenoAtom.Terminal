// Copyright (c) Alexandre Mutel. All rights reserved.
// Licensed under the BSD-Clause 2 license.
// See license.txt file in the project root for full license information.

namespace XenoAtom.Terminal;

/// <summary>
/// Stores line history for the interactive ReadLine editor.
/// </summary>
/// <remarks>
/// This history is intentionally not global to the terminal; callers can scope/share it by reusing
/// the same <see cref="TerminalReadLineOptions"/> instance (or by sharing this object explicitly).
/// </remarks>
public sealed class TerminalReadLineHistory
{
    private readonly Lock _lock = new();
    private readonly List<string> _items = new();

    /// <summary>
    /// Gets the number of history entries.
    /// </summary>
    public int Count
    {
        get
        {
            lock (_lock)
            {
                return _items.Count;
            }
        }
    }

    /// <summary>
    /// Clears the history.
    /// </summary>
    public void Clear()
    {
        lock (_lock)
        {
            _items.Clear();
        }
    }

    internal string? GetAt(int index)
    {
        lock (_lock)
        {
            if ((uint)index >= (uint)_items.Count)
            {
                return null;
            }

            return _items[index];
        }
    }

    internal void Add(string line, int capacity)
    {
        if (string.IsNullOrEmpty(line) || capacity <= 0)
        {
            return;
        }

        lock (_lock)
        {
            if (_items.Count == 0 || !string.Equals(_items[^1], line, StringComparison.Ordinal))
            {
                _items.Add(line);
            }

            if (_items.Count > capacity)
            {
                var remove = _items.Count - capacity;
                _items.RemoveRange(0, remove);
            }
        }
    }
}

