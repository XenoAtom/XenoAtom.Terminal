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

    public void Decode(ReadOnlySpan<char> chunk, bool isFinalChunk, TerminalInputOptions? options, TerminalEventBroadcaster events, Func<AnsiCursorPosition, bool>? cursorPositionReport = null, TerminalGraphicsProbeCoordinator? graphicsProbeCoordinator = null, TerminalKeyboardProbeCoordinator? keyboardProbeCoordinator = null)
    {
        ArgumentNullException.ThrowIfNull(events);

        _tokens.Clear();
        _tokenizer.Tokenize(chunk, isFinalChunk, _tokens);

        var mouseEnabled = options?.EnableMouseEvents == true;
        var captureCtrlC = options?.CaptureCtrlC == true && options.TreatControlCAsInput != true;

        foreach (var token in _tokens)
        {
            if (graphicsProbeCoordinator?.TryConsume(token) == true)
            {
                continue;
            }

            if (token is CsiToken csiKeyboardProbe && keyboardProbeCoordinator?.TryConsume(csiKeyboardProbe) == true)
            {
                continue;
            }

            if (token is CsiToken csiReport
                && csiReport.TryGetCursorPositionReport(out var cursorPosition)
                && cursorPositionReport?.Invoke(cursorPosition) == true)
            {
                continue;
            }

            if (_isInPaste)
            {
                if (token is CsiToken csi && IsBracketedPasteEnd(csi))
                {
                    _isInPaste = false;
                    events.Publish(new TerminalPasteEvent { Text = NormalizePasteTextLineEndings(_pasteBuilder.ToString()) });
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

            if (token is CsiToken csiKittyKeyboard && TryMapKittyKeyboardKey(csiKittyKeyboard, out var kittyKey, out var kittyText, out var isKittyCtrlC))
            {
                if (captureCtrlC && isKittyCtrlC)
                {
                    events.Publish(new TerminalSignalEvent { Kind = TerminalSignalKind.Interrupt });
                }

                PublishKey(kittyKey, events);
                if (!string.IsNullOrEmpty(kittyText) && HasPrintable(kittyText))
                {
                    events.Publish(new TerminalTextEvent { Text = kittyText });
                }

                continue;
            }

            if (token is CsiToken csiFunctionKey && TryMapModifiedFunctionKey(csiFunctionKey, out var functionKey))
            {
                PublishKey(functionKey, events);
                continue;
            }

            if (token is ControlToken control)
            {
                if (captureCtrlC && control.Control == TerminalChar.CtrlC)
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
        if (token.Control is >= TerminalChar.CtrlA and <= TerminalChar.CtrlZ)
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

    private static bool TryMapModifiedFunctionKey(CsiToken token, out TerminalKeyEvent ev)
    {
        ev = null!;

        if (token.PrivateMarker is not null || token.Intermediates.Length != 0 || token.Parameters.Length != 2 || token.Parameters[0] != 1)
        {
            return false;
        }

        var key = token.Final switch
        {
            'P' => TerminalKey.F1,
            'Q' => TerminalKey.F2,
            'R' => TerminalKey.F3,
            'S' => TerminalKey.F4,
            _ => TerminalKey.Unknown,
        };

        if (key == TerminalKey.Unknown || !TryMapXtermModifierParameter(token.Parameters[1], out var modifiers))
        {
            return false;
        }

        ev = new TerminalKeyEvent
        {
            Key = key,
            Modifiers = modifiers,
        };
        return true;
    }

    private static bool TryMapXtermModifierParameter(int modifierParameter, out TerminalModifiers modifiers)
    {
        modifiers = TerminalModifiers.None;

        // Xterm-style CSI key modifier parameter: 1 + Shift(1) + Alt(2) + Ctrl(4).
        if (modifierParameter is < 2 or > 8)
        {
            return false;
        }

        var bits = modifierParameter - 1;
        if ((bits & 1) != 0) modifiers |= TerminalModifiers.Shift;
        if ((bits & 2) != 0) modifiers |= TerminalModifiers.Alt;
        if ((bits & 4) != 0) modifiers |= TerminalModifiers.Ctrl;
        return true;
    }

    private static bool TryMapKittyKeyboardKey(CsiToken token, out TerminalKeyEvent ev, out string? text, out bool isCtrlC)
    {
        ev = null!;
        text = null;
        isCtrlC = false;

        if (token.Final != 'u' || token.PrivateMarker is not null || token.Intermediates.Length != 0 || token.Parameters.Length == 0)
        {
            return false;
        }

        var keyCode = token.Parameters[0];
        if (keyCode <= 0 || IsKittyStandaloneModifierKeyCode(keyCode))
        {
            return false;
        }

        var modifiers = TerminalModifiers.None;
        if (token.Parameters.Length >= 2)
        {
            if (!TryMapKittyModifierParameter(token.Parameters[1], out modifiers))
            {
                return false;
            }
        }

        if (token.Parameters.Length >= 3)
        {
            text = DecodeCodepoints(token.Parameters.AsSpan(2));
        }

        var key = MapKittyKeyCode(keyCode);
        var ch = GetKittyKeyChar(keyCode, key, text);

        ev = new TerminalKeyEvent
        {
            Key = key,
            Char = ch,
            Modifiers = modifiers,
        };
        isCtrlC = keyCode is 99 or 67 && modifiers.HasFlag(TerminalModifiers.Ctrl);
        return true;
    }

    private static bool TryMapKittyModifierParameter(int modifierParameter, out TerminalModifiers modifiers)
    {
        modifiers = TerminalModifiers.None;

        if (modifierParameter < 1)
        {
            return false;
        }

        var bits = modifierParameter - 1;
        if ((bits & 1) != 0) modifiers |= TerminalModifiers.Shift;
        if ((bits & 2) != 0) modifiers |= TerminalModifiers.Alt;
        if ((bits & 4) != 0) modifiers |= TerminalModifiers.Ctrl;
        if ((bits & (8 | 16 | 32)) != 0) modifiers |= TerminalModifiers.Meta;
        return true;
    }

    private static TerminalKey MapKittyKeyCode(int keyCode) => keyCode switch
    {
        13 or 10 => TerminalKey.Enter,
        27 => TerminalKey.Escape,
        8 or 127 => TerminalKey.Backspace,
        9 => TerminalKey.Tab,
        32 => TerminalKey.Space,
        57414 => TerminalKey.Enter,
        57417 => TerminalKey.Left,
        57418 => TerminalKey.Right,
        57419 => TerminalKey.Up,
        57420 => TerminalKey.Down,
        57421 => TerminalKey.PageUp,
        57422 => TerminalKey.PageDown,
        57423 => TerminalKey.Home,
        57424 => TerminalKey.End,
        57425 => TerminalKey.Insert,
        57426 => TerminalKey.Delete,
        _ => TerminalKey.Unknown,
    };

    private static bool IsKittyStandaloneModifierKeyCode(int keyCode) => keyCode is >= 57441 and <= 57454;

    private static char? GetKittyKeyChar(int keyCode, TerminalKey key, string? text)
    {
        if (text is { Length: 1 } && !char.IsSurrogate(text[0]))
        {
            return text[0];
        }

        return key switch
        {
            TerminalKey.Enter => keyCode == 10 ? '\n' : '\r',
            TerminalKey.Escape => '\x1b',
            TerminalKey.Backspace => '\b',
            TerminalKey.Tab => '\t',
            TerminalKey.Space => ' ',
            _ => null,
        };
    }

    private static string DecodeCodepoints(ReadOnlySpan<int> codepoints)
    {
        var builder = new StringBuilder(codepoints.Length);
        foreach (var codepoint in codepoints)
        {
            if (codepoint is <= 0 or > 0x10FFFF or >= 0xD800 and <= 0xDFFF)
            {
                continue;
            }

            builder.Append(char.ConvertFromUtf32(codepoint));
        }

        return builder.ToString();
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
            if (captureCtrlC && ch == TerminalChar.CtrlC)
            {
                events.Publish(new TerminalSignalEvent { Kind = TerminalSignalKind.Interrupt });
            }

            var modifiers = ch is >= TerminalChar.CtrlA and <= TerminalChar.CtrlZ ? TerminalModifiers.Ctrl : TerminalModifiers.None;
            PublishKey(new TerminalKeyEvent
            {
                Key = TerminalKey.Unknown,
                Char = ch,
                Modifiers = modifiers,
            }, events);
        }

        if (HasPrintable(text))
        {
            events.Publish(new TerminalTextEvent { Text = text });
        }
    }

    private static bool HasPrintable(string text)
    {
        foreach (var ch in text)
        {
            if (!char.IsControl(ch))
            {
                return true;
            }
        }

        return false;
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

    private static string NormalizePasteTextLineEndings(string text)
    {
        var firstCarriageReturn = text.IndexOf('\r');
        if (firstCarriageReturn < 0)
        {
            return text;
        }

        var sb = new StringBuilder(text.Length);
        sb.Append(text.AsSpan(0, firstCarriageReturn));

        for (var i = firstCarriageReturn; i < text.Length; i++)
        {
            var ch = text[i];
            if (ch != '\r')
            {
                sb.Append(ch);
                continue;
            }

            sb.Append('\n');
            if (i + 1 < text.Length && text[i + 1] == '\n')
            {
                i++;
            }
        }

        return sb.ToString();
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
            case AnsiStringControlToken stringControl:
                _pasteBuilder.Append(stringControl.Raw ?? ReconstructStringControl(stringControl));
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

    private static string ReconstructStringControl(AnsiStringControlToken token)
    {
        var introducer = token.Kind switch
        {
            AnsiStringControlKind.Dcs => 'P',
            AnsiStringControlKind.Sos => 'X',
            AnsiStringControlKind.Pm => '^',
            AnsiStringControlKind.Apc => '_',
            _ => 'P',
        };

        return $"\x1b{introducer}{token.Data}\x1b\\";
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
