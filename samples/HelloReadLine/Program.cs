using System.Text;
using XenoAtom.Ansi;
using XenoAtom.Terminal;

namespace HelloReadLine;

/// <summary>
/// Sample program demonstrating `XenoAtom.Terminal`'s interactive ReadLine editor.
/// </summary>
/// <remarks>
/// This sample showcases:
/// <list type="bullet">
///   <item><description>Initializing the terminal with common output/ANSI preferences.</description></item>
///   <item><description>Starting/stopping terminal input with mouse + resize event support.</description></item>
///   <item><description>An interactive prompt loop using `Terminal.ReadLineAsync`.</description></item>
///   <item><description>Prompt customization (a line counter rendered with markup).</description></item>
///   <item><description>Cancelable input handling (`OperationCanceledException`) and EOF handling (`null`).</description></item>
///   <item><description>Command parsing and runtime toggling of editor options (e.g. view width, max length).</description></item>
///   <item><description>Tab completion for slash commands via `CompletionHandler`.</description></item>
///   <item><description>A custom key binding (Ctrl+O inserts a timestamp) via `KeyHandler`.</description></item>
///   <item><description>Undo/redo (Ctrl+Z/Ctrl+Y) and reverse incremental history search (Ctrl+R).</description></item>
///   <item><description>Customizable built-in bindings via `TerminalReadLineKeyBindings`.</description></item>
///   <item><description>A custom markup renderer that highlights selections, the current word, and keywords
///   (e.g. <c>error</c>, <c>warn</c>, <c>info</c>) while properly escaping markup brackets.</description></item>
///   <item><description>Writing output atomically to avoid interleaving with input rendering.</description></item>
/// </list>
/// Intended as a compact, end-to-end reference for integrating the ReadLine editor into a console app.
/// </remarks>
public static class Program
{
    private static readonly string[] Commands =
    [
        "/help",
        "/hello",
        "/helium",
        "/exit",
        "/clear",
        "/nonl",
        "/width",
        "/max",
    ];

    public static async Task Main()
    {
        using var _session = Terminal.Open(options: new TerminalOptions
        {
            PreferUtf8Output = true,
            Prefer7BitC1 = true,
            ForceAnsi = false,
            StrictMode = false,
        });

        using var _title = Terminal.UseTitle("HelloReadLine - XenoAtom.Terminal");

        ShowHelp();

        using var _mouse = Terminal.EnableMouseInput(TerminalMouseMode.Drag);

        var promptNumber = 1;
        var keyBindings = TerminalReadLineKeyBindings.CreateDefault();
        var options = new TerminalReadLineOptions
        {
            Echo = true,
            ViewWidth = 60,
            MaxLength = 512,
            EmitNewLineOnAccept = true,
            EnableEditing = true,
            EnableHistory = true,
            AddToHistory = true,
            EnableBracketedPaste = true,
            EnableMouseEditing = true,
            KeyBindings = keyBindings,
            KeyHandler = HandleKey,
            CompletionHandler = Complete,
            MarkupRenderer = RenderMarkup,
        };

        while (true)
        {
            var promptForThisLine = promptNumber;
            options.PromptMarkup = () => $"[gray]{promptForThisLine,3}[/] [cyan]>[/] ";

            string? line;
            try
            {
                line = await Terminal.ReadLineAsync(options);
            }
            catch (OperationCanceledException)
            {
                Terminal.WriteMarkupLine("[gray](canceled; press Enter to continue)[/]");
                continue;
            }

            if (line is null)
            {
                Terminal.WriteMarkupLine("[gray](EOF)[/]");
                break;
            }

            if (line.Length == 0)
            {
                promptNumber++;
                continue;
            }

            if (line.StartsWith("/", StringComparison.Ordinal))
            {
                promptNumber++;
                if (HandleCommand(line, options))
                {
                    break;
                }

                continue;
            }

            promptNumber++;
            Terminal.WriteAtomic(w =>
            {
                w.Foreground(ConsoleColor.DarkGray);
                w.Write("You typed: ");
                w.ResetStyle();
                w.Write(line);
                w.Write("\n");
            });
        }
    }

