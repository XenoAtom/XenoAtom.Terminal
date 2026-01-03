// Copyright (c) Alexandre Mutel. All rights reserved.
// Licensed under the BSD-Clause 2 license.
// See license.txt file in the project root for full license information.

using System.Text;
using XenoAtom.Ansi;
using XenoAtom.Ansi.Tokens;

namespace XenoAtom.Terminal.Internal;

internal sealed class VtInputDecoder : IDisposable
{
    private readonly AnsiTokenizer _tokenizer;
    private readonly List<AnsiToken> _tokens;
    private readonly StringBuilder _pasteBuilder;
    private bool _isInPaste;

    public VtInputDecoder()
    {
        _tokenizer = new AnsiTokenizer();
        _tokens = new List<AnsiToken>(32);
        _pasteBuilder = new StringBuilder(256);
    }

    public void Decode(ReadOnlySpan<char> chunk, bool isFinalChunk, TerminalInputOptions? options, TerminalEventBroadcaster events)
    {
        ArgumentNullException.ThrowIfNull(events);

        _tokens.Clear();
        _tokenizer.Tokenize(chunk, isFinalChunk, _tokens);

        var mouseEnabled = options?.EnableMouseEvents == true;
        var captureCtrlC = options?.CaptureCtrlC == true && options.TreatControlCAsInput != true;

        foreach (var token in _tokens)
        {
            if (_isInPaste)
            {
                if (token is CsiToken csi && IsBracketedPasteEnd(csi))
                {
                    _isInPaste = false;
                    events.Publish(new TerminalPasteEvent { Text = _pasteBuilder.ToString() });
                    _pasteBuilder.Clear();
                    continue;
                }

                AppendRawToPaste(token);
                continue;
            }

            if (token is CsiToken pasteStart && IsBracketedPasteStart(pasteStart))
            {
                _isInPaste = true;
                _pasteBuilder.Clear();
                continue;
            }

            if (mouseEnabled && token is CsiToken csiMouse && csiMouse.TryGetSgrMouseEvent(out var mouseEvent))
            {
                events.Publish(MapMouse(mouseEvent));
                continue;
            }

            if (token is ControlToken control)
            {
                if (captureCtrlC && control.Control == '\x03')
                {
                    events.Publish(new TerminalSignalEvent { Kind = TerminalSignalKind.Interrupt });
                }

                if (TryMapControlKey(control, out var controlKey))
                {
                    PublishKey(controlKey, events);
                }
                continue;
            }

            if (token.TryGetKeyEvent(out var ansiKeyEvent))
            {
                PublishKey(MapKey(ansiKeyEvent), events);
                continue;
            }

            if (token is EscToken esc && esc.Intermediates.Length == 0)
            {
                // Commonly used for Alt+<key> (e.g. ESC a).
                PublishKey(new TerminalKeyEvent
                {
                    Key = TerminalKey.Unknown,
                    Char = esc.Final,
                    Modifiers = TerminalModifiers.Alt,
                }, events);
                continue;
            }

            if (token is TextToken text)
            {
                PublishText(text.Text, events, captureCtrlC);
                continue;
            }
        }
    }

    public void Dispose()
    {
        _tokenizer.Dispose();
    }

    private static bool TryMapControlKey(ControlToken token, out TerminalKeyEvent ev)
    {
        ev = null!;

        switch (token.Control)
        {
            case '\t':
                ev = new TerminalKeyEvent { Key = TerminalKey.Tab, Char = '\t' };
                return true;
            case '\b':
                ev = new TerminalKeyEvent { Key = TerminalKey.Backspace, Char = '\b' };
                return true;
            case '\r':
                ev = new TerminalKeyEvent { Key = TerminalKey.Enter, Char = '\r' };
                return true;
            case '\n':
                ev = new TerminalKeyEvent { Key = TerminalKey.Enter, Char = '\n' };
                return true;
        }

        // Common Ctrl+A..Z range.
        if (token.Control is >= '\x01' and <= '\x1A')
        {
            ev = new TerminalKeyEvent
            {
                Key = TerminalKey.Unknown,
                Char = token.Control,
                Modifiers = TerminalModifiers.Ctrl,
            };
            return true;
        }

        return false;
    }

