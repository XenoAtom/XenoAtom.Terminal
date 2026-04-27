// Copyright (c) Alexandre Mutel. All rights reserved.
// Licensed under the BSD-Clause 2 license.
// See license.txt file in the project root for full license information.

using XenoAtom.Ansi;
using XenoAtom.Ansi.Tokens;

namespace XenoAtom.Terminal.Internal;

internal sealed class TerminalGraphicsProbeCoordinator
{
    private readonly object _sync = new();
    private readonly List<string> _diagnostics = new(4);
    private int? _windowPixelWidth;
    private int? _windowPixelHeight;
    private int? _cellPixelWidth;
    private int? _cellPixelHeight;
    private int? _columns;
    private int? _rows;
    private TaskCompletionSource<TerminalPixelMetrics>? _pixelMetricsRequest;

    public AnsiKittyGraphicsReply? LastKittyReply { get; private set; }

    public TerminalPixelMetrics? LastPixelMetrics { get; private set; }

    public IReadOnlyList<string> Diagnostics => _diagnostics;

    public bool TryConsume(AnsiToken token)
    {
        switch (token)
        {
            case AnsiStringControlToken stringControl when AnsiKittyGraphicsSequences.TryParseReply(stringControl, out var kittyReply):
                LastKittyReply = kittyReply;
                _diagnostics.Add($"Consumed Kitty graphics reply: {kittyReply.Status}.");
                return true;
            case CsiToken csi when TryConsumePixelMetricReply(csi):
                return true;
            default:
                return false;
        }
    }

    public async ValueTask<TerminalPixelMetrics?> WaitForPixelMetricsAsync(TimeSpan timeout, CancellationToken cancellationToken)
    {
        TaskCompletionSource<TerminalPixelMetrics> request;
        lock (_sync)
        {
            if (LastPixelMetrics is { } metrics)
            {
                return metrics;
            }

            _pixelMetricsRequest?.TrySetCanceled(CancellationToken.None);
            request = new TaskCompletionSource<TerminalPixelMetrics>(TaskCreationOptions.RunContinuationsAsynchronously);
            _pixelMetricsRequest = request;
        }

        using var timeoutCancellation = CancellationTokenSource.CreateLinkedTokenSource(cancellationToken);
        timeoutCancellation.CancelAfter(timeout);
        try
        {
            return await request.Task.WaitAsync(timeoutCancellation.Token).ConfigureAwait(false);
        }
        catch (OperationCanceledException) when (!cancellationToken.IsCancellationRequested)
        {
            lock (_sync)
            {
                if (ReferenceEquals(_pixelMetricsRequest, request))
                {
                    _pixelMetricsRequest = null;
                }
            }

            return null;
        }
    }

    private bool TryConsumePixelMetricReply(CsiToken token)
    {
        if (token.Final != 't' || token.PrivateMarker is not null || token.Intermediates.Length != 0 || token.Parameters.Length < 3)
        {
            return false;
        }

        var kind = token.Parameters[0];
        var height = token.Parameters[1];
        var width = token.Parameters[2];
        if (height <= 0 || width <= 0)
        {
            return false;
        }

        switch (kind)
        {
            case 4:
                _windowPixelWidth = width;
                _windowPixelHeight = height;
                _diagnostics.Add($"Consumed window pixel size reply: {width}x{height}.");
                break;
            case 6:
                _cellPixelWidth = width;
                _cellPixelHeight = height;
                _diagnostics.Add($"Consumed cell pixel size reply: {width}x{height}.");
                break;
            case 8:
                _columns = width;
                _rows = height;
                _diagnostics.Add($"Consumed text area size reply: {width}x{height} cells.");
                break;
            default:
                return false;
        }

        UpdatePixelMetrics();
        return true;
    }

    private void UpdatePixelMetrics()
    {
        if (_cellPixelWidth is not { } cellWidth || _cellPixelHeight is not { } cellHeight)
        {
            return;
        }

        var columns = _columns ?? (_windowPixelWidth.HasValue ? _windowPixelWidth.Value / cellWidth : 0);
        var rows = _rows ?? (_windowPixelHeight.HasValue ? _windowPixelHeight.Value / cellHeight : 0);
        var windowPixelWidth = _windowPixelWidth ?? (columns * cellWidth);
        var windowPixelHeight = _windowPixelHeight ?? (rows * cellHeight);
        var metrics = new TerminalPixelMetrics(windowPixelWidth, windowPixelHeight, cellWidth, cellHeight, columns, rows);
        TaskCompletionSource<TerminalPixelMetrics>? request = null;

        lock (_sync)
        {
            LastPixelMetrics = metrics;
            request = _pixelMetricsRequest;
            _pixelMetricsRequest = null;
        }

        request?.TrySetResult(metrics);
    }
}