    private static void ShowHelp()
    {
        Terminal.WriteMarkup("""
                             [bold green]HelloReadLine[/] — interactive ReadLine editor demo

                             [bold]Try these features:[/]
                              - [cyan]Left/Right/Home/End[/] cursor movement, mid-line insert/delete
                              - [cyan]Up/Down[/] history (reusing the same options instance)
                              - [cyan]Shift+Left/Right[/] selection (and [cyan]Ctrl+Shift+Left/Right[/] by word when available)
                              - [cyan]Ctrl+Left/Right[/] word movement (often [cyan]Alt+Left/Right[/] on some terminals)
                              - [cyan]Ctrl+Backspace[/] / [cyan]Ctrl+Delete[/] word delete (when available)
                              - [cyan]Ctrl+Z/Ctrl+Y[/] undo/redo, [cyan]Ctrl+R[/] reverse search in history
                              - [cyan]Tab[/] completion for slash commands (type [gray]/he[/], then press Tab repeatedly to cycle between [gray]/help[/], [gray]/hello[/], [gray]/helium[/])
                              - [cyan]Ctrl+C/Ctrl+X/Ctrl+V[/] copy/cut/paste (Ctrl+C cancels when there is no selection)
                              - [cyan]Ctrl+O[/] inserts a timestamp via a custom key handler
                              - [cyan]Mouse click/drag[/] sets cursor and selection (when supported)
                              - Custom markup renderer highlights: selection + keywords [red]error[/], [yellow]warn[/], [green]info[/]
                              
                              [gray]Tip: when an app enables mouse reporting, terminal text selection usually stops working. Hold Shift while dragging to force terminal selection (varies by terminal).[/]

                             [bold]Commands[/]
                              - [cyan]/help[/]  show help
                              - [cyan]/hello[/] print a greeting (demo)
                              - [cyan]/helium[/] print a fake command (demo)
                              - [cyan]/exit[/]  exit the sample
                              - [cyan]/clear[/] clear the screen
                              - [cyan]/nonl[/]  toggle EmitNewLineOnAccept
                              - [cyan]/width N[/] set ViewWidth (cells), e.g. /width 20
                              - [cyan]/max N[/]   set MaxLength (UTF-16 code units), e.g. /max 80
                             
                             [gray]Type /help for commands. Type /exit to quit.[/]
                             """);
    }

    private static void HandleKey(TerminalReadLineController controller, TerminalKeyEvent key)
    {
        // Ctrl+O inserts a timestamp (example of a custom key binding).
        if (key.Key == TerminalKey.Unknown && key.Char == TerminalChar.CtrlO)
        {
            controller.Insert((DateTimeOffset.Now.ToString("HH:mm:ss.fff", System.Globalization.CultureInfo.InvariantCulture) + " ").AsSpan());
            return;
        }
    }

    private static TerminalReadLineCompletion Complete(ReadOnlySpan<char> text, int cursorIndex, int selectionStart, int selectionLength)
    {
        _ = selectionStart;
        _ = selectionLength;

        var tokenStart = cursorIndex;
        while (tokenStart > 0 && !char.IsWhiteSpace(text[tokenStart - 1]))
        {
            tokenStart--;
        }

        var token = text.Slice(tokenStart, cursorIndex - tokenStart);
        if (!token.StartsWith("/", StringComparison.Ordinal))
        {
            return default;
        }

        var candidates = new List<string>(4);
        for (var i = 0; i < Commands.Length; i++)
        {
            if (Commands[i].AsSpan().StartsWith(token, StringComparison.OrdinalIgnoreCase))
            {
                candidates.Add(Commands[i]);
            }
        }

        if (candidates.Count > 0)
        {
            return new TerminalReadLineCompletion
            {
                Handled = true,
                Candidates = candidates,
                ReplaceStart = tokenStart,
                ReplaceLength = cursorIndex - tokenStart,
            };
        }

        return default;
    }

    private static string RenderMarkup(ReadOnlySpan<char> text, int cursorIndex, int viewStart, int viewLength, int selectionStart, int selectionLength)
    {
        var view = text.Slice(viewStart, viewLength);

        var viewEnd = viewStart + viewLength;
        var selA = selectionLength > 0 ? Math.Clamp(selectionStart, viewStart, viewEnd) : viewEnd;
        var selB = selectionLength > 0 ? Math.Clamp(selectionStart + selectionLength, viewStart, viewEnd) : viewEnd;

        var wordA = TerminalTextUtility.GetWordStart(text, cursorIndex);
        var wordB = TerminalTextUtility.GetWordEnd(text, cursorIndex);
        var hasWord = wordA < wordB;
        if (hasWord)
        {
            wordA = Math.Clamp(wordA, viewStart, viewEnd);
            wordB = Math.Clamp(wordB, viewStart, viewEnd);
        }

        var boundaries = new List<int>(8) { 0, viewLength };
        if (selA < selB)
        {
            boundaries.Add(selA - viewStart);
            boundaries.Add(selB - viewStart);
        }

        if (hasWord && wordA < wordB)
        {
            boundaries.Add(wordA - viewStart);
            boundaries.Add(wordB - viewStart);
        }

        boundaries.Sort();
        for (var i = boundaries.Count - 2; i >= 0; i--)
        {
            if (boundaries[i] == boundaries[i + 1])
            {
                boundaries.RemoveAt(i + 1);
            }
        }

        var sb = new StringBuilder(viewLength + 64);
        for (var i = 0; i + 1 < boundaries.Count; i++)
        {
            var relStart = boundaries[i];
            var relEnd = boundaries[i + 1];
            if (relEnd <= relStart)
            {
                continue;
            }

            var globalStart = viewStart + relStart;
            var segment = view.Slice(relStart, relEnd - relStart);

            var inSelection = selA < selB && globalStart >= selA && globalStart < selB;
            var inWord = hasWord && wordA < wordB && globalStart >= wordA && globalStart < wordB;

            if (inSelection)
            {
                sb.Append("[black on brightyellow]");
            }

            if (inWord)
            {
                sb.Append("[underline]");
            }

            AppendWithKeywordHighlight(sb, text, segment, globalStart);

            if (inWord)
            {
                sb.Append("[/]");
            }

            if (inSelection)
            {
                sb.Append("[/]");
            }
        }

        return sb.ToString();
    }

