using XenoAtom.Terminal;

namespace HelloTerminal;

public static class Program
{
    public static async Task Main()
    {
        using var _session = Terminal.Open(options: new TerminalOptions
        {
            PreferUtf8Output = true,
            Prefer7BitC1 = true,
            ForceAnsi = false,
            StrictMode = false,
        });

        using var _title = Terminal.UseTitle("HelloTerminal - XenoAtom.Terminal");
        using var _alt = Terminal.UseAlternateScreen();

        {
            using var _cursor = Terminal.HideCursor();
            using var _mouse = Terminal.EnableMouseInput(TerminalMouseMode.Move);

            Terminal.Clear();

            Terminal.WriteMarkup("[bold green]HelloTerminal[/]");
            Terminal.WriteLine();
            Terminal.WriteLine("Press Esc to exit (Ctrl+C also exits).");
            Terminal.WriteLine("Try: typing keys, moving/clicking the mouse, scrolling the wheel, resizing the window.");
            Terminal.WriteLine();
            Terminal.WriteLine($"IsInteractive: {Terminal.IsInteractive}");
            Terminal.WriteLine($"Capabilities: Ansi={Terminal.Capabilities.AnsiEnabled}, Color={Terminal.Capabilities.ColorLevel}, Mouse={Terminal.Capabilities.SupportsMouse}, Raw={Terminal.Capabilities.SupportsRawMode}");
            Terminal.WriteLine($"Size: {Terminal.Size.Columns}x{Terminal.Size.Rows}");
            Terminal.WriteLine();

            Terminal.WriteLine("Events:");

            try
            {
                await foreach (var ev in Terminal.ReadEventsAsync())
                {
                    Terminal.WriteAtomic((TextWriter w) => w.WriteLine(FormatEvent(ev)));

                    if (ev is TerminalKeyEvent { Key: TerminalKey.Escape })
                    {
                        break;
                    }

                    if (ev is TerminalSignalEvent { Kind: TerminalSignalKind.Interrupt or TerminalSignalKind.Break })
                    {
                        break;
                    }
                }
            }
            catch (OperationCanceledException)
            {
            }
            finally
            {
                Terminal.WriteLine();
                Terminal.WriteLine("Bye.");
            }
        }

        Console.WriteLine("Press Enter to exit");
        _ = Console.ReadLine();
    }

    private static string FormatEvent(TerminalEvent ev)
    {
        return ev switch
        {
            TerminalKeyEvent key => $"[Key] Key={key.Key} Char={FormatChar(key.Char)} Mods={FormatMods(key.Modifiers)}",
            TerminalMouseEvent mouse => $"[Mouse] Kind={mouse.Kind} Button={mouse.Button} X={mouse.X} Y={mouse.Y} Mods={FormatMods(mouse.Modifiers)} WheelDelta={mouse.WheelDelta}",
            TerminalResizeEvent resize => $"[Resize] {resize.Size.Columns}x{resize.Size.Rows}",
            TerminalPasteEvent paste => $"[Paste] \"{paste.Text}\"",
            TerminalTextEvent text => $"[Text] \"{text.Text}\" (len={text.Text.Length})",
            TerminalSignalEvent signal => $"[Signal] {signal.Kind}",
            _ => $"[{ev.GetType().Name}]",
        };
    }

    private static string FormatChar(char? ch)
    {
        if (ch is null)
        {
            return "null";
        }

        var c = ch.Value;

        // Never print control characters directly (they can move the cursor / beep / etc.).
        if (char.IsControl(c))
        {
            return c switch
            {
                '\0' => "\\0 (U+0000)",
                '\a' => "\\a (BEL, U+0007)",
                '\b' => "\\b (BS, U+0008)",
                '\t' => "\\t (TAB, U+0009)",
                '\n' => "\\n (LF, U+000A)",
                '\r' => "\\r (CR, U+000D)",
                '\x1b' => "\\e (ESC, U+001B)",
                '\x7f' => "DEL (U+007F)",
                _ when c <= TerminalChar.CtrlZ => $"^{(char)('@' + c)} (U+{(int)c:X4})",
                _ => $"CTRL (U+{(int)c:X4})",
            };
        }

        if (char.IsSurrogate(c))
        {
            return $"SURROGATE (U+{(int)c:X4})";
        }

        return $"'{c}' (U+{(int)c:X4})";
    }

    private static string FormatMods(TerminalModifiers mods)
    {
        if (mods == TerminalModifiers.None)
        {
            return "None";
        }

        var parts = new List<string>(4);
        if (mods.HasFlag(TerminalModifiers.Ctrl)) parts.Add("Ctrl");
        if (mods.HasFlag(TerminalModifiers.Shift)) parts.Add("Shift");
        if (mods.HasFlag(TerminalModifiers.Alt)) parts.Add("Alt");
        if (mods.HasFlag(TerminalModifiers.Meta)) parts.Add("Meta");
        return string.Join('+', parts);
    }
}
