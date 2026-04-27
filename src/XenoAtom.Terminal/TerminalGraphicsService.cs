// Copyright (c) Alexandre Mutel. All rights reserved.
// Licensed under the BSD-Clause 2 license.
// See license.txt file in the project root for full license information.

using XenoAtom.Terminal.Internal;

namespace XenoAtom.Terminal;

/// <summary>
/// Provides access to terminal graphics capabilities and probe helpers for a terminal instance.
/// </summary>
public sealed class TerminalGraphicsService
{
    private const string PixelMetricsProbeSequence = "\x1b[14t\x1b[16t\x1b[18t";

    private readonly TerminalInstance _terminal;

    internal TerminalGraphicsService(TerminalInstance terminal)
    {
        ArgumentNullException.ThrowIfNull(terminal);
        _terminal = terminal;
    }

    /// <summary>
    /// Gets the current graphics capabilities reported by the terminal backend.
    /// </summary>
    public TerminalGraphicsCapabilities Capabilities => _terminal.Capabilities.Graphics;

    /// <summary>
    /// Refreshes graphics capabilities.
    /// </summary>
    /// <param name="cancellationToken">A cancellation token.</param>
    /// <returns>The current graphics capabilities.</returns>
    /// <remarks>
    /// The core package performs deterministic option/environment detection. Probe replies consumed by the terminal input
    /// loop are exposed through diagnostics and pixel metrics queries; image decoding and protocol encoding live in the
    /// optional graphics package.
    /// </remarks>
    /// <exception cref="OperationCanceledException">The operation was canceled.</exception>
    public ValueTask<TerminalGraphicsCapabilities> RefreshCapabilitiesAsync(CancellationToken cancellationToken = default)
    {
        cancellationToken.ThrowIfCancellationRequested();
        return ValueTask.FromResult(Capabilities);
    }

    /// <summary>
    /// Queries terminal pixel metrics when they are known or when an interactive VT probe can collect them.
    /// </summary>
    /// <param name="cancellationToken">A cancellation token.</param>
    /// <returns>The known or probed pixel metrics, or <see langword="null"/> when unavailable.</returns>
    /// <exception cref="OperationCanceledException">The operation was canceled.</exception>
    public async ValueTask<TerminalPixelMetrics?> QueryPixelMetricsAsync(CancellationToken cancellationToken = default)
    {
        cancellationToken.ThrowIfCancellationRequested();
        if (Capabilities.PixelMetrics is { } metrics)
        {
            return metrics;
        }

        var terminalCapabilities = _terminal.Capabilities;
        if (_terminal.Options.Graphics.DisableProbing
            || !terminalCapabilities.AnsiEnabled
            || terminalCapabilities.IsOutputRedirected
            || terminalCapabilities.IsInputRedirected
            || _terminal.Backend is not ITerminalGraphicsProbeBackend probeBackend)
        {
            return null;
        }

        if (!_terminal.Backend.IsInputRunning)
        {
            if (!_terminal.Options.ImplicitStartInput)
            {
                return null;
            }

            _terminal.StartInput();
        }

        var timeout = _terminal.Options.Graphics.ProbeTimeout;
        var pendingMetrics = probeBackend.GraphicsProbeCoordinator.WaitForPixelMetricsAsync(timeout, cancellationToken);
        if (!probeBackend.TryWriteGraphicsProbe(PixelMetricsProbeSequence))
        {
            return null;
        }

        return await pendingMetrics.ConfigureAwait(false);
    }
}