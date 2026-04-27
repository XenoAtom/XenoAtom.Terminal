// Copyright (c) Alexandre Mutel. All rights reserved.
// Licensed under the BSD-Clause 2 license.
// See license.txt file in the project root for full license information.

namespace XenoAtom.Terminal;

/// <summary>
/// Describes detected terminal graphics capabilities.
/// </summary>
public sealed class TerminalGraphicsCapabilities
{
    /// <summary>
    /// Gets an immutable unsupported capabilities instance.
    /// </summary>
    public static TerminalGraphicsCapabilities None { get; } = new()
    {
        PreferredProtocol = TerminalGraphicsProtocol.None,
        SupportedProtocols = Array.Empty<TerminalGraphicsProtocol>(),
        SupportState = TerminalGraphicsSupportState.Unsupported,
        PresentationModel = TerminalGraphicsPresentationModel.None,
        DetectionSource = "none",
        Diagnostics = Array.Empty<string>(),
    };

    /// <summary>
    /// Gets the preferred protocol selected for output.
    /// </summary>
    public TerminalGraphicsProtocol PreferredProtocol { get; init; } = TerminalGraphicsProtocol.None;

    /// <summary>
    /// Gets the protocols believed to be available.
    /// </summary>
    public IReadOnlyList<TerminalGraphicsProtocol> SupportedProtocols { get; init; } = Array.Empty<TerminalGraphicsProtocol>();

    /// <summary>
    /// Gets the support state and source confidence.
    /// </summary>
    public TerminalGraphicsSupportState SupportState { get; init; } = TerminalGraphicsSupportState.Unsupported;

    /// <summary>
    /// Gets the presentation model of the selected protocol.
    /// </summary>
    public TerminalGraphicsPresentationModel PresentationModel { get; init; } = TerminalGraphicsPresentationModel.None;

    /// <summary>
    /// Gets a value indicating whether static images are supported.
    /// </summary>
    public bool SupportsStaticImages { get; init; }

    /// <summary>
    /// Gets a value indicating whether repeated frame updates are expected to be usable.
    /// </summary>
    public bool SupportsRealTimeUpdates { get; init; }

    /// <summary>
    /// Gets a value indicating whether image content can be retained by id.
    /// </summary>
    public bool SupportsRetainedImages { get; init; }

    /// <summary>
    /// Gets a value indicating whether placements can be retained independently from image content.
    /// </summary>
    public bool SupportsRetainedPlacements { get; init; }

    /// <summary>
    /// Gets a value indicating whether protocol-level delete commands are available.
    /// </summary>
    public bool SupportsDelete { get; init; }

    /// <summary>
    /// Gets a value indicating whether protocol-level move or replace operations are available.
    /// </summary>
    public bool SupportsMoveOrReplace { get; init; }

    /// <summary>
    /// Gets a value indicating whether placement can be expressed in terminal cells.
    /// </summary>
    public bool SupportsCellPlacement { get; init; }

    /// <summary>
    /// Gets a value indicating whether placement can be expressed in pixels.
    /// </summary>
    public bool SupportsPixelPlacement { get; init; }

    /// <summary>
    /// Gets a value indicating whether alpha/transparency can be preserved by the selected protocol.
    /// </summary>
    public bool SupportsTransparency { get; init; }

    /// <summary>
    /// Gets a value indicating whether the caller should reserve/clear cells before drawing graphics.
    /// </summary>
    public bool RequiresCellReservation { get; init; }

    /// <summary>
    /// Gets the recommended maximum protocol chunk size in bytes or characters. Zero means no chunking recommendation.
    /// </summary>
    public int MaxChunkBytes { get; init; }

    /// <summary>
    /// Gets the recommended maximum payload size before fallback or explicit opt-in should be considered.
    /// </summary>
    public int MaxRecommendedPayloadBytes { get; init; }

    /// <summary>
    /// Gets the detected or forced terminal pixel metrics, if known.
    /// </summary>
    public TerminalPixelMetrics? PixelMetrics { get; init; }

    /// <summary>
    /// Gets a short description of the source used for detection.
    /// </summary>
    public string DetectionSource { get; init; } = string.Empty;

    /// <summary>
    /// Gets the detected terminal name, if known.
    /// </summary>
    public string? TerminalName { get; init; }

    /// <summary>
    /// Gets a value indicating whether a terminal multiplexer appears active.
    /// </summary>
    public bool IsMultiplexer { get; init; }

    /// <summary>
    /// Gets a value indicating whether a remote shell/session appears active.
    /// </summary>
    public bool IsRemoteSession { get; init; }

    /// <summary>
    /// Gets diagnostic messages explaining the detection decision.
    /// </summary>
    public IReadOnlyList<string> Diagnostics { get; init; } = Array.Empty<string>();
}