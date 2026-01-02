# XenoAtom.Terminal User Guide

XenoAtom.Terminal is a modern replacement for `System.Console` designed for TUI/CLI apps.
It keeps the familiar Console surface (title/colors/cursor/window/ReadKey), while adding terminal-native features that `System.Console` does not provide: atomic output, markup rendering, unified input events, and deterministic tests.

## Why Terminal?

`System.Console` is great for basic apps, but TUIs and concurrent CLIs quickly run into limitations:

- Output from multiple threads can interleave and corrupt ANSI sequences.
- No events for mouse or resize; input is key/text only.
- Input is split across `ReadKey`, `ReadLine`, `CancelKeyPress`, and ad-hoc polling patterns.
- “Do something, then restore state” patterns are easy to get wrong.
- Tests are hard because `System.Console` is global and environment-dependent.

XenoAtom.Terminal addresses these with:

- **Serialized output** (`Terminal.Out`/`Terminal.Error`/`Terminal.Writer`) + **atomic writes** (`WriteAtomic`).
- A **single unified input event stream** (`ReadEventsAsync`) and **Console-like** `ReadKey`/`KeyAvailable` built on it.
- **Scopes** that reliably restore terminal state.
- An **in-memory backend** for deterministic tests.

## Terminal vs System.Console

| Feature | `System.Console` | `XenoAtom.Terminal` |
|---|---:|---:|
| Thread-safe “don’t interleave” writes | ❌ | ✅ `Terminal.Out` / `Terminal.WriteAtomic(...)` |
| Atomic multi-step output (ANSI-safe) | ❌ | ✅ `WriteAtomic` / `WriteErrorAtomic` |
| Markup rendering | ❌ | ✅ `Terminal.WriteMarkup(...)` (via `XenoAtom.Ansi`) |
| Unified input events (key/mouse/resize/text/signal) | ❌ | ✅ `Terminal.ReadEventsAsync(...)` |
| Async input with cancellation | limited | ✅ `ReadEventsAsync` / `ReadKeyAsync` / `ReadLineAsync` |
| Easy “do X then restore” | manual | ✅ scopes (`UseAlternateScreen`, `HideCursor`, `UseRawMode`, …) |
| Deterministic tests | hard | ✅ `InMemoryTerminalBackend` |
| Console-like surface (Title/Colors/Cursor/Window/ReadKey) | ✅ | ✅ |

## CI and redirected output

When output is redirected, many hosts disable colors even if the log viewer supports ANSI.
XenoAtom.Terminal detects several common CI environments (GitHub Actions, Azure Pipelines, GitLab, Bitbucket, …) and keeps ANSI colors enabled while still treating the output as redirected (so cursor/screen control stays off by default).

## Getting started

```csharp
using XenoAtom.Terminal;

Terminal.WriteLine("Hello");
Terminal.WriteMarkup("[bold green]Hello[/] [gray]world[/]!");
```

## Output

### Plain text and ANSI-aware output

- Use `Terminal.Out` / `Terminal.Error` for serialized text output.
- Use `Terminal.Writer` for ANSI-aware output (built on `XenoAtom.Ansi`).

### Atomic output

Use atomic writes to ensure multi-step output stays together (especially important for ANSI style transitions):

```csharp
Terminal.WriteAtomic(w =>
{
    w.Foreground(XenoAtom.Ansi.AnsiColor.Red).Write("Error: ").ResetStyle();
    w.WriteLine("something went wrong");
});
```

### Markup

Markup is provided by `XenoAtom.Ansi.AnsiMarkup`:

```csharp
Terminal.WriteMarkup("[bold yellow]Warning:[/] something happened");
```

### Console-like properties

```csharp
Terminal.Title = "My App";
Terminal.ForegroundColor = XenoAtom.Ansi.AnsiColor.Basic16(2); // or: (XenoAtom.Ansi.AnsiColor)ConsoleColor.Green
Terminal.BackgroundColor = XenoAtom.Ansi.AnsiColor.Default;
Terminal.Decorations = XenoAtom.Ansi.AnsiDecorations.Bold;
Terminal.Beep();
```

### Cursor and window

```csharp
Terminal.SetCursorPosition(0, 0);
Terminal.WriteLine("Top-left");

Terminal.Cursor.Visible = false;
using var _restore = Terminal.UseCursorPosition(); // restores on dispose (best effort)
Terminal.CursorLeft = 10;
Terminal.CursorTop = 5;
Terminal.WriteLine("Here");
Terminal.Cursor.Visible = true;
```

Window/buffer access mirrors `System.Console`:

- `Terminal.WindowWidth`, `Terminal.WindowHeight`
- `Terminal.BufferWidth`, `Terminal.BufferHeight`
- `Terminal.LargestWindowWidth`, `Terminal.LargestWindowHeight`

## Input

XenoAtom.Terminal uses a single event stream for input. This is the preferred API for TUIs.

```csharp
Terminal.StartInput(new TerminalInputOptions { EnableMouseEvents = true });
await foreach (var ev in Terminal.ReadEventsAsync())
{
    if (ev is TerminalKeyEvent { Key: TerminalKey.Escape })
        break;
}
await Terminal.StopInputAsync();
```

### Important

When the input loop is running, consumers MUST NOT call `Console.ReadKey`, `Console.ReadLine`, `Console.KeyAvailable`, etc., as they will steal input from the same buffer.

### ReadKey / KeyAvailable

`Terminal.ReadKey(...)` is a Console-like API built on the same input stream:

```csharp
Terminal.StartInput();
if (Terminal.KeyAvailable)
{
    var key = Terminal.ReadKey(intercept: true);
    var consoleKeyInfo = key.ToConsoleKeyInfo();
}
```

### Ctrl+C behavior

By default, Ctrl+C/Ctrl+Break are published as `TerminalSignalEvent` (and can interrupt reads).

If you want Console-like behavior where Ctrl+C is treated as input (best effort):

```csharp
Terminal.TreatControlCAsInput = true;
```

## ReadLine

`Terminal.ReadLineAsync(...)` is a minimal line-oriented reader built on the terminal event stream (so apps don't need `Console.ReadLine`):

```csharp
Terminal.StartInput();
var name = await Terminal.ReadLineAsync(new TerminalReadLineOptions { Echo = true });
```

If `TerminalOptions.ImplicitStartInput` is disabled, callers must start input explicitly (e.g. `Terminal.StartInput()`) before calling `ReadLineAsync`.

## Scopes

Scoped operations restore state reliably when disposed:

```csharp
using var _title = Terminal.UseTitle("My App");
using var _alt = Terminal.UseAlternateScreen();
using var _cursor = Terminal.HideCursor();
using var _raw = Terminal.UseRawMode(TerminalRawModeKind.CBreak);

Terminal.Clear();
Terminal.WriteLine("Hello from the alternate screen");
```

## Testing

Use the in-memory backend for deterministic tests:

```csharp
using XenoAtom.Terminal.Backends;

var backend = new InMemoryTerminalBackend();
Terminal.Initialize(backend);

Terminal.WriteMarkup("[red]test[/]");
backend.PushEvent(new TerminalKeyEvent { Key = TerminalKey.Enter });

var output = backend.GetOutText();
```

## Samples

- `samples/HelloTerminal` demonstrates input events and common terminal scopes.
- `samples/LogTerminal` prints a colored pseudo log and is used in CI to validate ANSI rendering in logs.
