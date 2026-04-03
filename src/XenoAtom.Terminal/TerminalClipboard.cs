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
    public bool IsSupported => CanGetText || CanSetText || CanGetFormats || CanSetFormats;

    /// <summary>
    /// Gets a value indicating whether clipboard text can be read.
    /// </summary>
    public bool CanGetText => _terminal.Capabilities.SupportsClipboardGet || _terminal.Capabilities.SupportsClipboard;

    /// <summary>
    /// Gets a value indicating whether clipboard text can be set.
    /// </summary>
    public bool CanSetText => _terminal.Capabilities.SupportsClipboardSet || _terminal.Capabilities.SupportsClipboard;

    /// <summary>
    /// Gets a value indicating whether named clipboard formats can be enumerated or read.
    /// </summary>
    public bool CanGetFormats
        => _terminal.Capabilities.SupportsClipboardFormatsGet
        || _terminal.Capabilities.SupportsClipboardGet
        || _terminal.Capabilities.SupportsClipboard;

    /// <summary>
    /// Gets a value indicating whether named clipboard formats can be written.
    /// </summary>
    public bool CanSetFormats
        => _terminal.Capabilities.SupportsClipboardFormatsSet
        || _terminal.Capabilities.SupportsClipboardSet
        || _terminal.Capabilities.SupportsClipboard;

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
    /// Tries to get the advertised clipboard formats (best effort).
    /// </summary>
    public bool TryGetFormats([System.Diagnostics.CodeAnalysis.NotNullWhen(true)] out IReadOnlyList<string>? formats)
        => _terminal.Backend.TryGetClipboardFormats(out formats);

    /// <summary>
    /// Gets the advertised clipboard formats (best effort).
    /// </summary>
    public IReadOnlyList<string> GetFormats()
        => TryGetFormats(out var formats) ? formats : Array.Empty<string>();

    /// <summary>
    /// Tries to get clipboard data for the specified format (best effort).
    /// </summary>
    /// <param name="format">The format identifier. Use <see cref="TerminalClipboardFormats" /> for well-known values.</param>
    /// <param name="data">When this method returns <see langword="true" />, contains the clipboard bytes for the requested format.</param>
    public bool TryGetData(string format, [System.Diagnostics.CodeAnalysis.NotNullWhen(true)] out byte[]? data)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(format);
        return _terminal.Backend.TryGetClipboardData(format, out data);
    }

    /// <summary>
    /// Tries to set clipboard data for the specified format (best effort).
    /// </summary>
    /// <param name="format">The format identifier. Use <see cref="TerminalClipboardFormats" /> for well-known values.</param>
    /// <param name="data">The exact bytes to store for the requested format.</param>
    public bool TrySetData(string format, ReadOnlySpan<byte> data)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(format);
        return _terminal.Backend.TrySetClipboardData(format, data);
    }

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
    /// Gets the advertised clipboard formats asynchronously (best effort).
    /// </summary>
    public async ValueTask<IReadOnlyList<string>> GetFormatsAsync(int timeoutMs = 1000, CancellationToken cancellationToken = default)
    {
        using var cts = CancellationTokenSource.CreateLinkedTokenSource(cancellationToken);
        if (timeoutMs >= 0)
        {
            cts.CancelAfter(timeoutMs);
        }

        return await Task.Run(GetFormats, cts.Token).ConfigureAwait(false);
    }

    /// <summary>
    /// Gets clipboard data for the specified format asynchronously (best effort).
    /// </summary>
    public async ValueTask<byte[]?> GetDataAsync(string format, int timeoutMs = 1000, CancellationToken cancellationToken = default)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(format);

        using var cts = CancellationTokenSource.CreateLinkedTokenSource(cancellationToken);
        if (timeoutMs >= 0)
        {
            cts.CancelAfter(timeoutMs);
        }

        return await Task.Run(() => TryGetData(format, out var data) ? data : null, cts.Token).ConfigureAwait(false);
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
    /// Tries to set clipboard data asynchronously (best effort).
    /// </summary>
    public async ValueTask<bool> TrySetDataAsync(string format, ReadOnlyMemory<byte> data, int timeoutMs = 1000, CancellationToken cancellationToken = default)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(format);

        using var cts = CancellationTokenSource.CreateLinkedTokenSource(cancellationToken);
        if (timeoutMs >= 0)
        {
            cts.CancelAfter(timeoutMs);
        }

        return await Task.Run(() => TrySetData(format, data.Span), cts.Token).ConfigureAwait(false);
    }

    /// <summary>
    /// Sets clipboard text asynchronously (best effort).
    /// </summary>
    public ValueTask<bool> TrySetTextAsync(string? text, int timeoutMs = 1000, CancellationToken cancellationToken = default)
        => TrySetTextAsync((text ?? string.Empty).AsMemory(), timeoutMs, cancellationToken);
}
