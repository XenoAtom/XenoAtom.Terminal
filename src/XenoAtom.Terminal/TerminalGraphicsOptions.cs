// Copyright (c) Alexandre Mutel. All rights reserved.
// Licensed under the BSD-Clause 2 license.
// See license.txt file in the project root for full license information.

namespace XenoAtom.Terminal;

/// <summary>
/// Options controlling terminal graphics detection and policy.
/// </summary>
public sealed class TerminalGraphicsOptions
{
    /// <summary>
    /// Gets or sets a preferred protocol. When not <see cref="TerminalGraphicsProtocol.None"/>, it is treated as an explicit override.
    /// </summary>
    public TerminalGraphicsProtocol PreferredProtocol { get; set; } = TerminalGraphicsProtocol.None;

    /// <summary>
    /// Gets or sets a value indicating whether terminal graphics should be disabled.
    /// </summary>
    public bool DisableGraphics { get; set; }

    /// <summary>
    /// Gets or sets a value indicating whether active probes should be skipped.
    /// </summary>
    public bool DisableProbing { get; set; }

    /// <summary>
    /// Gets or sets a value indicating whether environment heuristics may enable graphics.
    /// </summary>
    public bool AllowHeuristicEnablement { get; set; } = true;

    /// <summary>
    /// Gets or sets a value indicating whether graphics may be enabled while a multiplexer appears active.
    /// </summary>
    public bool AllowMultiplexerPassthrough { get; set; } = true;

    /// <summary>
    /// Gets or sets the timeout budget for active probes.
    /// </summary>
    public TimeSpan ProbeTimeout { get; set; } = TimeSpan.FromMilliseconds(250);

    /// <summary>
    /// Gets or sets forced pixel metrics to use instead of probing or environment-derived metrics.
    /// </summary>
    public TerminalPixelMetrics? ForcedPixelMetrics { get; set; }

    /// <summary>
    /// Gets or sets the protocol preference order used when multiple protocols are supported.
    /// </summary>
    public IReadOnlyList<TerminalGraphicsProtocol>? ProtocolOrder { get; set; }
}