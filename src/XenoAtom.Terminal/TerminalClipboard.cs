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
    public bool IsSupported => CanGetText || CanSetText;

    /// <summary>
    /// Gets a value indicating whether clipboard text can be read.
    /// </summary>
    public bool CanGetText => _terminal.Capabilities.SupportsClipboardGet || _terminal.Capabilities.SupportsClipboard;

    /// <summary>
    /// Gets a value indicating whether clipboard text can be set.
    /// </summary>
    public bool CanSetText => _terminal.Capabilities.SupportsClipboardSet || _terminal.Capabilities.SupportsClipboard;

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

    /// <summary>
    /// Gets clipboard text asynchronously (best effort).
    /// </summary>
    public async ValueTask<string?> GetTextAsync(int timeoutMs = 1000, CancellationToken cancellationToken = default)
    {
        using var cts = CancellationTokenSource.CreateLinkedTokenSource(cancellationToken);
        if (timeoutMs >= 0)
        {
            cts.CancelAfter(timeoutMs);
        }

        return await Task.Run(() => TryGetText(out var text) ? text : null, cts.Token).ConfigureAwait(false);
    }

    /// <summary>
    /// Tries to set clipboard text asynchronously (best effort).
    /// </summary>
    public async ValueTask<bool> TrySetTextAsync(ReadOnlyMemory<char> text, int timeoutMs = 1000, CancellationToken cancellationToken = default)
    {
        using var cts = CancellationTokenSource.CreateLinkedTokenSource(cancellationToken);
        if (timeoutMs >= 0)
        {
            cts.CancelAfter(timeoutMs);
        }

        return await Task.Run(() => TrySetText(text.Span), cts.Token).ConfigureAwait(false);
    }

    /// <summary>
    /// Sets clipboard text asynchronously (best effort).
    /// </summary>
    public ValueTask<bool> TrySetTextAsync(string? text, int timeoutMs = 1000, CancellationToken cancellationToken = default)
        => TrySetTextAsync((text ?? string.Empty).AsMemory(), timeoutMs, cancellationToken);
}
