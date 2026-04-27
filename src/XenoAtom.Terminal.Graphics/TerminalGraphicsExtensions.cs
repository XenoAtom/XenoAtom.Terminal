// Copyright (c) Alexandre Mutel. All rights reserved.
// Licensed under the BSD-Clause 2 license.
// See license.txt file in the project root for full license information.

using System.Buffers;
using System.IO;
using XenoAtom.Ansi;
using XenoAtom.Terminal;

namespace XenoAtom.Terminal.Graphics;

/// <summary>
/// Options used when writing an image directly to a terminal instance.
/// </summary>
public sealed class TerminalImageWriteOptions
{
    /// <summary>
    /// Gets or sets the protocol to use. <see cref="TerminalGraphicsProtocol.None"/> selects the terminal's preferred protocol.
    /// </summary>
    public TerminalGraphicsProtocol Protocol { get; set; } = TerminalGraphicsProtocol.None;

    /// <summary>
    /// Gets or sets the target pixel size. When empty, the frame's natural size is used unless cell size and pixel metrics provide a better size.
    /// </summary>
    public TerminalImageSize PixelSize { get; set; } = TerminalImageSize.Empty;

    /// <summary>
    /// Gets or sets the target terminal cell size used by protocol placement metadata.
    /// </summary>
    public TerminalImageSize CellSize { get; set; } = TerminalImageSize.Empty;

    /// <summary>
    /// Gets or sets terminal pixel metrics used to derive a pixel size from <see cref="CellSize"/>.
    /// </summary>
    public TerminalPixelMetrics? PixelMetrics { get; set; }

    /// <summary>
    /// Gets or sets the scale mode.
    /// </summary>
    public TerminalImageScaleMode ScaleMode { get; set; } = TerminalImageScaleMode.Fit;

    /// <summary>
    /// Gets or sets a value indicating whether aspect ratio should be preserved.
    /// </summary>
    public bool PreserveAspectRatio { get; set; } = true;

    /// <summary>
    /// Gets or sets the matte color used when alpha must be flattened.
    /// </summary>
    public TerminalImageColor? MatteColor { get; set; }

    /// <summary>
    /// Gets or sets the raster resampling quality.
    /// </summary>
    public TerminalImageResamplingQuality Quality { get; set; } = TerminalImageResamplingQuality.High;

    /// <summary>
    /// Gets or sets the maximum payload chunk size for protocols that support chunking.
    /// </summary>
    public int MaxPayloadChunkBytes { get; set; } = AnsiKittyGraphicsSequences.DefaultMaxPayloadChunkChars;

    /// <summary>
    /// Gets or sets an optional retained Kitty image id.
    /// </summary>
    public int? ImageId { get; set; }

    /// <summary>
    /// Gets or sets an optional retained Kitty placement id.
    /// </summary>
    public int? PlacementId { get; set; }

    /// <summary>
    /// Gets or sets a value indicating whether an unsupported protocol should throw instead of producing no output.
    /// </summary>
    public bool ThrowIfUnsupported { get; set; }

    /// <summary>
    /// Gets or sets fallback text to write when no graphics protocol is available.
    /// </summary>
    /// <remarks>
    /// The fallback is ignored when <see cref="ThrowIfUnsupported"/> is enabled and no protocol is available.
    /// </remarks>
    public string? FallbackText { get; set; }

    /// <summary>
    /// Gets or sets a value indicating whether to reserve the encoded image's cell area before writing the payload.
    /// </summary>
    /// <remarks>
    /// When enabled, the writer clears the target cell rectangle, writes the image at its top-left corner, and leaves the
    /// cursor on the line after the image region. Leave this disabled for advanced callers that manage cursor placement
    /// themselves.
    /// </remarks>
    public bool ReserveCellArea { get; set; }

    /// <summary>
    /// Gets or sets the rasterizer to use.
    /// </summary>
    public ITerminalImageRasterizer? Rasterizer { get; set; }

    /// <summary>
    /// Gets or sets an optional encoded-image cache.
    /// </summary>
    public TerminalImageMemoryCache? Cache { get; set; }

    /// <summary>
    /// Gets or sets Sixel-specific encoding options.
    /// </summary>
    public TerminalSixelEncoderOptions? SixelOptions { get; set; }
}

