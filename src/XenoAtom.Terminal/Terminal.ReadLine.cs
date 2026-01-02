// Copyright (c) Alexandre Mutel. All rights reserved.
// Licensed under the BSD-Clause 2 license.
// See license.txt file in the project root for full license information.

namespace XenoAtom.Terminal;

public static partial class Terminal
{
    /// <inheritdoc cref="TerminalInstance.ReadLine(TerminalReadLineOptions?)" />
    public static string? ReadLine(TerminalReadLineOptions? options = null) => Instance.ReadLine(options);

    /// <inheritdoc cref="TerminalInstance.ReadLineAsync(TerminalReadLineOptions?, CancellationToken)" />
    public static ValueTask<string?> ReadLineAsync(TerminalReadLineOptions? options = null, CancellationToken cancellationToken = default)
        => Instance.ReadLineAsync(options, cancellationToken);
}