    private static void AppendWithKeywordHighlight(StringBuilder sb, ReadOnlySpan<char> fullText, ReadOnlySpan<char> segment, int globalSegmentStart)
    {
        var i = 0;
        while (i < segment.Length)
        {
            if (TryMatchKeyword(fullText, globalSegmentStart + i, segment.Slice(i), out var keywordLength, out var tag))
            {
                sb.Append(tag);
                AppendEscaped(sb, segment.Slice(i, keywordLength));
                sb.Append("[/]");
                i += keywordLength;
                continue;
            }

            var next = i + 1;
            while (next < segment.Length)
            {
                if (TryMatchKeyword(fullText, globalSegmentStart + next, segment.Slice(next), out _, out _))
                {
                    break;
                }
                next++;
            }

            AppendEscaped(sb, segment.Slice(i, next - i));
            i = next;
        }
    }

    private static bool TryMatchKeyword(ReadOnlySpan<char> fullText, int globalIndex, ReadOnlySpan<char> remaining, out int length, out string tag)
    {
        length = 0;
        tag = string.Empty;

        if (!TerminalTextUtility.IsWordStart(fullText, globalIndex))
        {
            return false;
        }

        if (remaining.StartsWith("error", StringComparison.OrdinalIgnoreCase) && TerminalTextUtility.IsWordEnd(fullText, globalIndex + 5))
        {
            length = 5;
            tag = "[bold red]";
            return true;
        }

        if (remaining.StartsWith("warning", StringComparison.OrdinalIgnoreCase) && TerminalTextUtility.IsWordEnd(fullText, globalIndex + 7))
        {
            length = 7;
            tag = "[bold yellow]";
            return true;
        }

        if (remaining.StartsWith("warn", StringComparison.OrdinalIgnoreCase) && TerminalTextUtility.IsWordEnd(fullText, globalIndex + 4))
        {
            length = 4;
            tag = "[bold yellow]";
            return true;
        }

        if (remaining.StartsWith("info", StringComparison.OrdinalIgnoreCase) && TerminalTextUtility.IsWordEnd(fullText, globalIndex + 4))
        {
            length = 4;
            tag = "[bold green]";
            return true;
        }

        return false;
    }

    private static void AppendEscaped(StringBuilder sb, ReadOnlySpan<char> text) => sb.Append(AnsiMarkup.Escape(text));

    private static bool HandleCommand(string line, TerminalReadLineOptions options)
    {
        if (line.Equals("/exit", StringComparison.OrdinalIgnoreCase))
        {
            return true;
        }

        if (line.Equals("/help", StringComparison.OrdinalIgnoreCase))
        {
            ShowHelp();
            return false;
        }

        if (line.Equals("/hello", StringComparison.OrdinalIgnoreCase))
        {
            Terminal.WriteMarkupLine("[green]Hello![/] [gray](This is a fake command for completion cycling.)[/]");
            return false;
        }

        if (line.Equals("/helium", StringComparison.OrdinalIgnoreCase))
        {
            Terminal.WriteMarkupLine("[gray]He[/][cyan]Li[/][gray]Um[/] [cyan](Another fake command for completion cycling.)[/]");
            return false;
        }

        if (line.Equals("/clear", StringComparison.OrdinalIgnoreCase))
        {
            Terminal.Clear();
            return false;
        }

        if (line.Equals("/nonl", StringComparison.OrdinalIgnoreCase))
        {
            options.EmitNewLineOnAccept = !options.EmitNewLineOnAccept;
            Terminal.WriteMarkupLine($"[gray]EmitNewLineOnAccept = {options.EmitNewLineOnAccept}[/]");
            return false;
        }

        if (TryParseIntCommand(line, "/width", out var width))
        {
            options.ViewWidth = width <= 0 ? null : width;
            Terminal.WriteMarkupLine($"[gray]ViewWidth = {(options.ViewWidth?.ToString() ?? "(auto)")}[/]");
            return false;
        }

        if (TryParseIntCommand(line, "/max", out var max))
        {
            options.MaxLength = max <= 0 ? null : max;
            Terminal.WriteMarkupLine($"[gray]MaxLength = {(options.MaxLength?.ToString() ?? "(none)")}[/]");
            return false;
        }

        Terminal.WriteMarkupLine("[gray]Unknown command. Type /help.[/]");
        return false;
    }

    private static bool TryParseIntCommand(string line, string command, out int value)
    {
        value = 0;
        if (!line.StartsWith(command, StringComparison.OrdinalIgnoreCase))
        {
            return false;
        }

        var rest = line.AsSpan(command.Length).Trim();
        return int.TryParse(rest, System.Globalization.NumberStyles.Integer, System.Globalization.CultureInfo.InvariantCulture, out value);
    }
}