/// <summary>
/// Provides direct terminal image output helpers.
/// </summary>
public static class TerminalGraphics
{
    /// <summary>
    /// Writes an image to the global terminal instance.
    /// </summary>
    /// <param name="source">The image source.</param>
    /// <param name="options">Write options, or <see langword="null"/> for defaults.</param>
    /// <param name="cancellationToken">A cancellation token.</param>
    /// <returns>The encoded image that was written, or <see langword="null"/> when no supported protocol is available.</returns>
    /// <exception cref="ArgumentNullException"><paramref name="source"/> is <see langword="null"/>.</exception>
    public static ValueTask<TerminalEncodedImage?> WriteImageAsync(TerminalImageSource source, TerminalImageWriteOptions? options = null, CancellationToken cancellationToken = default)
        => global::XenoAtom.Terminal.Terminal.Instance.WriteImageAsync(source, options, cancellationToken);
}

/// <summary>
/// Extension methods for writing terminal graphics directly through <see cref="TerminalInstance"/>.
/// </summary>
public static class TerminalGraphicsExtensions
{
    /// <summary>
    /// Writes an image to the terminal using the selected or detected graphics protocol.
    /// </summary>
    /// <param name="terminal">The terminal instance.</param>
    /// <param name="source">The image source.</param>
    /// <param name="options">Write options, or <see langword="null"/> for defaults.</param>
    /// <param name="cancellationToken">A cancellation token.</param>
    /// <returns>The encoded image that was written, or <see langword="null"/> when no supported protocol is available.</returns>
    /// <exception cref="ArgumentNullException"><paramref name="terminal"/> or <paramref name="source"/> is <see langword="null"/>.</exception>
    /// <exception cref="InvalidOperationException">No supported graphics protocol is available and <see cref="TerminalImageWriteOptions.ThrowIfUnsupported"/> is enabled.</exception>
    public static async ValueTask<TerminalEncodedImage?> WriteImageAsync(this TerminalInstance terminal, TerminalImageSource source, TerminalImageWriteOptions? options = null, CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(terminal);
        ArgumentNullException.ThrowIfNull(source);

        options ??= new TerminalImageWriteOptions();
        var protocol = options.Protocol != TerminalGraphicsProtocol.None
            ? options.Protocol
            : terminal.Graphics.Capabilities.PreferredProtocol;

        if (protocol == TerminalGraphicsProtocol.None)
        {
            if (options.ThrowIfUnsupported)
            {
                throw new InvalidOperationException("No terminal graphics protocol is available for direct image output.");
            }

            WriteFallbackText(terminal, options.FallbackText);
            return null;
        }

        var rasterizer = options.Rasterizer ?? TerminalImageRasterizer.Default;
        var frameRequest = TerminalImageFrameRequest.Default;
        await using var frame = await source.GetFrameAsync(frameRequest, cancellationToken).ConfigureAwait(false);
        if (frame is null)
        {
            return null;
        }

        var pixelMetrics = options.PixelMetrics ?? terminal.Graphics.Capabilities.PixelMetrics;
        var pixelSize = ResolvePixelSize(frame, options, pixelMetrics);
        var cellSize = ResolveCellSize(pixelSize, options, pixelMetrics);
        var encodeRequest = new TerminalImageEncodeRequest(
            protocol,
            pixelSize,
            cellSize,
            pixelMetrics,
            options.ScaleMode,
            options.MatteColor,
            options.PreserveAspectRatio,
            options.Quality,
            options.MaxPayloadChunkBytes,
            options.ImageId,
            options.PlacementId);

        var service = new TerminalImageEncodingService(rasterizer, options.SixelOptions);
        var encoded = await service.EncodeFrameAsync(frame, encodeRequest, options.Cache, cancellationToken).ConfigureAwait(false);

        terminal.WriteAtomic(writer => WriteDirectImage(writer, encoded, options));
        terminal.Flush();
        return encoded;
    }

    /// <summary>
    /// Writes an already encoded terminal image payload to an ANSI writer.
    /// </summary>
    /// <param name="writer">The ANSI writer.</param>
    /// <param name="image">The encoded image.</param>
    /// <param name="maxPayloadChunkBytes">The maximum Kitty payload chunk size. Values less than one use the encoded image chunk size or the default.</param>
    /// <exception cref="ArgumentNullException"><paramref name="writer"/> or <paramref name="image"/> is <see langword="null"/>.</exception>
    /// <exception cref="ArgumentOutOfRangeException"><paramref name="image"/> uses an unsupported protocol.</exception>
    public static void WriteEncodedImage(AnsiWriter writer, TerminalEncodedImage image, int maxPayloadChunkBytes = 0)
    {
        ArgumentNullException.ThrowIfNull(writer);
        ArgumentNullException.ThrowIfNull(image);

        switch (image.Protocol)
        {
            case TerminalGraphicsProtocol.Kitty:
                var chunkSize = maxPayloadChunkBytes > 0 ? maxPayloadChunkBytes : ResolveEncodedChunkSize(image);
                AnsiKittyGraphicsSequences.WriteCommandChunks(writer, image.Parameters, image.ContinuationParameters, image.GetPayloadText().AsSpan(), chunkSize);
                break;
            case TerminalGraphicsProtocol.ITerm2:
                AnsiIterm2ImageSequences.WriteFile(writer, image.Parameters, image.GetPayloadText().AsSpan());
                break;
            case TerminalGraphicsProtocol.Sixel:
                AnsiSixelSequences.WriteImage(writer, image.Parameters, image.GetPayloadText().AsSpan());
                break;
            default:
                throw new ArgumentOutOfRangeException(nameof(image), "The encoded image protocol is not supported for terminal output.");
        }
    }

