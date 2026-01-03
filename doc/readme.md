# XenoAtom.Terminal User Guide

XenoAtom.Terminal is a modern replacement for `System.Console` designed for TUI/CLI apps.
It keeps a familiar Console-like surface while adding terminal-native features that `System.Console` does not provide: **atomic ANSI-safe output**, **markup/styling**, **unified input events**, **scopes for state restore**, and **deterministic testing**.

> [!NOTE]
> XenoAtom.Terminal is a terminal API, not a widget framework. It focuses on safe I/O, state/scopes, and input events; higher-level libraries can build screen buffers and widgets on top.

- [Contents](#contents)
- [Getting started](#getting-started)
  - [Initialization](#initialization)
- [Capabilities and backends](#capabilities-and-backends)
- [Output](#output)
  - [Plain text output (serialized)](#plain-text-output-serialized)
  - [ANSI-aware output (`AnsiWriter`)](#ansi-aware-output-ansiwriter)
  - [Markup](#markup)
  - [Atomic output](#atomic-output)
  - [Links (OSC 8)](#links-osc-8)
- [Console-like state (title, style, cursor, window)](#console-like-state-title-style-cursor-window)
  - [Title](#title)
  - [Style and colors (`AnsiStyle` / `AnsiColor`)](#style-and-colors-ansistyle--ansicolor)
  - [Cursor](#cursor)
  - [Window and buffer sizes](#window-and-buffer-sizes)
  - [Clipboard](#clipboard)
- [Input](#input)
  - [Important: do not mix Console input APIs](#important-do-not-mix-console-input-apis)
  - [Event types](#event-types)
  - [ReadKey / KeyAvailable (Console-like)](#readkey--keyavailable-console-like)
  - [Ctrl+C behavior](#ctrlc-behavior)
- [ReadLine editor](#readline-editor)
  - [Basic usage](#basic-usage)
  - [Prompt (plain or markup)](#prompt-plain-or-markup)
  - [History (scoped, not global)](#history-scoped-not-global)
  - [Completion (Tab)](#completion-tab)
  - [Extending the editor (custom key/mouse handlers)](#extending-the-editor-custom-keymouse-handlers)
  - [Mouse editing (optional)](#mouse-editing-optional)
  - [Rendering and styling the editable line](#rendering-and-styling-the-editable-line)
  - [Fixed-width view, ellipsis, max length](#fixed-width-view-ellipsis-max-length)
  - [Cancellation and newline emission](#cancellation-and-newline-emission)
- [Scopes](#scopes)
- [Testing](#testing)
- [Samples](#samples)

## Contents

- Getting started
- Capabilities and backends
- Output (plain, ANSI, markup, atomic writes)
- Console-like state (title, colors/style, cursor/window)
- Input (unified events, ReadKey, signals)
- ReadLine editor (detailed)
- Scopes (restore-on-dispose)
- Testing (in-memory backend)
- Samples

## Getting started

Add the NuGet package, then write normally:

```csharp
using XenoAtom.Terminal;

Terminal.WriteLine("Hello");
Terminal.WriteMarkup("[bold green]Hello[/] [gray]world[/]!");
```

### Initialization

The first use initializes the global instance lazily. You can also initialize explicitly:

```csharp
Terminal.Initialize(options: new TerminalOptions
{
    PreferUtf8Output = true,
    Prefer7BitC1 = true,
    ForceAnsi = false,
    StrictMode = false,
});
```

For deterministic tests or headless scenarios, pass a backend:

```csharp
using XenoAtom.Terminal.Backends;

var backend = new InMemoryTerminalBackend();
Terminal.Initialize(backend);
```

## Capabilities and backends

XenoAtom.Terminal selects a backend automatically (Windows Console on Windows, Unix on Linux/macOS, and a virtual CI backend when output is redirected but ANSI is supported).

Inspect the detected capabilities:

```csharp
Terminal.WriteLine($"Ansi={Terminal.Capabilities.AnsiEnabled}, Color={Terminal.Capabilities.ColorLevel}, Mouse={Terminal.Capabilities.SupportsMouse}");
```

> [!NOTE]
> Terminal capabilities vary by host (terminal emulator, CI log, SSH, etc.). Some APIs are best-effort or capability-dependent; check the API/XML documentation for per-member details.

## Output

### Plain text output (serialized)

Use `Terminal.Out` and `Terminal.Error` to write plain text safely from multiple threads (writes are serialized):

```csharp
Terminal.Out.WriteLine("stdout");
Terminal.Error.WriteLine("stderr");
```

### ANSI-aware output (`AnsiWriter`)

Use `Terminal.Writer` (or `Terminal.WriteAtomic(Action<AnsiWriter>)`) for ANSI/VT styling and cursor operations:

```csharp
Terminal.WriteAtomic(w =>
{
    w.Foreground(ConsoleColor.DarkGray);
    w.Write("[");
    w.Write(DateTimeOffset.UtcNow.ToString("HH:mm:ss.fff"));
    w.Write("] ");
    w.ResetStyle();
    w.Write("message\n");
});
```

For fluent usage, `Terminal.T` is a convenience alias for the instance:

```csharp
Terminal.T.Foreground(ConsoleColor.Green).Write("ok").ResetStyle().WriteLine();
```

### Markup

Markup is provided by `XenoAtom.Ansi.AnsiMarkup` and rendered to ANSI:

```csharp
Terminal.WriteMarkup("[bold yellow]Warning:[/] something happened");
```

### Atomic output

Atomic writes guarantee that a multi-step output sequence is not interleaved with other concurrent output (important for styling transitions and cursor ops):

```csharp
Terminal.WriteAtomic(w =>
{
    w.Foreground(ConsoleColor.Red).Write("Error: ").ResetStyle();
    w.Write("something went wrong\n");
});
```

### Links (OSC 8)

When supported, you can emit hyperlinks:

```csharp
Terminal.WriteAtomic(w =>
{
    w.BeginLink("https://github.com/XenoAtom/XenoAtom.Terminal");
    w.Write("XenoAtom.Terminal");
    w.EndLink();
    w.Write("\n");
});
```

## Console-like state (title, style, cursor, window)

### Title

```csharp
Terminal.Title = "My App";
```

### Style and colors (`AnsiStyle` / `AnsiColor`)

Terminal exposes style via `XenoAtom.Ansi` types. `AnsiColor` supports default colors and has an implicit converter from `ConsoleColor`.

```csharp
Terminal.ForegroundColor = (XenoAtom.Ansi.AnsiColor)ConsoleColor.Green;
Terminal.BackgroundColor = XenoAtom.Ansi.AnsiColor.Default;
Terminal.Decorations = XenoAtom.Ansi.AnsiDecorations.Bold;
```

> [!NOTE]
> These properties reflect style changes made via Terminal APIs. If raw ANSI is written outside of Terminal APIs, the tracked state can become inaccurate.

### Cursor

```csharp
Terminal.Cursor.Visible = false;
Terminal.Cursor.Position = new TerminalPosition(0, 0);
Terminal.Cursor.Style = XenoAtom.Ansi.AnsiCursorStyle.Bar;
Terminal.WriteLine("Top-left");
Terminal.Cursor.Visible = true;
```

Async position query:

```csharp
var pos = await Terminal.Cursor.GetPositionAsync();
```

Scoped cursor restore:

```csharp
using var _pos = Terminal.UseCursorPosition();
Terminal.SetCursorPosition(10, 5);
Terminal.WriteLine("Temporarily moved");
```

### Window and buffer sizes

The API mirrors `System.Console` where possible:

- `Terminal.WindowWidth`, `Terminal.WindowHeight`
- `Terminal.BufferWidth`, `Terminal.BufferHeight`
- `Terminal.LargestWindowWidth`, `Terminal.LargestWindowHeight`

### Clipboard

Terminal provides clipboard access via `Terminal.Clipboard`:

```csharp
Terminal.Clipboard.Text = "Hello from XenoAtom.Terminal";
var text = Terminal.Clipboard.Text;
```

Clipboard support is platform-dependent:

- Windows: Win32 clipboard (`CF_UNICODETEXT`)
- macOS: `pbcopy` / `pbpaste`
- Linux: prefers `wl-copy` / `wl-paste` (Wayland), then `xclip` / `xsel` (X11)

## Input

XenoAtom.Terminal uses a single unified event stream for input. This is the preferred API for TUIs.

```csharp
Terminal.StartInput(new TerminalInputOptions
{
    EnableResizeEvents = true,
    EnableMouseEvents = true,
    MouseMode = TerminalMouseMode.Drag,
});

await foreach (var ev in Terminal.ReadEventsAsync())
{
    if (ev is TerminalKeyEvent { Key: TerminalKey.Escape })
        break;
}

await Terminal.StopInputAsync();
```

### Important: do not mix Console input APIs

When the input loop is running, do not call `Console.ReadKey`, `Console.ReadLine`, `Console.KeyAvailable`, etc. They read from the same underlying stream and will steal input.

### Event types

The default input stream can publish:

- `TerminalKeyEvent` (non-text keys and some control sequences)
- `TerminalTextEvent` (text input)
- `TerminalPasteEvent` (bracketed paste text when enabled/supported)
- `TerminalMouseEvent` (mouse move/down/up/drag/wheel)
- `TerminalResizeEvent` (terminal resized)
- `TerminalSignalEvent` (interrupt/break)

### ReadKey / KeyAvailable (Console-like)

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
If you want Console-like behavior where Ctrl+C is treated as input:

```csharp
Terminal.TreatControlCAsInput = true;
```

## ReadLine editor

`Terminal.ReadLineAsync(...)` is built on the terminal event stream (so apps don't need `Console.ReadLine`).

When supported (interactive output + cursor positioning), it provides an in-terminal editor; otherwise it falls back to a simple non-editing mode.

### Basic usage

```csharp
Terminal.StartInput();
var line = await Terminal.ReadLineAsync(new TerminalReadLineOptions { Echo = true });
```

### Prompt (plain or markup)

Use `Prompt` for plain text prompts, or `PromptMarkup` for styled prompts:

```csharp
var counter = 1;
var options = new TerminalReadLineOptions
{
    Echo = true,
    PromptMarkup = () => $"[gray]{counter++}[/] [darkgray]>[/] ",
};

var line = await Terminal.ReadLineAsync(options);
```

### History (scoped, not global)

History lives on `TerminalReadLineOptions` so you can scope/share it explicitly:

```csharp
Terminal.StartInput();
var options = new TerminalReadLineOptions { Echo = true, PromptMarkup = () => "[darkgray]>[/] " };

while (true)
{
    var line = await Terminal.ReadLineAsync(options);
    if (line is null || line.Length == 0) break;
}
```

### Completion (Tab)

```csharp
var options = new TerminalReadLineOptions
{
    Echo = true,
    CompletionHandler = static (text, cursor, selectionStart, selectionLength) =>
    {
        _ = selectionStart;
        _ = selectionLength;

        // Return candidates, then press Tab repeatedly to cycle through them until you type another key.
        return new TerminalReadLineCompletion
        {
            Handled = true,
            Candidates = ["help", "hello", "helium"],
            ReplaceStart = 0,
            ReplaceLength = cursor,
        };
    },
};
```

### Extending the editor (custom key/mouse handlers)

Use `KeyHandler` and `MouseHandler` to react to keys (including `Esc`, `F1`..`F12`, etc.) or mouse input.
Handlers receive a `TerminalReadLineController` that can edit the line:

```csharp
var options = new TerminalReadLineOptions
{
    Echo = true,
    KeyHandler = static (ctl, key) =>
    {
        if (key.Key == TerminalKey.Escape) ctl.Cancel();
        if (key.Key == TerminalKey.F2) ctl.Insert(" --flag".AsSpan());
        if (key.Key == TerminalKey.F3) ctl.SetCursorIndex(0);
        if (key.Key == TerminalKey.F4) ctl.Select(0, ctl.Length);
    },
};
```

### Mouse editing (optional)

Mouse-based cursor positioning and selection is opt-in:

```csharp
using var _mouse = Terminal.EnableMouseInput(TerminalMouseMode.Drag);
var line = await Terminal.ReadLineAsync(new TerminalReadLineOptions { EnableMouseEditing = true });
```

`EnableMouseInput(...)` enables mouse reporting and ensures mouse events are published by the input loop.

#### Why mouse “does nothing” in terminals

Most terminal emulators (including Windows Terminal) use the mouse for **their own UI** by default (text selection, copy/paste, context menus).
Applications only receive mouse events after they explicitly enable *mouse reporting*.

When mouse reporting is enabled, the terminal typically stops doing its own text selection and instead sends mouse events to the application.
In many terminals you can still force “terminal selection” by holding a modifier (commonly **Shift**) while dragging.

### Rendering and styling the editable line

Use `MarkupRenderer` to control how the visible slice of the line is rendered (e.g. highlighting matches or selection):

```csharp
var options = new TerminalReadLineOptions
{
    Echo = true,
    MarkupRenderer = static (text, cursor, viewStart, viewLength, selectionStart, selectionLength)
        => XenoAtom.Ansi.AnsiMarkup.Escape(text.Slice(viewStart, viewLength)),
};
```

### Fixed-width view, ellipsis, max length

For constrained UI regions, use:

- `ViewWidth` (cells) to restrict the visible editing region
- `ShowEllipsis` / `Ellipsis` to indicate truncation
- `MaxLength` to cap input length

### Text utilities (cell width, word boundaries)

For higher-level controls (layout, clipping, hit testing), use `TerminalTextUtility`:

```csharp
var cells = TerminalTextUtility.GetWidth("A😃中".AsSpan());
TerminalTextUtility.TryGetIndexAtCell("hello world".AsSpan(), cellOffset: 6, out var index);

var wordStart = TerminalTextUtility.GetWordStart("hello_world".AsSpan(), index: 8);
var wordEnd = TerminalTextUtility.GetWordEnd("hello_world".AsSpan(), index: 8);
```

### Cancellation and newline emission

- Ctrl+C copies the current selection when present; otherwise it cancels the editor (throws `OperationCanceledException`).
- Ctrl+X cuts the selection (or the whole line when nothing is selected) to the clipboard.
- Ctrl+V pastes clipboard text.
- Set `EmitNewLineOnAccept = false` to accept without writing a newline.

If `TerminalOptions.ImplicitStartInput` is disabled, callers must start input explicitly (e.g. `Terminal.StartInput()`) before calling `ReadLineAsync`.

## Scopes

Scoped operations restore terminal state reliably when disposed:

```csharp
using var _title = Terminal.UseTitle("My App");
using var _alt = Terminal.UseAlternateScreen();
using var _cursor = Terminal.HideCursor();
using var _raw = Terminal.UseRawMode(TerminalRawModeKind.CBreak);
using var _paste = Terminal.EnableBracketedPaste();

Terminal.Clear();
Terminal.WriteLine("Hello from the alternate screen");
```

## Testing

Use the in-memory backend for deterministic tests (captures output and lets you inject events):

```csharp
using XenoAtom.Terminal.Backends;

var backend = new InMemoryTerminalBackend();
Terminal.Initialize(backend);
Terminal.StartInput(new TerminalInputOptions { EnableMouseEvents = false, EnableResizeEvents = false });

var task = Terminal.ReadLineAsync(new TerminalReadLineOptions { Echo = false }).AsTask();
backend.PushEvent(new TerminalTextEvent { Text = "abc" });
backend.PushEvent(new TerminalKeyEvent { Key = TerminalKey.Enter });

var line = await task;
var outText = backend.GetOutText();
```

## Samples

- `samples/HelloTerminal` prints all input events and demonstrates scopes.
- `samples/HelloReadLine` demonstrates the interactive editor (prompt markup, history, selection, mouse, completion, custom handlers).
- `samples/LogTerminal` prints colored pseudo log lines and is run in CI to validate ANSI output.
