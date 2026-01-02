// Copyright (c) Alexandre Mutel. All rights reserved.
// Licensed under the BSD-Clause 2 license.
// See license.txt file in the project root for full license information.

namespace XenoAtom.Terminal;

/// <summary>
/// Options controlling the terminal input loop.
/// </summary>
public sealed class TerminalInputOptions
{
    /// <summary>
    /// Enables publishing <see cref="TerminalResizeEvent"/> events when the terminal size changes.
    /// </summary>
    public bool EnableResizeEvents { get; set; } = true;

    /// <summary>
    /// Enables publishing <see cref="TerminalMouseEvent"/> events.
    /// </summary>
    public bool EnableMouseEvents { get; set; } = true;

    /// <summary>
    /// Selects the level of mouse reporting to enable when mouse events are enabled.
    /// </summary>
    public TerminalMouseMode MouseMode { get; set; } = TerminalMouseMode.Drag;

    /// <summary>
    /// When enabled, Ctrl+C / Ctrl+Break are treated as regular key input (best effort) instead of terminal signals.
    /// </summary>
    public bool TreatControlCAsInput { get; set; }

    /// <summary>
     /// Captures Ctrl+C (Console.CancelKeyPress) and publishes a <see cref="TerminalSignalEvent"/>.
     /// </summary>
    public bool CaptureCtrlC { get; set; } = true;

    /// <summary>
     /// Captures Ctrl+Break (Console.CancelKeyPress) and publishes a <see cref="TerminalSignalEvent"/>.
     /// </summary>
    public bool CaptureCtrlBreak { get; set; } = true;
}
