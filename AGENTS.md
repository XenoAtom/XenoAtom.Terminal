# XenoAtom.Terminal — Codex Agent Instructions

XenoAtom.Terminal is a modern replacement for `System.Console` for TUI/CLI apps: safe ANSI/markup output, unified input events, and deterministic tests.

Paths/commands below are relative to this repository root.

## Orientation

- Library: `src/XenoAtom.Terminal/`
- Tests: `src/XenoAtom.Terminal.Tests/` (MSTest)
- Samples: `samples/`
- Docs to keep in sync with behavior: `readme.md` and `doc/readme.md` (and other `doc/**/*.md` when relevant)
- Related repo: `C:\code\XenoAtom\XenoAtom.Ansi` (ANSI/markup dependency)

## Build & Test

```sh
cd src
dotnet build -c Release
dotnet test -c Release
```

Before submitting: all tests pass, and docs are updated if behavior changed.

## Contribution Rules (Do/Don't)

- Keep diffs focused; avoid drive-by refactors/formatting and unnecessary dependencies.
- Follow existing patterns and naming; add small "why" comments when behavior is subtle.
- New/changed behavior requires tests; bug fix = regression test first, then fix.
- Public APIs require XML docs (avoid CS1591) and should document thrown exceptions.

## C# Conventions (Project Defaults)

- Naming: `PascalCase` public/types/namespaces, `camelCase` locals/params, `_camelCase` private fields, `I*` interfaces.
- Style: file-scoped namespaces; `using` outside namespace (`System` first); `var` when the type is obvious.
- Nullability: enabled — respect annotations; use `ArgumentNullException.ThrowIfNull()`; prefer `is null`/`is not null`; don't suppress warnings without a justification comment.
- Exceptions: validate inputs early; throw specific exceptions (e.g., `ArgumentException`/`ArgumentNullException`) with meaningful messages.
- Async: `Async` suffix; no `async void` (except event handlers); use `ConfigureAwait(false)` in library code; consider `ValueTask<T>` on hot paths.

## Performance / AOT / Trimming

- Minimize allocations (`Span<T>`, `stackalloc`, `ArrayPool<T>`, avoid `string` slicing).
- Keep code NativeAOT/trimmer-friendly: avoid reflection; prefer source generators; use `[DynamicallyAccessedMembers]` when unavoidable.
- Use `sealed` for non-inheritable classes where appropriate.

## API Design

- Follow .NET guidelines; keep APIs small and hard to misuse.
- Prefer overloads over optional parameters (binary compatibility); consider `Try*` methods alongside throwing versions.
- Mark APIs `[Obsolete("message", error: false)]` before removal.

## Related Repos

These repos are optional local checkouts and may not exist in the current workspace. If present, consult their `AGENTS.md`
for cross-repo changes; otherwise treat them as external dependencies (typically consumed via NuGet).

- `XenoAtom.Ansi`: `../XenoAtom.Ansi` (if checked out)

## Git / Pre-Submit

- Commits: imperative subject, < 72 chars; one logical change per commit.
- Checklist: build+tests pass; docs updated if behavior changed; public APIs have XML docs; changes covered by unit tests.