    private static TerminalMouseEvent MapMouse(AnsiMouseEvent mouseEvent)
    {
        var modifiers = MapModifiers(mouseEvent.Modifiers);
        var button = mouseEvent.Button switch
        {
            AnsiMouseButton.Left => TerminalMouseButton.Left,
            AnsiMouseButton.Middle => TerminalMouseButton.Middle,
            AnsiMouseButton.Right => TerminalMouseButton.Right,
            _ => TerminalMouseButton.None,
        };

        var kind = mouseEvent.Action switch
        {
            AnsiMouseAction.Press => TerminalMouseKind.Down,
            AnsiMouseAction.Release => TerminalMouseKind.Up,
            AnsiMouseAction.Wheel => TerminalMouseKind.Wheel,
            _ => button == TerminalMouseButton.None ? TerminalMouseKind.Move : TerminalMouseKind.Drag,
        };

        return new TerminalMouseEvent
        {
            X = mouseEvent.X - 1,
            Y = mouseEvent.Y - 1,
            Button = button,
            Kind = kind,
            Modifiers = modifiers,
            WheelDelta = mouseEvent.WheelDelta,
        };
    }

    private static TerminalKeyEvent MapKey(AnsiKeyEvent keyEvent)
    {
        var mods = MapModifiers(keyEvent.Modifiers);

        var key = keyEvent.Key switch
        {
            AnsiKey.Up => TerminalKey.Up,
            AnsiKey.Down => TerminalKey.Down,
            AnsiKey.Left => TerminalKey.Left,
            AnsiKey.Right => TerminalKey.Right,
            AnsiKey.Home => TerminalKey.Home,
            AnsiKey.End => TerminalKey.End,
            AnsiKey.Insert => TerminalKey.Insert,
            AnsiKey.Delete => TerminalKey.Delete,
            AnsiKey.PageUp => TerminalKey.PageUp,
            AnsiKey.PageDown => TerminalKey.PageDown,
            AnsiKey.Enter => TerminalKey.Enter,
            AnsiKey.Escape => TerminalKey.Escape,
            AnsiKey.Tab => TerminalKey.Tab,
            AnsiKey.Backspace => TerminalKey.Backspace,
            AnsiKey.F1 => TerminalKey.F1,
            AnsiKey.F2 => TerminalKey.F2,
            AnsiKey.F3 => TerminalKey.F3,
            AnsiKey.F4 => TerminalKey.F4,
            AnsiKey.F5 => TerminalKey.F5,
            AnsiKey.F6 => TerminalKey.F6,
            AnsiKey.F7 => TerminalKey.F7,
            AnsiKey.F8 => TerminalKey.F8,
            AnsiKey.F9 => TerminalKey.F9,
            AnsiKey.F10 => TerminalKey.F10,
            AnsiKey.F11 => TerminalKey.F11,
            AnsiKey.F12 => TerminalKey.F12,
            AnsiKey.BackTab => TerminalKey.Tab,
            _ => TerminalKey.Unknown,
        };

        if (keyEvent.Key == AnsiKey.BackTab)
        {
            mods |= TerminalModifiers.Shift;
        }

        char? ch = null;
        if (key == TerminalKey.Tab) ch = '\t';
        if (key == TerminalKey.Enter) ch = '\r';
        if (key == TerminalKey.Backspace) ch = '\b';
        if (key == TerminalKey.Escape) ch = '\x1b';

        return new TerminalKeyEvent
        {
            Key = key,
            Char = ch,
            Modifiers = mods,
        };
    }

