// Copyright (c) Alexandre Mutel. All rights reserved.
// Licensed under the BSD-Clause 2 license.
// See license.txt file in the project root for full license information.

using System.Diagnostics.CodeAnalysis;

namespace XenoAtom.Terminal;

/// <summary>
/// Maps key gestures to ReadLine editor commands.
/// </summary>
public sealed class TerminalReadLineKeyBindings
{
    private readonly Dictionary<TerminalKeyGesture, TerminalReadLineCommand> _bindings = new();

    /// <summary>
    /// Creates a bindings instance pre-populated with the default editor key map.
    /// </summary>
    public static TerminalReadLineKeyBindings CreateDefault()
    {
        var b = new TerminalReadLineKeyBindings();

        // Acceptance / cancel / completion.
        b.Bind(TerminalKey.Enter, TerminalModifiers.None, TerminalReadLineCommand.AcceptLine);
        b.Bind(TerminalKey.Escape, TerminalModifiers.None, TerminalReadLineCommand.Cancel);
        b.Bind(TerminalKey.Tab, TerminalModifiers.None, TerminalReadLineCommand.Complete);

        // Movement.
        b.Bind(TerminalKey.Left, TerminalModifiers.None, TerminalReadLineCommand.CursorLeft);
        b.Bind(TerminalKey.Right, TerminalModifiers.None, TerminalReadLineCommand.CursorRight);
        b.Bind(TerminalKey.Home, TerminalModifiers.None, TerminalReadLineCommand.CursorHome);
        b.Bind(TerminalKey.End, TerminalModifiers.None, TerminalReadLineCommand.CursorEnd);
        b.Bind(TerminalKey.Left, TerminalModifiers.Ctrl, TerminalReadLineCommand.CursorWordLeft);
        b.Bind(TerminalKey.Right, TerminalModifiers.Ctrl, TerminalReadLineCommand.CursorWordRight);
        b.BindChar('b', TerminalModifiers.Alt, TerminalReadLineCommand.CursorWordLeft);
        b.BindChar('f', TerminalModifiers.Alt, TerminalReadLineCommand.CursorWordRight);

        // History.
        b.Bind(TerminalKey.Up, TerminalModifiers.None, TerminalReadLineCommand.HistoryPrevious);
        b.Bind(TerminalKey.Down, TerminalModifiers.None, TerminalReadLineCommand.HistoryNext);
        b.BindChar('\x10', TerminalModifiers.Ctrl, TerminalReadLineCommand.HistoryPrevious); // Ctrl+P
        b.BindChar('\x0E', TerminalModifiers.Ctrl, TerminalReadLineCommand.HistoryNext); // Ctrl+N

        // Editing.
        b.Bind(TerminalKey.Backspace, TerminalModifiers.None, TerminalReadLineCommand.DeleteBackward);
        b.Bind(TerminalKey.Delete, TerminalModifiers.None, TerminalReadLineCommand.DeleteForward);
        b.Bind(TerminalKey.Backspace, TerminalModifiers.Ctrl, TerminalReadLineCommand.DeleteWordBackward);
        b.Bind(TerminalKey.Delete, TerminalModifiers.Ctrl, TerminalReadLineCommand.DeleteWordForward);
        b.BindChar('d', TerminalModifiers.Alt, TerminalReadLineCommand.KillWordRight);
        b.BindChar('\x01', TerminalModifiers.Ctrl, TerminalReadLineCommand.CursorHome); // Ctrl+A
        b.BindChar('\x05', TerminalModifiers.Ctrl, TerminalReadLineCommand.CursorEnd); // Ctrl+E
        b.BindChar('\x02', TerminalModifiers.Ctrl, TerminalReadLineCommand.CursorLeft); // Ctrl+B
        b.BindChar('\x06', TerminalModifiers.Ctrl, TerminalReadLineCommand.CursorRight); // Ctrl+F

        // Clipboard / selection.
        b.BindChar('\x03', TerminalModifiers.Ctrl, TerminalReadLineCommand.CopySelectionOrCancel); // Ctrl+C
        b.BindChar('\x18', TerminalModifiers.Ctrl, TerminalReadLineCommand.CutSelectionOrAll); // Ctrl+X
        b.BindChar('\x16', TerminalModifiers.Ctrl, TerminalReadLineCommand.Paste); // Ctrl+V
        b.BindChar('\x0B', TerminalModifiers.Ctrl, TerminalReadLineCommand.KillToEnd); // Ctrl+K
        b.BindChar('\x15', TerminalModifiers.Ctrl, TerminalReadLineCommand.KillToStart); // Ctrl+U
        b.BindChar('\x17', TerminalModifiers.Ctrl, TerminalReadLineCommand.KillWordLeft); // Ctrl+W

        // Clear screen.
        b.BindChar('\x0C', TerminalModifiers.Ctrl, TerminalReadLineCommand.ClearScreen); // Ctrl+L

        // Undo/redo.
        b.BindChar('\x1A', TerminalModifiers.Ctrl, TerminalReadLineCommand.Undo); // Ctrl+Z
        b.BindChar('\x19', TerminalModifiers.Ctrl, TerminalReadLineCommand.Redo); // Ctrl+Y

        // Reverse search.
        b.BindChar('\x12', TerminalModifiers.Ctrl, TerminalReadLineCommand.ReverseSearch); // Ctrl+R

        return b;
    }

    /// <summary>
    /// Sets the command for the specified key gesture.
    /// </summary>
    public void Bind(TerminalKey key, TerminalModifiers modifiers, TerminalReadLineCommand command)
        => _bindings[new TerminalKeyGesture(key, Char: null, modifiers)] = command;

    /// <summary>
    /// Sets the command for the specified character gesture.
    /// </summary>
    public void BindChar(char ch, TerminalModifiers modifiers, TerminalReadLineCommand command)
        => _bindings[new TerminalKeyGesture(TerminalKey.Unknown, ch, modifiers)] = command;

    /// <summary>
    /// Removes a binding for the specified key gesture.
    /// </summary>
    public bool Unbind(TerminalKey key, TerminalModifiers modifiers)
        => _bindings.Remove(new TerminalKeyGesture(key, Char: null, modifiers));

    /// <summary>
    /// Removes a binding for the specified character gesture.
    /// </summary>
    public bool UnbindChar(char ch, TerminalModifiers modifiers)
        => _bindings.Remove(new TerminalKeyGesture(TerminalKey.Unknown, ch, modifiers));

    /// <summary>
    /// Tries to get the bound command for a gesture.
    /// </summary>
    public bool TryGetCommand(TerminalKeyGesture gesture, out TerminalReadLineCommand command)
        => _bindings.TryGetValue(gesture, out command);

    internal bool TryGetCommandWithShiftFallback(TerminalKeyGesture gesture, out TerminalReadLineCommand command)
    {
        if (_bindings.TryGetValue(gesture, out command))
        {
            return true;
        }

        if (gesture.Modifiers.HasFlag(TerminalModifiers.Shift))
        {
            var withoutShift = gesture with { Modifiers = gesture.Modifiers & ~TerminalModifiers.Shift };
            if (_bindings.TryGetValue(withoutShift, out command))
            {
                return true;
            }
        }

        command = TerminalReadLineCommand.None;
        return false;
    }
}
