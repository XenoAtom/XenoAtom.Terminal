// Copyright (c) Alexandre Mutel. All rights reserved.
// Licensed under the BSD-Clause 2 license.
// See license.txt file in the project root for full license information.

namespace XenoAtom.Terminal;

/// <summary>
/// Represents an application-level terminal session that ensures cleanup on dispose.
/// </summary>
public sealed class TerminalSession : IDisposable, IAsyncDisposable
{
    internal TerminalSession(TerminalInstance instance)
    {
        Instance = instance ?? throw new ArgumentNullException(nameof(instance));
    }

    /// <summary>
    /// Gets the terminal instance for this session.
    /// </summary>
    public TerminalInstance Instance { get; }

    /// <summary>
    /// Disposes the global terminal instance (idempotent).
    /// </summary>
    public void Dispose() => Terminal.Close();

    /// <summary>
    /// Disposes the global terminal instance asynchronously (idempotent).
    /// </summary>
    public ValueTask DisposeAsync()
    {
        Terminal.Close();
        return default;
    }
}

