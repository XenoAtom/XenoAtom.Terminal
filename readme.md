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

- Modern `System.Console` replacement (same “feel”, more capabilities)
- Unified input events for TUIs: keys, text, mouse, resize, signals
- Rich output: ANSI styling + markup rendering (powered by XenoAtom.Ansi)
- Thread-safe output: serialized writers to avoid interleaved escape sequences
- Atomic writes: build multi-step output without tearing between threads
- Portable terminal state: style/colors/decorations, title, cursor, window size (best effort)
- Reliable scopes: alternate screen, raw/cbreak mode, mouse reporting, bracketed paste, hide cursor
- CI-friendly: detects popular CI terminals and keeps colors when output is redirected
- Deterministic tests: in-memory backend to capture output and inject input events
- Cross-platform backends: Windows Console + Unix (Linux/macOS)
- `net10.0`+ and NativeAOT-friendly design

> [!NOTE]
> XenoAtom.Terminal is a terminal API, not a widget/UI framework.
> It provides safe I/O, terminal state/scopes, and input events; higher-level libraries can build screen buffers, widgets, and layouts on top.

## User Guide

For more details on how to use XenoAtom.Terminal, please visit the [user guide](https://github.com/XenoAtom/XenoAtom.Terminal/blob/main/doc/readme.md).

## Sample

- `samples/HelloTerminal` prints all input events (key/mouse/resize/text/signal) and demonstrates scopes.
- `samples/LogTerminal` prints colored pseudo log lines (timestamp/level/category/message) and is run in CI to validate ANSI output.

## License

This software is released under the [BSD-2-Clause license](https://opensource.org/licenses/BSD-2-Clause). 

## Author

Alexandre Mutel aka [xoofx](https://xoofx.github.io).
