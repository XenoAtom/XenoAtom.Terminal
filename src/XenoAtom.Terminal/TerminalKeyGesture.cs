// Copyright (c) Alexandre Mutel. All rights reserved.
// Licensed under the BSD-Clause 2 license.
// See license.txt file in the project root for full license information.

using System.Text;

namespace XenoAtom.Terminal;

/// <summary>
/// Represents a key gesture for binding editor commands (key + optional char + modifiers).
/// </summary>
public readonly record struct TerminalKeyGesture(TerminalKey Key, char? Char, TerminalModifiers Modifiers)
{
    private const string ModCtrl = "CTRL";
    private const string ModShift = "SHIFT";
    private const string ModAlt = "ALT";
    private const string ModMeta = "META";

    /// <summary>
    /// Creates a gesture from a key event.
    /// </summary>
    public static TerminalKeyGesture From(TerminalKeyEvent key)
    {
        ArgumentNullException.ThrowIfNull(key);
        return new TerminalKeyGesture(key.Key, key.Char, key.Modifiers);
    }

    /// <summary>
    /// Parses a textual gesture representation (e.g. CTRL+R, ALT+b, backspace).
    /// </summary>
    public static TerminalKeyGesture Parse(ReadOnlySpan<char> text)
    {
        if (TryParse(text, out var gesture))
        {
            return gesture;
        }

        throw new FormatException($"Invalid terminal key gesture: '{text}'");
    }

    /// <summary>
    /// Tries to parse a textual gesture representation (e.g. CTRL+R, ALT+b, backspace).
    /// </summary>
    public static bool TryParse(ReadOnlySpan<char> text, out TerminalKeyGesture gesture)
    {
        gesture = default;
        if (text .IsEmpty || text.IsWhiteSpace())
        {
            return false;
        }

        var modifiers = TerminalModifiers.None;
        var index = 0;
        var hasToken = false;
        ReadOnlySpan<char> lastToken = default;

        while (TryReadToken(text, index, out var token, out index))
        {
            if (hasToken)
            {
                if (!TryParseModifier(lastToken, out var mod))
                {
                    return false;
                }

                modifiers |= mod;
            }

            hasToken = true;
            lastToken = token;
        }

        if (!hasToken)
        {
            return false;
        }

        if (!TryParseKeyToken(lastToken, modifiers, out var key, out var ch))
        {
            return false;
        }

        gesture = new TerminalKeyGesture(key, ch, modifiers);
        return true;
    }

    /// <summary>
    /// Formats this gesture as a textual representation (e.g. CTRL+R, ALT+b, backspace).
    /// </summary>
    public override string ToString()
    {
        var keyToken = GetKeyToken();
        if (Modifiers == TerminalModifiers.None)
        {
            return keyToken;
        }

        var sb = new StringBuilder();
        AppendModifier(sb, Modifiers, TerminalModifiers.Ctrl, ModCtrl);
        AppendModifier(sb, Modifiers, TerminalModifiers.Shift, ModShift);
        AppendModifier(sb, Modifiers, TerminalModifiers.Alt, ModAlt);
        AppendModifier(sb, Modifiers, TerminalModifiers.Meta, ModMeta);

        if (sb.Length > 0)
        {
            sb.Append('+');
        }
        sb.Append(keyToken);
        return sb.ToString();
    }

    private string GetKeyToken()
    {
        if (Key != TerminalKey.Unknown)
        {
            return FormatKey(Key);
        }

        if (Char is { } ch)
        {
            if (TryGetCtrlLetter(ch, out var letter))
            {
                return letter.ToString();
            }

            return ch.ToString();
        }

        return "unknown";
    }

    private static void AppendModifier(StringBuilder sb, TerminalModifiers modifiers, TerminalModifiers flag, string text)
    {
        if (!modifiers.HasFlag(flag))
        {
            return;
        }

        if (sb.Length > 0)
        {
            sb.Append('+');
        }

        sb.Append(text);
    }

    private static bool TryParseModifier(ReadOnlySpan<char> token, out TerminalModifiers modifier)
    {
        modifier = TerminalModifiers.None;
        if (token.Equals(ModCtrl.AsSpan(), StringComparison.OrdinalIgnoreCase)
            || token.Equals("CONTROL".AsSpan(), StringComparison.OrdinalIgnoreCase))
        {
            modifier = TerminalModifiers.Ctrl;
            return true;
        }

        if (token.Equals(ModShift.AsSpan(), StringComparison.OrdinalIgnoreCase))
        {
            modifier = TerminalModifiers.Shift;
            return true;
        }

        if (token.Equals(ModAlt.AsSpan(), StringComparison.OrdinalIgnoreCase))
        {
            modifier = TerminalModifiers.Alt;
            return true;
        }

        if (token.Equals(ModMeta.AsSpan(), StringComparison.OrdinalIgnoreCase)
            || token.Equals("SUPER".AsSpan(), StringComparison.OrdinalIgnoreCase)
            || token.Equals("WIN".AsSpan(), StringComparison.OrdinalIgnoreCase))
        {
            modifier = TerminalModifiers.Meta;
            return true;
        }

        return false;
    }

    private static bool TryParseKeyToken(ReadOnlySpan<char> token, TerminalModifiers modifiers, out TerminalKey key, out char? ch)
    {
        key = TerminalKey.Unknown;
        ch = null;
        if (token.Length == 0)
        {
            return false;
        }

        if (TryParseKeyName(token, out key))
        {
            return true;
        }

        if (token.Length == 1)
        {
            var c = token[0];
            if (modifiers.HasFlag(TerminalModifiers.Ctrl) && char.IsAsciiLetter(c))
            {
                ch = TerminalChar.Ctrl(c);
            }
            else
            {
                ch = c;
            }
            return true;
        }

        return false;
    }

    private static bool TryParseKeyName(ReadOnlySpan<char> token, out TerminalKey key)
    {
        key = TerminalKey.Unknown;
        if (token.Length == 0)
        {
            return false;
        }

        if (TryParseFunctionKey(token, out key))
        {
            return true;
        }

        if (token.Equals("enter".AsSpan(), StringComparison.OrdinalIgnoreCase)
            || token.Equals("return".AsSpan(), StringComparison.OrdinalIgnoreCase))
        {
            key = TerminalKey.Enter;
            return true;
        }

        if (token.Equals("escape".AsSpan(), StringComparison.OrdinalIgnoreCase)
            || token.Equals("esc".AsSpan(), StringComparison.OrdinalIgnoreCase))
        {
            key = TerminalKey.Escape;
            return true;
        }

        if (token.Equals("backspace".AsSpan(), StringComparison.OrdinalIgnoreCase)
            || token.Equals("bksp".AsSpan(), StringComparison.OrdinalIgnoreCase)
            || token.Equals("bs".AsSpan(), StringComparison.OrdinalIgnoreCase))
        {
            key = TerminalKey.Backspace;
            return true;
        }

        if (token.Equals("tab".AsSpan(), StringComparison.OrdinalIgnoreCase))
        {
            key = TerminalKey.Tab;
            return true;
        }

        if (token.Equals("space".AsSpan(), StringComparison.OrdinalIgnoreCase))
        {
            key = TerminalKey.Space;
            return true;
        }

        if (token.Equals("up".AsSpan(), StringComparison.OrdinalIgnoreCase))
        {
            key = TerminalKey.Up;
            return true;
        }

        if (token.Equals("down".AsSpan(), StringComparison.OrdinalIgnoreCase))
        {
            key = TerminalKey.Down;
            return true;
        }

        if (token.Equals("left".AsSpan(), StringComparison.OrdinalIgnoreCase))
        {
            key = TerminalKey.Left;
            return true;
        }

        if (token.Equals("right".AsSpan(), StringComparison.OrdinalIgnoreCase))
        {
            key = TerminalKey.Right;
            return true;
        }

        if (token.Equals("home".AsSpan(), StringComparison.OrdinalIgnoreCase))
        {
            key = TerminalKey.Home;
            return true;
        }

        if (token.Equals("end".AsSpan(), StringComparison.OrdinalIgnoreCase))
        {
            key = TerminalKey.End;
            return true;
        }

        if (token.Equals("pageup".AsSpan(), StringComparison.OrdinalIgnoreCase)
            || token.Equals("pgup".AsSpan(), StringComparison.OrdinalIgnoreCase))
        {
            key = TerminalKey.PageUp;
            return true;
        }

        if (token.Equals("pagedown".AsSpan(), StringComparison.OrdinalIgnoreCase)
            || token.Equals("pgdn".AsSpan(), StringComparison.OrdinalIgnoreCase))
        {
            key = TerminalKey.PageDown;
            return true;
        }

        if (token.Equals("insert".AsSpan(), StringComparison.OrdinalIgnoreCase)
            || token.Equals("ins".AsSpan(), StringComparison.OrdinalIgnoreCase))
        {
            key = TerminalKey.Insert;
            return true;
        }

        if (token.Equals("delete".AsSpan(), StringComparison.OrdinalIgnoreCase)
            || token.Equals("del".AsSpan(), StringComparison.OrdinalIgnoreCase))
        {
            key = TerminalKey.Delete;
            return true;
        }

        if (token.Equals("unknown".AsSpan(), StringComparison.OrdinalIgnoreCase))
        {
            key = TerminalKey.Unknown;
            return true;
        }

        return false;
    }

    private static bool TryParseFunctionKey(ReadOnlySpan<char> token, out TerminalKey key)
    {
        key = TerminalKey.Unknown;
        if (token.Length < 2 || (token[0] != 'f' && token[0] != 'F'))
        {
            return false;
        }

        if (!int.TryParse(token[1..], out var index))
        {
            return false;
        }

        key = index switch
        {
            1 => TerminalKey.F1,
            2 => TerminalKey.F2,
            3 => TerminalKey.F3,
            4 => TerminalKey.F4,
            5 => TerminalKey.F5,
            6 => TerminalKey.F6,
            7 => TerminalKey.F7,
            8 => TerminalKey.F8,
            9 => TerminalKey.F9,
            10 => TerminalKey.F10,
            11 => TerminalKey.F11,
            12 => TerminalKey.F12,
            _ => TerminalKey.Unknown,
        };

        return key != TerminalKey.Unknown;
    }

    private static string FormatKey(TerminalKey key)
        => key switch
        {
            TerminalKey.Enter => "enter",
            TerminalKey.Escape => "escape",
            TerminalKey.Backspace => "backspace",
            TerminalKey.Tab => "tab",
            TerminalKey.Space => "space",
            TerminalKey.Up => "up",
            TerminalKey.Down => "down",
            TerminalKey.Left => "left",
            TerminalKey.Right => "right",
            TerminalKey.Home => "home",
            TerminalKey.End => "end",
            TerminalKey.PageUp => "pageup",
            TerminalKey.PageDown => "pagedown",
            TerminalKey.Insert => "insert",
            TerminalKey.Delete => "delete",
            TerminalKey.F1 => "f1",
            TerminalKey.F2 => "f2",
            TerminalKey.F3 => "f3",
            TerminalKey.F4 => "f4",
            TerminalKey.F5 => "f5",
            TerminalKey.F6 => "f6",
            TerminalKey.F7 => "f7",
            TerminalKey.F8 => "f8",
            TerminalKey.F9 => "f9",
            TerminalKey.F10 => "f10",
            TerminalKey.F11 => "f11",
            TerminalKey.F12 => "f12",
            _ => "unknown",
        };

    private static bool TryGetCtrlLetter(char ch, out char letter)
    {
        if (ch is >= TerminalChar.CtrlA and <= TerminalChar.CtrlZ)
        {
            letter = (char)('A' + (ch - TerminalChar.CtrlA));
            return true;
        }

        letter = default;
        return false;
    }

    private static bool TryReadToken(ReadOnlySpan<char> text, int startIndex, out ReadOnlySpan<char> token, out int nextIndex)
    {
        token = default;
        nextIndex = startIndex;
        var length = text.Length;
        var index = startIndex;

        while (index < length)
        {
            while (index < length && (text[index] == '+' || char.IsWhiteSpace(text[index])))
            {
                index++;
            }

            if (index >= length)
            {
                nextIndex = index;
                return false;
            }

            var start = index;
            while (index < length && text[index] != '+')
            {
                index++;
            }

            var slice = text[start..index].Trim();
            if (slice.Length == 0)
            {
                continue;
            }

            token = slice;
            nextIndex = index;
            return true;
        }

        nextIndex = index;
        return false;
    }
}
