// Copyright (c) Alexandre Mutel. All rights reserved.
// Licensed under the BSD-Clause 2 license.
// See license.txt file in the project root for full license information.

namespace XenoAtom.Terminal;

/// <summary>
/// Provides access to the system clipboard (best effort).
/// </summary>
public sealed class TerminalClipboard
{
    private readonly TerminalInstance _terminal;

    internal TerminalClipboard(TerminalInstance terminal)
    {
        _terminal = terminal ?? throw new ArgumentNullException(nameof(terminal));
    }

    /// <summary>
    /// Gets a value indicating whether clipboard access is supported (best effort).
    /// </summary>
    public bool IsSupported => _terminal.Capabilities.SupportsClipboard;

    /// <summary>
    /// Gets or sets clipboard text (best effort).
    /// </summary>
    public string? Text
    {
        get => TryGetText(out var text) ? text : null;
        set
        {
            var span = (value ?? string.Empty).AsSpan();
            if (!TrySetText(span) && _terminal.Options.StrictMode)
            {
                throw new NotSupportedException("Clipboard access is not supported by this terminal backend.");
            }
        }
    }

    /// <summary>
    /// Tries to get clipboard text (best effort).
    /// </summary>
    public bool TryGetText(out string? text) => _terminal.Backend.TryGetClipboardText(out text);

    /// <summary>
    /// Tries to set clipboard text (best effort).
    /// </summary>
    public bool TrySetText(ReadOnlySpan<char> text) => _terminal.Backend.TrySetClipboardText(text);
}

