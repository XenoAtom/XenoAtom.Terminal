// Copyright (c) Alexandre Mutel. All rights reserved.
// Licensed under the BSD-Clause 2 license.
// See license.txt file in the project root for full license information.

using XenoAtom.Terminal.Internal;

namespace XenoAtom.Terminal;

/// <summary>
/// Represents a scope that restores terminal state when disposed.
/// </summary>
public readonly struct TerminalScope : IDisposable
{
    private readonly IDisposable? _disposable;

    internal TerminalScope(IDisposable? disposable) => _disposable = disposable;

    /// <summary>
    /// Disposes this scope and restores the previous terminal state (best effort).
    /// </summary>
    public void Dispose() => _disposable?.Dispose();

    /// <summary>
    /// Gets a value indicating whether this scope performs no action.
    /// </summary>
    public bool IsEmpty => _disposable is null;

    /// <summary>
    /// Gets an empty scope that performs no action.
    /// </summary>
    public static TerminalScope Empty => default;

    internal static TerminalScope Create(Action dispose) => new TerminalScope(new DisposeOnce(dispose));
}
