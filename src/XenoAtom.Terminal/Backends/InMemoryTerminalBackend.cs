// Copyright (c) Alexandre Mutel. All rights reserved.
// Licensed under the BSD-Clause 2 license.
// See license.txt file in the project root for full license information.

using System.Text;

namespace XenoAtom.Terminal.Backends;

/// <summary>
/// An in-memory backend for deterministic tests (captures stdout/stderr).
/// </summary>
public sealed class InMemoryTerminalBackend : VirtualTerminalBackend
{
    private readonly StringBuilder _outBuilder;
    private readonly StringBuilder _errorBuilder;

    /// <summary>
    /// Initializes a new instance of the <see cref="InMemoryTerminalBackend"/> class.
    /// </summary>
    /// <param name="initialSize">The initial terminal size. When default, uses 80x25.</param>
    /// <param name="capabilities">Optional capabilities to report. When <see langword="null"/>, a permissive virtual set is used.</param>
    public InMemoryTerminalBackend(TerminalSize initialSize = default, TerminalCapabilities? capabilities = null)
        : this(CreateBuilder(), CreateBuilder(), initialSize, capabilities)
    {
    }

    private InMemoryTerminalBackend(StringBuilder outBuilder, StringBuilder errorBuilder, TerminalSize initialSize, TerminalCapabilities? capabilities)
        : base(new StringWriter(outBuilder), new StringWriter(errorBuilder), initialSize, capabilities, disposeWriters: true)
    {
        _outBuilder = outBuilder;
        _errorBuilder = errorBuilder;
    }

    private static StringBuilder CreateBuilder() => new();

    /// <summary>
    /// Gets the text written to <see cref="ITerminalBackend.Out"/> so far.
    /// </summary>
    public string GetOutText() => _outBuilder.ToString();

    /// <summary>
    /// Gets the text written to <see cref="ITerminalBackend.Error"/> so far.
    /// </summary>
    public string GetErrorText() => _errorBuilder.ToString();
}

