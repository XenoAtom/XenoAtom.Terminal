# XenoAtom.Terminal Code Contribution Instructions

## Overview

- In `readme.md`, you will find general information about the XenoAtom.Terminal project.
- In `doc/readme.md`, you will find the user guide documentation for the XenoAtom.Terminal library.
- The `XenoAtom.Ansi` dependency lives in `C:\code\XenoAtom\XenoAtom.Ansi` and can be referenced as needed.

## Project Structure

- `src/XenoAtom.Terminal`: main library code.
- `src/XenoAtom.Terminal.Tests`: unit tests.
- `samples`: sample applications demonstrating usage.

## Building and Testing

- Build: from `src`, run `dotnet build -c Release`.
- Tests: from `src`, run `dotnet test -c Release`.
- Ensure that all tests pass successfully before submitting any changes.
- Ensure that user guide documentation (`doc/readme.md`) and top-level readme are updated to reflect any changes made to the library.

## General Coding Instructions

- Follow the coding style and conventions used in the existing code base.
- Write clear and concise inline comments to explain the purpose and functionality of your code. This is about the "why" more than the "what".
- All public APIs must have XML documentation comments to avoid CS1591 warnings.
- Ensure that your code is well-structured and modular to facilitate maintenance and future enhancements.
- Adhere to best practices for error handling and input validation.
- Write unit tests for any new functionality you add to ensure code quality and reliability.
  - When fixing a bug, add a unit test that reproduces the bug before implementing the fix.
- Use meaningful variable and method names that accurately reflect their purpose.
- Avoid code duplication by reusing existing methods and classes whenever possible.

## C# Coding Conventions

### Naming Conventions

- Use `PascalCase` for public members, types, and namespaces.
- Use `camelCase` for local variables and parameters.
- Use `_camelCase` (with underscore prefix) for private fields.
- Prefix interfaces with `I` (e.g., `IMyInterface`).
- Use descriptive names; avoid abbreviations unless widely understood (e.g., `Id`, `Url`).

### Code Style

- Use file-scoped namespaces unless the file requires multiple namespaces.
- Use `var` when the type is obvious from the right-hand side; otherwise, use explicit types.
- Prefer expression-bodied members for single-line implementations.
- Use pattern matching and switch expressions where they improve readability.
- Place `using` directives outside the namespace, sorted alphabetically with `System` namespaces first.

### Nullable Reference Types

- This project uses nullable reference types. Respect nullability annotations.
- Never suppress nullable warnings (`#pragma warning disable`) without a comment explaining why.
- Use `ArgumentNullException.ThrowIfNull()` for null checks on parameters.
- Prefer `is null` and `is not null` over `== null` and `!= null`.

### Error Handling

- Throw `ArgumentException` or `ArgumentNullException` for invalid arguments.
- Use specific exception types rather than generic `Exception`.
- Include meaningful error messages that help diagnose the issue.
- Document exceptions in XML comments using `<exception cref="...">` when appropriate.

### Async/Await

- Suffix async methods with `Async` (e.g., `LoadDataAsync`).
- Use `ConfigureAwait(false)` in library code unless context capture is required.
- Prefer `ValueTask<T>` over `Task<T>` for hot paths that often complete synchronously.
- Never use `async void` except for event handlers.

## Performance Considerations

- Ensure that the code is optimized for performance without sacrificing readability.
- Ensure that the code minimizes GC allocations where possible.
  - Use `Span<T>`/`ReadOnlySpan<T>` where appropriate to reduce memory allocations.
  - Use `stackalloc` for small, fixed-size buffers in performance-critical paths.
  - Use `ArrayPool<T>` for temporary arrays that would otherwise cause allocations.
- Ensure generated code is AOT-compatible and trimmer-friendly.
  - Avoid reflection where possible; prefer source generators.
  - Use `[DynamicallyAccessedMembers]` attributes when reflection is necessary.
- Use `sealed` on classes that are not designed for inheritance to enable devirtualization.
- Prefer `ReadOnlySpan<char>` over `string` for parsing and substring operations.

## Testing Guidelines

### Test Organization

- Name test classes as `{ClassName}Tests`.
- Name test methods descriptively: `{MethodName}_{Scenario}_{ExpectedResult}` (or plain English when clearer).
- Use the Arrange-Act-Assert (AAA) pattern.
- Avoid test interdependencies; each test must be able to run in isolation.

### Test Quality

- Each test should verify one specific behavior (single assertion concept).
- Include edge cases: null inputs, empty collections, boundary values, and error conditions.
- When fixing a bug, first write a test that reproduces the bug, then fix it.

## API Design Guidelines

- Follow .NET API design guidelines for consistency with the ecosystem.
- Keep APIs simple and focused; avoid over-engineering.
- Don't introduce unnecessary interface abstractions; prefer concrete types unless explicit extensibility is required.
- Use immutable types where possible; allow mutability when necessary for performance or usability.
- Make APIs hard to misuse: validate inputs early and use strong types.
- Prefer method overloads over optional parameters for binary compatibility.
- Consider adding `Try*` pattern methods (returning `bool`) alongside throwing versions.
- Mark obsolete APIs with `[Obsolete("message", error: false)]` before removal.

## Git Commit Instructions

- Write a concise and descriptive commit message that summarizes the changes made.
- Start the commit message with a verb in imperative mood (e.g., "Add", "Fix", "Update", "Remove").
- Keep the first line under 72 characters; add details in the body if needed.
- Create a commit for each logical change or feature added to facilitate easier code review and tracking of changes.

## Pre-Submission Checklist

Before submitting changes, verify:

- [ ] Code builds without errors or warnings (`dotnet build -c Release`).
- [ ] All tests pass (`dotnet test -c Release`).
- [ ] New public APIs have XML documentation comments.
- [ ] Changes are covered by unit tests.
- [ ] No unintended files are included in the commit.
- [ ] Documentation is updated if behavior changes.