    private static TerminalModifiers MapModifiers(AnsiKeyModifiers modifiers)
    {
        TerminalModifiers result = TerminalModifiers.None;
        if (modifiers.HasFlag(AnsiKeyModifiers.Shift)) result |= TerminalModifiers.Shift;
        if (modifiers.HasFlag(AnsiKeyModifiers.Control)) result |= TerminalModifiers.Ctrl;
        if (modifiers.HasFlag(AnsiKeyModifiers.Alt)) result |= TerminalModifiers.Alt;
        return result;
    }

    private void PublishText(string text, TerminalEventBroadcaster events, bool captureCtrlC)
    {
        if (string.IsNullOrEmpty(text))
        {
            return;
        }

        foreach (var ch in text)
        {
            if (captureCtrlC && ch == '\x03')
            {
                events.Publish(new TerminalSignalEvent { Kind = TerminalSignalKind.Interrupt });
            }

            var modifiers = ch is >= '\x01' and <= '\x1A' ? TerminalModifiers.Ctrl : TerminalModifiers.None;
            PublishKey(new TerminalKeyEvent
            {
                Key = TerminalKey.Unknown,
                Char = ch,
                Modifiers = modifiers,
            }, events);
        }

        var hasPrintable = false;
        foreach (var ch in text)
        {
            if (!char.IsControl(ch))
            {
                hasPrintable = true;
                break;
            }
        }

        if (hasPrintable)
        {
            events.Publish(new TerminalTextEvent { Text = text });
        }
    }

    private void PublishKey(TerminalKeyEvent key, TerminalEventBroadcaster events)
    {
        events.Publish(key);
    }

    private static bool IsBracketedPasteStart(CsiToken token)
    {
        if (token.Final != '~' || token.PrivateMarker is not null || token.Intermediates.Length != 0)
        {
            return false;
        }

        return token.Parameters.Length == 1 && token.Parameters[0] == 200;
    }

    private static bool IsBracketedPasteEnd(CsiToken token)
    {
        if (token.Final != '~' || token.PrivateMarker is not null || token.Intermediates.Length != 0)
        {
            return false;
        }

        return token.Parameters.Length == 1 && token.Parameters[0] == 201;
    }

    private void AppendRawToPaste(AnsiToken token)
    {
        switch (token)
        {
            case TextToken text:
                _pasteBuilder.Append(text.Text);
                break;
            case ControlToken control:
                _pasteBuilder.Append(control.Control);
                break;
            case UnknownEscapeToken unknown:
                _pasteBuilder.Append(unknown.Raw);
                break;
            case CsiToken csi:
                _pasteBuilder.Append(csi.Raw ?? ReconstructCsi(csi));
                break;
            case Ss3Token ss3:
                _pasteBuilder.Append(ss3.Raw ?? $"\x1bO{ss3.Final}");
                break;
            case EscToken esc:
                _pasteBuilder.Append(esc.Raw ?? $"\x1b{esc.Intermediates}{esc.Final}");
                break;
            case OscToken osc:
                _pasteBuilder.Append(osc.Raw ?? ReconstructOsc(osc));
                break;
            case SgrToken sgr:
                if (!string.IsNullOrEmpty(sgr.Raw))
                {
                    _pasteBuilder.Append(sgr.Raw);
                }
                break;
        }
    }

    private static string ReconstructCsi(CsiToken token)
    {
        var sb = new StringBuilder(16);
        sb.Append("\x1b[");
        if (token.PrivateMarker is { } pm)
        {
            sb.Append(pm);
        }
        for (var i = 0; i < token.Parameters.Length; i++)
        {
            if (i > 0) sb.Append(';');
            sb.Append(token.Parameters[i]);
        }
        sb.Append(token.Intermediates);
        sb.Append(token.Final);
        return sb.ToString();
    }

    private static string ReconstructOsc(OscToken token)
    {
        // OSC: ESC ] code ; data BEL
        // Use BEL termination for reconstruction.
        var sb = new StringBuilder(token.Data.Length + 16);
        sb.Append("\x1b]");
        sb.Append(token.Code);
        sb.Append(';');
        sb.Append(token.Data);
        sb.Append('\x07');
        return sb.ToString();
    }
}
