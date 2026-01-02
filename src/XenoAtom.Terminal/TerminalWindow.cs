// Copyright (c) Alexandre Mutel. All rights reserved.
// Licensed under the BSD-Clause 2 license.
// See license.txt file in the project root for full license information.

namespace XenoAtom.Terminal;

/// <summary>
/// Provides window and buffer size operations.
/// </summary>
public sealed class TerminalWindow
{
    private readonly TerminalInstance _terminal;

    internal TerminalWindow(TerminalInstance terminal)
    {
        _terminal = terminal ?? throw new ArgumentNullException(nameof(terminal));
    }

    /// <summary>
    /// Gets or sets the window width in character cells.
    /// </summary>
    public int Width
    {
        get => _terminal.GetWindowSize().Columns;
        set => _terminal.SetWindowSize(new TerminalSize(value, Height));
    }

    /// <summary>
    /// Gets or sets the window height in character cells.
    /// </summary>
    public int Height
    {
        get => _terminal.GetWindowSize().Rows;
        set => _terminal.SetWindowSize(new TerminalSize(Width, value));
    }

    /// <summary>
    /// Gets or sets the window size in character cells.
    /// </summary>
    public TerminalSize Size
    {
        get => _terminal.GetWindowSize();
        set => _terminal.SetWindowSize(value);
    }

    /// <summary>
    /// Gets or sets the buffer width in character cells.
    /// </summary>
    public int BufferWidth
    {
        get => _terminal.GetBufferSize().Columns;
        set => _terminal.SetBufferSize(new TerminalSize(value, BufferHeight));
    }

    /// <summary>
    /// Gets or sets the buffer height in character cells.
    /// </summary>
    public int BufferHeight
    {
        get => _terminal.GetBufferSize().Rows;
        set => _terminal.SetBufferSize(new TerminalSize(BufferWidth, value));
    }

    /// <summary>
    /// Gets or sets the buffer size in character cells.
    /// </summary>
    public TerminalSize BufferSize
    {
        get => _terminal.GetBufferSize();
        set => _terminal.SetBufferSize(value);
    }

    /// <summary>
    /// Gets the largest supported window size in character cells (best effort).
    /// </summary>
    public TerminalSize LargestSize => _terminal.GetLargestWindowSize();
}

