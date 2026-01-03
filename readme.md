# XenoAtom.Terminal [![ci](https://github.com/XenoAtom/XenoAtom.Terminal/actions/workflows/ci.yml/badge.svg)](https://github.com/XenoAtom/XenoAtom.Terminal/actions/workflows/ci.yml) [![NuGet](https://img.shields.io/nuget/v/XenoAtom.Terminal.svg)](https://www.nuget.org/packages/XenoAtom.Terminal/)

<img align="right" width="160px" height="160px" src="https://raw.githubusercontent.com/XenoAtom/XenoAtom.Terminal/main/img/XenoAtom.Terminal.png">

XenoAtom.Terminal is a modern replacement for `System.Console` designed for TUI/CLI apps: serialized output, rich ANSI/markup rendering, unified input events, and deterministic tests.

> [!WARNING]
> This project is under development and in pre-release status. The API may change before reaching a stable 1.0.0 version.

## Quick start

```csharp
using XenoAtom.Terminal;

Terminal.WriteLine("Hello");
Terminal.WriteMarkup("[bold green]Hello[/] [gray]world[/]!");
```

## ✨ Features

- **Console-compatible API surface**: Title, cursor, window, `ReadKey`/`ReadLine`-style workflows
- **Output (ANSI-safe)**:
  - **Serialized writers** prevent interleaved escape sequences across threads
  - **Atomic writes** for multi-step output without tearing
  - **Markup + ANSI styling** (powered by [XenoAtom.Ansi](https://github.com/XenoAtom/XenoAtom.Ansi))
- **Input (unified events)**:
  - Single event stream for **keys**, **text**, **mouse**, **resize**, **signals**
  - Async + cancellation-friendly APIs for TUI loops
- **Interactive ReadLine editor** (best effort):
  - Cursor movement, mid-line insert/delete, word navigation/delete
  - Selection by keyboard (Shift) and **mouse click/drag**
  - History stored on the `TerminalReadLineOptions` instance (shareable, not global)
  - Completion + extensibility via **custom key/mouse handlers** (`TerminalReadLineController`)
  - Styled prompt (`PromptMarkup`) + custom line rendering (`MarkupRenderer`)
- **Scopes + state management**:
  - Reliable scopes: alternate screen, raw/cbreak mode, bracketed paste, mouse reporting, hide cursor
  - Best-effort state: style/colors/decorations, title, cursor position/visibility, window size
- **CI + testing**:
  - CI-aware backend keeps colors when output is redirected
  - In-memory backend for deterministic tests (capture output + inject events)
- **Cross-platform + AOT**: Windows Console + Unix (Linux/macOS), `net10.0`+ and NativeAOT-friendly design

> [!NOTE]
> XenoAtom.Terminal is a terminal API, not a widget/UI framework.
> It provides safe I/O, terminal state/scopes, and input events; higher-level libraries can build screen buffers, widgets, and layouts on top.

## User Guide

For more details on how to use XenoAtom.Terminal, please visit the [user guide](https://github.com/XenoAtom/XenoAtom.Terminal/blob/main/doc/readme.md).

## Sample

- `samples/HelloTerminal` prints all input events (key/mouse/resize/text/signal) and demonstrates scopes.
- `samples/HelloReadLine` demonstrates the interactive `ReadLine` editor (history, selection, completion, and markup rendering).
- `samples/LogTerminal` prints colored pseudo log lines (timestamp/level/category/message) and is run in CI to validate ANSI output.

## License

This software is released under the [BSD-2-Clause license](https://opensource.org/licenses/BSD-2-Clause). 

## Author

Alexandre Mutel aka [xoofx](https://xoofx.github.io).