    private static void WriteFallbackText(TerminalInstance terminal, string? fallbackText)
    {
        if (string.IsNullOrEmpty(fallbackText))
        {
            return;
        }

        terminal.WriteAtomic((TextWriter writer) => writer.Write(fallbackText));
        terminal.Flush();
    }

    private static void WriteDirectImage(AnsiWriter writer, TerminalEncodedImage image, TerminalImageWriteOptions options)
    {
        if (!options.ReserveCellArea)
        {
            WriteEncodedImage(writer, image, options.MaxPayloadChunkBytes);
            return;
        }

        writer.PrivateMode(2026, enabled: true);
        writer.SaveCursor();
        ClearDirectImageRegion(writer, image.CellSize);
        writer.RestoreCursor();
        WriteEncodedImage(writer, image, options.MaxPayloadChunkBytes);
        writer.RestoreCursor();
        writer.NextLine(Math.Max(1, image.CellSize.Height));
        writer.PrivateMode(2026, enabled: false);
    }

    private static void ClearDirectImageRegion(AnsiWriter writer, TerminalImageSize cellSize)
    {
        var width = Math.Max(1, cellSize.Width);
        var height = Math.Max(1, cellSize.Height);
        var rented = ArrayPool<char>.Shared.Rent(Math.Min(width, 256));
        try
        {
            var spaces = rented.AsSpan(0, Math.Min(width, rented.Length));
            spaces.Fill(' ');
            for (var y = 0; y < height; y++)
            {
                WriteSpaces(writer, spaces, width);
                if (y < height - 1)
                {
                    writer.NextLine();
                }
            }
        }
        finally
        {
            ArrayPool<char>.Shared.Return(rented);
        }
    }

    private static void WriteSpaces(AnsiWriter writer, ReadOnlySpan<char> spaces, int count)
    {
        while (count > 0)
        {
            var chunk = Math.Min(count, spaces.Length);
            writer.Write(spaces[..chunk]);
            count -= chunk;
        }
    }

    private static int ResolveEncodedChunkSize(TerminalEncodedImage image)
    {
        if (image.Chunks.Count > 0)
        {
            return image.Chunks[0].Length;
        }

        return AnsiKittyGraphicsSequences.DefaultMaxPayloadChunkChars;
    }

    private static TerminalImageSize ResolvePixelSize(TerminalImageFrame frame, TerminalImageWriteOptions options, TerminalPixelMetrics? pixelMetrics)
    {
        if (!options.PixelSize.IsEmpty)
        {
            return options.PixelSize;
        }

        if (!options.CellSize.IsEmpty && pixelMetrics is { } metrics)
        {
            return new TerminalImageSize(Math.Max(1, options.CellSize.Width * metrics.CellPixelWidth), Math.Max(1, options.CellSize.Height * metrics.CellPixelHeight));
        }

        return new TerminalImageSize(frame.PixelWidth, frame.PixelHeight);
    }

    private static TerminalImageSize ResolveCellSize(TerminalImageSize pixelSize, TerminalImageWriteOptions options, TerminalPixelMetrics? pixelMetrics)
    {
        if (!options.CellSize.IsEmpty)
        {
            return options.CellSize;
        }

        if (pixelMetrics is { } metrics && metrics.CellPixelWidth > 0 && metrics.CellPixelHeight > 0)
        {
            return new TerminalImageSize(Math.Max(1, (int)Math.Ceiling(pixelSize.Width / (double)metrics.CellPixelWidth)), Math.Max(1, (int)Math.Ceiling(pixelSize.Height / (double)metrics.CellPixelHeight)));
        }

        return new TerminalImageSize(Math.Max(1, pixelSize.Width), Math.Max(1, pixelSize.Height));
    }
}
