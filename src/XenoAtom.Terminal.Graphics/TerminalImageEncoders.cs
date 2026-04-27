// Copyright (c) Alexandre Mutel. All rights reserved.
// Licensed under the BSD-Clause 2 license.
// See license.txt file in the project root for full license information.

using System.Buffers;
using System.Buffers.Text;
using System.Globalization;
using System.Runtime.InteropServices;
using System.Text;
using SkiaSharp;
using XenoAtom.Terminal;

namespace XenoAtom.Terminal.Graphics;

/// <summary>
/// Creates protocol-specific terminal image encoders.
/// </summary>
public static class TerminalImageEncoders
{
    /// <summary>
    /// Creates an encoder for the specified graphics protocol.
    /// </summary>
    /// <param name="protocol">The terminal graphics protocol.</param>
    /// <param name="rasterizer">The rasterizer to use, or <see langword="null"/> to use the default SkiaSharp-backed rasterizer.</param>
    /// <returns>The encoder.</returns>
    /// <exception cref="ArgumentOutOfRangeException"><paramref name="protocol"/> is not supported by this package.</exception>
    public static ITerminalImageEncoder Create(TerminalGraphicsProtocol protocol, ITerminalImageRasterizer? rasterizer = null) => protocol switch
    {
        TerminalGraphicsProtocol.Kitty => new KittyTerminalImageEncoder(rasterizer),
        TerminalGraphicsProtocol.ITerm2 => new ITerm2TerminalImageEncoder(rasterizer),
        TerminalGraphicsProtocol.Sixel => new SixelTerminalImageEncoder(rasterizer),
        _ => throw new ArgumentOutOfRangeException(nameof(protocol), "The specified terminal graphics protocol is not supported for image encoding."),
    };

    internal static byte[] ToBase64Ascii(ReadOnlySpan<byte> bytes)
    {
        if (bytes.IsEmpty)
        {
            return [];
        }

        var length = checked(((bytes.Length + 2) / 3) * 4);
        var result = GC.AllocateUninitializedArray<byte>(length);
        var status = Base64.EncodeToUtf8(bytes, result, out var consumed, out var written);
        if (status != OperationStatus.Done || consumed != bytes.Length || written != length)
        {
            throw new InvalidOperationException("Unable to encode terminal image payload as Base64.");
        }

        return result;
    }

    internal static byte[] ToAsciiBytes(StringBuilder builder)
    {
        var length = builder.Length;
        if (length == 0)
        {
            return [];
        }

        var result = GC.AllocateUninitializedArray<byte>(length);
        var offset = 0;
        foreach (var chunk in builder.GetChunks())
        {
            var span = chunk.Span;
            for (var i = 0; i < span.Length; i++)
            {
                result[offset + i] = (byte)span[i];
            }

            offset += span.Length;
        }

        return result;
    }
}

/// <summary>
/// Coordinates image source, encoding, caching, cancellation, and latest-wins frame dropping.
/// </summary>
public sealed class TerminalImageEncodingService
{
    private readonly ITerminalImageRasterizer _rasterizer;
    private readonly TerminalSixelEncoderOptions? _sixelOptions;
    private KittyTerminalImageEncoder? _kittyEncoder;
    private ITerm2TerminalImageEncoder? _iterm2Encoder;
    private SixelTerminalImageEncoder? _sixelEncoder;
    private long _latestRequestId;

    /// <summary>
    /// Initializes a new instance of the <see cref="TerminalImageEncodingService"/> class.
    /// </summary>
    /// <param name="rasterizer">The rasterizer to use, or <see langword="null"/> to use the default rasterizer.</param>
    /// <param name="sixelOptions">Options for Sixel encoding, or <see langword="null"/> for defaults.</param>
    public TerminalImageEncodingService(ITerminalImageRasterizer? rasterizer = null, TerminalSixelEncoderOptions? sixelOptions = null)
    {
        _rasterizer = rasterizer ?? TerminalImageRasterizer.Default;
        _sixelOptions = sixelOptions;
    }

    /// <summary>
    /// Encodes a frame from an image source.
    /// </summary>
    /// <param name="source">The image source.</param>
    /// <param name="frameRequest">The frame request.</param>
    /// <param name="encodeRequest">The encode request.</param>
    /// <param name="cache">An optional encoded-image cache.</param>
    /// <param name="cancellationToken">A cancellation token.</param>
    /// <returns>The encoded image, or <see langword="null"/> when no frame is available.</returns>
    /// <exception cref="ArgumentNullException"><paramref name="source"/> is <see langword="null"/>.</exception>
    public async ValueTask<TerminalEncodedImage?> EncodeAsync(
        TerminalImageSource source,
        TerminalImageFrameRequest frameRequest,
        TerminalImageEncodeRequest encodeRequest,
        TerminalImageMemoryCache? cache = null,
        CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(source);
        await using var frame = await source.GetFrameAsync(frameRequest, cancellationToken).ConfigureAwait(false);
        if (frame is null)
        {
            return null;
        }

        return await EncodeFrameAsync(frame, encodeRequest, cache, cancellationToken).ConfigureAwait(false);
    }

    /// <summary>
    /// Encodes a frame from an image source and drops the result if a newer latest-wins request starts before it completes.
    /// </summary>
    /// <param name="source">The image source.</param>
    /// <param name="frameRequest">The frame request.</param>
    /// <param name="encodeRequest">The encode request.</param>
    /// <param name="cache">An optional encoded-image cache.</param>
    /// <param name="cancellationToken">A cancellation token.</param>
    /// <returns>The encoded image, <see langword="null"/> when no frame is available, or <see langword="null"/> when superseded by a newer request.</returns>
    /// <exception cref="ArgumentNullException"><paramref name="source"/> is <see langword="null"/>.</exception>
    public async ValueTask<TerminalEncodedImage?> EncodeLatestAsync(
        TerminalImageSource source,
        TerminalImageFrameRequest frameRequest,
        TerminalImageEncodeRequest encodeRequest,
        TerminalImageMemoryCache? cache = null,
        CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(source);
        var requestId = Interlocked.Increment(ref _latestRequestId);
        await using var frame = await source.GetFrameAsync(frameRequest, cancellationToken).ConfigureAwait(false);
        if (frame is null || requestId != Volatile.Read(ref _latestRequestId))
        {
            return null;
        }

        var encoded = await EncodeFrameAsync(frame, encodeRequest, cache, cancellationToken).ConfigureAwait(false);
        return requestId == Volatile.Read(ref _latestRequestId) ? encoded : null;
    }

    internal async ValueTask<TerminalEncodedImage> EncodeFrameAsync(TerminalImageFrame frame, TerminalImageEncodeRequest encodeRequest, TerminalImageMemoryCache? cache, CancellationToken cancellationToken)
    {
        cancellationToken.ThrowIfCancellationRequested();
        var request = NormalizeRequest(frame, encodeRequest);
        var encoder = GetEncoder(request.Protocol);
        var expectedCacheKey = TerminalImageCacheKeys.Create(frame, request, EncoderCacheName(request.Protocol));
        if (cache is not null && cache.TryGetEncoded(expectedCacheKey, out var cached))
        {
            return cached;
        }

        var encoded = await encoder.EncodeAsync(frame, request, cancellationToken).ConfigureAwait(false);
        if (cache is not null)
        {
            cache.StoreEncoded(encoded);
        }

        return encoded;
    }

    private static TerminalImageEncodeRequest NormalizeRequest(TerminalImageFrame frame, TerminalImageEncodeRequest request)
    {
        var pixelSize = request.PixelSize.IsEmpty ? new TerminalImageSize(frame.PixelWidth, frame.PixelHeight) : request.PixelSize;
        var cellSize = request.CellSize.IsEmpty ? new TerminalImageSize(Math.Max(1, pixelSize.Width), Math.Max(1, pixelSize.Height)) : request.CellSize;
        var matteColor = request.Protocol == TerminalGraphicsProtocol.Sixel && request.MatteColor is null
            ? TerminalImageColor.Black
            : request.MatteColor;
        return request with
        {
            PixelSize = pixelSize,
            CellSize = cellSize,
            MatteColor = matteColor,
            MaxPayloadChunkBytes = request.NormalizedMaxPayloadChunkBytes,
        };
    }

    private string EncoderCacheName(TerminalGraphicsProtocol protocol) => protocol switch
    {
        TerminalGraphicsProtocol.Kitty => KittyTerminalImageEncoder.CacheName,
        TerminalGraphicsProtocol.ITerm2 => ITerm2TerminalImageEncoder.CacheName,
        TerminalGraphicsProtocol.Sixel => SixelTerminalImageEncoder.CacheNameForOptions(_sixelOptions),
        _ => protocol.ToString(),
    };

    private ITerminalImageEncoder GetEncoder(TerminalGraphicsProtocol protocol) => protocol switch
    {
        TerminalGraphicsProtocol.Kitty => _kittyEncoder ??= new KittyTerminalImageEncoder(_rasterizer),
        TerminalGraphicsProtocol.ITerm2 => _iterm2Encoder ??= new ITerm2TerminalImageEncoder(_rasterizer),
        TerminalGraphicsProtocol.Sixel => _sixelEncoder ??= new SixelTerminalImageEncoder(_rasterizer, _sixelOptions),
        _ => throw new ArgumentOutOfRangeException(nameof(protocol), "The specified terminal graphics protocol is not supported for image encoding."),
    };
}

/// <summary>
/// Encodes images for the Kitty graphics protocol.
/// </summary>
public sealed class KittyTerminalImageEncoder : ITerminalImageEncoder
{
    internal static string CacheName => "kitty-v1|" + TerminalImageRasterizer.BackendVersion;
    private readonly ITerminalImageRasterizer _rasterizer;

    /// <summary>
    /// Initializes a new instance of the <see cref="KittyTerminalImageEncoder"/> class.
    /// </summary>
    /// <param name="rasterizer">The rasterizer to use, or <see langword="null"/> to use the default rasterizer.</param>
    public KittyTerminalImageEncoder(ITerminalImageRasterizer? rasterizer = null)
    {
        _rasterizer = rasterizer ?? TerminalImageRasterizer.Default;
    }

    /// <inheritdoc />
    public TerminalGraphicsProtocol Protocol => TerminalGraphicsProtocol.Kitty;

    /// <inheritdoc />
    public async ValueTask<TerminalEncodedImage> EncodeAsync(TerminalImageFrame frame, TerminalImageEncodeRequest request, CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(frame);
        cancellationToken.ThrowIfCancellationRequested();

        var pixelSize = ResolvePixelSize(frame, request.PixelSize);
        var cellSize = ResolveCellSize(pixelSize, request.CellSize);
        byte[] payloadBytes;
        int kittyFormat;

        if (CanPassThroughPng(frame, pixelSize, request))
        {
            payloadBytes = frame.Data.ToArray();
            kittyFormat = 100;
        }
        else
        {
            await using var raster = await _rasterizer.RasterizeAsync(frame, new TerminalRasterizeRequest(pixelSize, request.ScaleMode, request.PreserveAspectRatio, request.MatteColor, request.Quality), cancellationToken).ConfigureAwait(false);
            payloadBytes = CopyTightRgba(raster);
            kittyFormat = 32;
        }

        var payloadUtf8 = TerminalImageEncoders.ToBase64Ascii(payloadBytes);
        var parameters = BuildKittyParameters("a=T", kittyFormat, pixelSize, cellSize, request.ImageId, request.PlacementId);
        var continuationParameters = request.ImageId.HasValue ? "i=" + request.ImageId.Value.ToString(CultureInfo.InvariantCulture) : string.Empty;
        var normalizedRequest = request with { PixelSize = pixelSize, CellSize = cellSize, Protocol = Protocol, MaxPayloadChunkBytes = request.NormalizedMaxPayloadChunkBytes };
        return new TerminalEncodedImage
        {
            Protocol = Protocol,
            PixelWidth = pixelSize.Width,
            PixelHeight = pixelSize.Height,
            CellSize = cellSize,
            Parameters = parameters,
            ContinuationParameters = continuationParameters,
            PayloadUtf8 = payloadUtf8,
            Chunks = TerminalPayloadChunker.Chunk(payloadUtf8, normalizedRequest.NormalizedMaxPayloadChunkBytes),
            CacheKey = TerminalImageCacheKeys.Create(frame, normalizedRequest, CacheName),
        };
    }

    private static bool CanPassThroughPng(TerminalImageFrame frame, TerminalImageSize pixelSize, TerminalImageEncodeRequest request)
        => frame.Format == TerminalImageFormat.Png
        && frame.PixelWidth == pixelSize.Width
        && frame.PixelHeight == pixelSize.Height
        && request.MatteColor is null
        && (request.ScaleMode == TerminalImageScaleMode.Stretch || request.ScaleMode == TerminalImageScaleMode.Fit || request.ScaleMode == TerminalImageScaleMode.Center);

    private static string BuildKittyParameters(string action, int format, TerminalImageSize pixelSize, TerminalImageSize cellSize, int? imageId, int? placementId)
    {
        var builder = new StringBuilder(96);
        builder.Append(action);
        builder.Append(",f=").Append(format.ToString(CultureInfo.InvariantCulture));
        builder.Append(",s=").Append(pixelSize.Width.ToString(CultureInfo.InvariantCulture));
        builder.Append(",v=").Append(pixelSize.Height.ToString(CultureInfo.InvariantCulture));
        builder.Append(",c=").Append(cellSize.Width.ToString(CultureInfo.InvariantCulture));
        builder.Append(",r=").Append(cellSize.Height.ToString(CultureInfo.InvariantCulture));
        if (imageId.HasValue)
        {
            builder.Append(",i=").Append(imageId.Value.ToString(CultureInfo.InvariantCulture));
        }

        if (placementId.HasValue)
        {
            builder.Append(",p=").Append(placementId.Value.ToString(CultureInfo.InvariantCulture));
        }

        return builder.ToString();
    }

    private static byte[] CopyTightRgba(TerminalRasterImage raster)
    {
        if (raster.PixelFormat != TerminalPixelFormat.Rgba32)
        {
            throw new InvalidOperationException("The Kitty raw encoder expects RGBA raster pixels.");
        }

        var tightStride = checked(raster.PixelWidth * 4);
        if (raster.StrideBytes == tightStride)
        {
            return raster.PixelBytes[..checked(tightStride * raster.PixelHeight)].ToArray();
        }

        var result = new byte[checked(tightStride * raster.PixelHeight)];
        var source = raster.PixelBytes.Span;
        for (var y = 0; y < raster.PixelHeight; y++)
        {
            source.Slice(y * raster.StrideBytes, tightStride).CopyTo(result.AsSpan(y * tightStride, tightStride));
        }

        return result;
    }

    internal static TerminalImageSize ResolvePixelSize(TerminalImageFrame frame, TerminalImageSize requestedSize)
        => requestedSize.IsEmpty ? new TerminalImageSize(frame.PixelWidth, frame.PixelHeight) : requestedSize;

    internal static TerminalImageSize ResolveCellSize(TerminalImageSize pixelSize, TerminalImageSize requestedCellSize)
        => requestedCellSize.IsEmpty ? new TerminalImageSize(Math.Max(1, pixelSize.Width), Math.Max(1, pixelSize.Height)) : requestedCellSize;
}

/// <summary>
/// Encodes images for the iTerm2 inline image protocol.
/// </summary>
public sealed class ITerm2TerminalImageEncoder : ITerminalImageEncoder
{
    internal static string CacheName => "iterm2-v1|" + TerminalImageRasterizer.BackendVersion;
    private readonly ITerminalImageRasterizer _rasterizer;

    /// <summary>
    /// Initializes a new instance of the <see cref="ITerm2TerminalImageEncoder"/> class.
    /// </summary>
    /// <param name="rasterizer">The rasterizer to use, or <see langword="null"/> to use the default rasterizer.</param>
    public ITerm2TerminalImageEncoder(ITerminalImageRasterizer? rasterizer = null)
    {
        _rasterizer = rasterizer ?? TerminalImageRasterizer.Default;
    }

    /// <inheritdoc />
    public TerminalGraphicsProtocol Protocol => TerminalGraphicsProtocol.ITerm2;

    /// <inheritdoc />
    public async ValueTask<TerminalEncodedImage> EncodeAsync(TerminalImageFrame frame, TerminalImageEncodeRequest request, CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(frame);
        cancellationToken.ThrowIfCancellationRequested();

        var pixelSize = KittyTerminalImageEncoder.ResolvePixelSize(frame, request.PixelSize);
        var cellSize = KittyTerminalImageEncoder.ResolveCellSize(pixelSize, request.CellSize);
        byte[] fileBytes;

        if (CanPassThroughEncoded(frame, pixelSize, request))
        {
            fileBytes = frame.Data.ToArray();
        }
        else
        {
            await using var raster = await _rasterizer.RasterizeAsync(frame, new TerminalRasterizeRequest(pixelSize, request.ScaleMode, request.PreserveAspectRatio, request.MatteColor, request.Quality), cancellationToken).ConfigureAwait(false);
            fileBytes = SkiaPngImageEncoder.EncodeRgbaToPng(raster);
        }

        var payloadUtf8 = TerminalImageEncoders.ToBase64Ascii(fileBytes);
        var parameters = BuildFileParameters(fileBytes.Length, pixelSize, request.PreserveAspectRatio);
        var normalizedRequest = request with { PixelSize = pixelSize, CellSize = cellSize, Protocol = Protocol, MaxPayloadChunkBytes = request.NormalizedMaxPayloadChunkBytes };
        return new TerminalEncodedImage
        {
            Protocol = Protocol,
            PixelWidth = pixelSize.Width,
            PixelHeight = pixelSize.Height,
            CellSize = cellSize,
            Parameters = parameters,
            PayloadUtf8 = payloadUtf8,
            Chunks = TerminalPayloadChunker.Chunk(payloadUtf8, normalizedRequest.NormalizedMaxPayloadChunkBytes),
            CacheKey = TerminalImageCacheKeys.Create(frame, normalizedRequest, CacheName),
        };
    }

    private static bool CanPassThroughEncoded(TerminalImageFrame frame, TerminalImageSize pixelSize, TerminalImageEncodeRequest request)
        => frame.Format is TerminalImageFormat.Png or TerminalImageFormat.Jpeg or TerminalImageFormat.Gif
        && frame.PixelWidth == pixelSize.Width
        && frame.PixelHeight == pixelSize.Height
        && request.MatteColor is null
        && (request.ScaleMode == TerminalImageScaleMode.Stretch || request.ScaleMode == TerminalImageScaleMode.Fit || request.ScaleMode == TerminalImageScaleMode.Center);

    private static string BuildFileParameters(int byteLength, TerminalImageSize pixelSize, bool preserveAspectRatio)
        => string.Create(CultureInfo.InvariantCulture, $"inline=1;size={byteLength};width={pixelSize.Width}px;height={pixelSize.Height}px;preserveAspectRatio={(preserveAspectRatio ? 1 : 0)}");
}

/// <summary>
/// Selects the palette strategy used by <see cref="SixelTerminalImageEncoder"/>.
/// </summary>
public enum TerminalSixelPaletteMode
{
    /// <summary>
    /// Builds an adaptive palette from the image contents.
    /// </summary>
    Adaptive = 0,

    /// <summary>
    /// Uses a fixed 256-color RGB332 palette. This trades color precision for much faster quantization, especially when
    /// dithering is disabled.
    /// </summary>
    FixedRgb332 = 1,
}

/// <summary>
/// Options for the built-in deterministic Sixel encoder.
/// </summary>
public sealed class TerminalSixelEncoderOptions
{
    /// <summary>
    /// Gets or sets the maximum number of palette colors.
    /// </summary>
    public int MaxColors { get; set; } = 256;

    /// <summary>
    /// Gets or sets the palette strategy.
    /// </summary>
    /// <remarks>
    /// <see cref="TerminalSixelPaletteMode.FixedRgb332"/> always uses 256 colors and ignores <see cref="MaxColors"/>.
    /// Disable <see cref="EnableDithering"/> with that mode for maximum throughput.
    /// </remarks>
    public TerminalSixelPaletteMode PaletteMode { get; set; } = TerminalSixelPaletteMode.Adaptive;

    /// <summary>
    /// Gets or sets a value indicating whether Sixel run-length encoding should be used.
    /// </summary>
    public bool UseRunLengthEncoding { get; set; } = true;

    /// <summary>
    /// Gets or sets a value indicating whether palette quantization should use Floyd-Steinberg error diffusion.
    /// </summary>
    /// <remarks>
    /// The default is <see langword="false"/> because dithering is substantially more expensive than nearest-palette
    /// quantization and is usually only appropriate for one-shot/static Sixel output where the extra quality is worth the
    /// encode cost.
    /// </remarks>
    public bool EnableDithering { get; set; }
}

/// <summary>
/// Encodes images for DEC Sixel.
/// </summary>
public sealed class SixelTerminalImageEncoder : ITerminalImageEncoder
{
    private const string DefaultDcsParameters = "0;1";

    internal static string CacheName => "sixel-v2|" + TerminalImageRasterizer.BackendVersion;
    internal static string DefaultCacheName => CreateCacheName(256, useRunLengthEncoding: true, enableDithering: false);
    internal static string CacheNameForOptions(TerminalSixelEncoderOptions? options)
        => options is null
            ? DefaultCacheName
            : CreateCacheName(options.MaxColors, options.UseRunLengthEncoding, options.EnableDithering, options.PaletteMode);

    private readonly ITerminalImageRasterizer _rasterizer;
    private readonly TerminalSixelEncoderOptions _options;

    /// <summary>
    /// Initializes a new instance of the <see cref="SixelTerminalImageEncoder"/> class.
    /// </summary>
    /// <param name="rasterizer">The rasterizer to use, or <see langword="null"/> to use the default rasterizer.</param>
    /// <param name="options">The Sixel encoder options, or <see langword="null"/> for defaults.</param>
    public SixelTerminalImageEncoder(ITerminalImageRasterizer? rasterizer = null, TerminalSixelEncoderOptions? options = null)
    {
        _rasterizer = rasterizer ?? TerminalImageRasterizer.Default;
        _options = options ?? new TerminalSixelEncoderOptions();
    }

    /// <inheritdoc />
    public TerminalGraphicsProtocol Protocol => TerminalGraphicsProtocol.Sixel;

    /// <inheritdoc />
    public async ValueTask<TerminalEncodedImage> EncodeAsync(TerminalImageFrame frame, TerminalImageEncodeRequest request, CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(frame);
        cancellationToken.ThrowIfCancellationRequested();
        if (_options.PaletteMode == TerminalSixelPaletteMode.Adaptive && (_options.MaxColors is < 2 or > 256))
        {
            throw new InvalidOperationException("Sixel MaxColors must be between 2 and 256.");
        }

        var pixelSize = KittyTerminalImageEncoder.ResolvePixelSize(frame, request.PixelSize);
        var cellSize = KittyTerminalImageEncoder.ResolveCellSize(pixelSize, request.CellSize);
        var matte = request.MatteColor ?? TerminalImageColor.Black;
        await using var raster = await _rasterizer.RasterizeAsync(frame, new TerminalRasterizeRequest(pixelSize, request.ScaleMode, request.PreserveAspectRatio, matte, request.Quality), cancellationToken).ConfigureAwait(false);
        var payloadUtf8 = EncodeSixelPayload(raster, _options);
        var normalizedRequest = request with { PixelSize = pixelSize, CellSize = cellSize, MatteColor = matte, Protocol = Protocol, MaxPayloadChunkBytes = request.NormalizedMaxPayloadChunkBytes };
        var cacheName = CreateCacheName(_options.MaxColors, _options.UseRunLengthEncoding, _options.EnableDithering, _options.PaletteMode);
        return new TerminalEncodedImage
        {
            Protocol = Protocol,
            PixelWidth = pixelSize.Width,
            PixelHeight = pixelSize.Height,
            CellSize = cellSize,
            Parameters = DefaultDcsParameters,
            PayloadUtf8 = payloadUtf8,
            Chunks = TerminalPayloadChunker.Chunk(payloadUtf8, normalizedRequest.NormalizedMaxPayloadChunkBytes),
            CacheKey = TerminalImageCacheKeys.Create(frame, normalizedRequest, cacheName),
        };
    }

    private static string CreateCacheName(int maxColors, bool useRunLengthEncoding, bool enableDithering, TerminalSixelPaletteMode paletteMode = TerminalSixelPaletteMode.Adaptive)
    {
        var effectiveMaxColors = paletteMode == TerminalSixelPaletteMode.FixedRgb332 ? 256 : maxColors;
        var baseName = string.Create(
            CultureInfo.InvariantCulture,
            $"{CacheName}|colors={effectiveMaxColors}|rle={useRunLengthEncoding}|dither={enableDithering}");
        return paletteMode == TerminalSixelPaletteMode.Adaptive ? baseName : baseName + "|palette=rgb332";
    }

    private static readonly List<TerminalImageColor> FixedRgb332Palette = CreateFixedRgb332Palette();

    private static List<TerminalImageColor> CreateFixedRgb332Palette()
    {
        var palette = new List<TerminalImageColor>(256);
        for (var red = 0; red < 8; red++)
        {
            for (var green = 0; green < 8; green++)
            {
                for (var blue = 0; blue < 4; blue++)
                {
                    palette.Add(new TerminalImageColor(
                        ScaleFixedPaletteChannel(red, 7),
                        ScaleFixedPaletteChannel(green, 7),
                        ScaleFixedPaletteChannel(blue, 3)));
                }
            }
        }

        return palette;
    }

    private static byte ScaleFixedPaletteChannel(int value, int maxValue)
        => (byte)((value * 255 + (maxValue / 2)) / maxValue);

    private static byte[] EncodeSixelPayload(TerminalRasterImage raster, TerminalSixelEncoderOptions options)
    {
        var palette = options.PaletteMode == TerminalSixelPaletteMode.FixedRgb332
            ? FixedRgb332Palette
            : BuildPalette(raster, options.MaxColors);
        var width = raster.PixelWidth;
        var height = raster.PixelHeight;
        var pixelCount = checked(width * height);
        var indexedPixels = ArrayPool<byte>.Shared.Rent(pixelCount);
        var colorUsed = ArrayPool<bool>.Shared.Rent(palette.Count);
        var bandSixels = ArrayPool<byte>.Shared.Rent(checked(palette.Count * width));
        var usedPaletteIndexes = ArrayPool<int>.Shared.Rent(palette.Count);

        var builder = new PooledAsciiByteBuffer(Math.Min(checked((palette.Count * 24) + Math.Max(width * 8, 4096)), 1 << 20));
        try
        {
            var indexedPixelSpan = indexedPixels.AsSpan(0, pixelCount);
            if (options.PaletteMode == TerminalSixelPaletteMode.FixedRgb332 && !options.EnableDithering)
            {
                QuantizePixelsRgb332(raster, indexedPixelSpan);
            }
            else
            {
                QuantizePixels(raster, palette, options.EnableDithering, indexedPixelSpan);
            }
            var colorUsedSpan = colorUsed.AsSpan(0, palette.Count);
            var bandSixelsSpan = bandSixels.AsSpan(0, checked(palette.Count * width));

            colorUsedSpan.Clear();
            for (var i = 0; i < indexedPixelSpan.Length; i++)
            {
                colorUsedSpan[indexedPixelSpan[i]] = true;
            }

            var usedPaletteCount = 0;
            for (var i = 0; i < palette.Count; i++)
            {
                if (colorUsedSpan[i])
                {
                    usedPaletteIndexes[usedPaletteCount++] = i;
                }
            }

            builder.Append((byte)'"').AppendAscii("1;1;").AppendInt(width).Append((byte)';').AppendInt(height);
            for (var usedPaletteIndex = 0; usedPaletteIndex < usedPaletteCount; usedPaletteIndex++)
            {
                var colorIndex = usedPaletteIndexes[usedPaletteIndex];
                var color = palette[colorIndex];
                builder.Append((byte)'#').AppendInt(colorIndex).AppendAscii(";2;")
                    .AppendInt(ToSixelPercent(color.R)).Append((byte)';')
                    .AppendInt(ToSixelPercent(color.G)).Append((byte)';')
                    .AppendInt(ToSixelPercent(color.B));
            }

            for (var bandY = 0; bandY < height; bandY += 6)
            {
                if (bandY > 0)
                {
                    builder.Append((byte)'-');
                }

                colorUsedSpan.Clear();
                if (usedPaletteCount * 2 >= palette.Count)
                {
                    bandSixelsSpan.Clear();
                }
                else
                {
                    for (var usedPaletteIndex = 0; usedPaletteIndex < usedPaletteCount; usedPaletteIndex++)
                    {
                        bandSixelsSpan.Slice(usedPaletteIndexes[usedPaletteIndex] * width, width).Clear();
                    }
                }

                for (var bit = 0; bit < 6; bit++)
                {
                    var y = bandY + bit;
                    if (y >= height)
                    {
                        break;
                    }

                    for (var x = 0; x < width; x++)
                    {
                        var colorIndex = indexedPixelSpan[(y * width) + x];
                        colorUsedSpan[colorIndex] = true;
                        bandSixelsSpan[(colorIndex * width) + x] |= (byte)(1 << bit);
                    }
                }

                var wroteColorInBand = false;
                for (var usedPaletteIndex = 0; usedPaletteIndex < usedPaletteCount; usedPaletteIndex++)
                {
                    var colorIndex = usedPaletteIndexes[usedPaletteIndex];
                    if (!colorUsedSpan[colorIndex])
                    {
                        continue;
                    }

                    if (wroteColorInBand)
                    {
                        builder.Append((byte)'$');
                    }

                    builder.Append((byte)'#').AppendInt(colorIndex);
                    byte last = 0;
                    var repeat = 0;
                    var colorOffset = colorIndex * width;
                    for (var x = 0; x < width; x++)
                    {
                        var sixel = (byte)(63 + bandSixelsSpan[colorOffset + x]);
                        AppendRun(builder, sixel, ref last, ref repeat, options.UseRunLengthEncoding);
                    }

                    FlushRun(builder, last, repeat, options.UseRunLengthEncoding);
                    wroteColorInBand = true;
                }
            }

            return builder.ToArray();
        }
        finally
        {
            builder.Dispose();
            ArrayPool<byte>.Shared.Return(indexedPixels);
            ArrayPool<bool>.Shared.Return(colorUsed);
            ArrayPool<byte>.Shared.Return(bandSixels);
            ArrayPool<int>.Shared.Return(usedPaletteIndexes);
        }
    }

    private static void QuantizePixels(TerminalRasterImage raster, List<TerminalImageColor> palette, bool enableDithering, Span<byte> pixels)
    {
        if (!enableDithering || palette.Count <= 1)
        {
            var paletteLookup = new Dictionary<int, int>(palette.Count);
            for (var i = 0; i < palette.Count; i++)
            {
                paletteLookup[palette[i].ToRgbKey()] = i;
            }

            var source = raster.PixelBytes.Span;
            var bytesPerPixel = TerminalRasterImage.GetBytesPerPixel(raster.PixelFormat);
            var nearestCacheKeys = ArrayPool<int>.Shared.Rent(NearestPaletteCacheSize);
            var nearestCacheValues = ArrayPool<byte>.Shared.Rent(NearestPaletteCacheSize);
            try
            {
                nearestCacheKeys.AsSpan(0, NearestPaletteCacheSize).Fill(-1);
                for (var y = 0; y < raster.PixelHeight; y++)
                {
                    var row = source.Slice(y * raster.StrideBytes);
                    var pixelRowOffset = y * raster.PixelWidth;
                    for (var x = 0; x < raster.PixelWidth; x++)
                    {
                        var offset = x * bytesPerPixel;
                        var pixel = new TerminalImageColor(row[offset], row[offset + 1], row[offset + 2]);
                        var rgbKey = pixel.ToRgbKey();
                        if (!paletteLookup.TryGetValue(rgbKey, out var actualIndex) &&
                            !TryGetCachedNearestPaletteIndex(nearestCacheKeys, nearestCacheValues, rgbKey, out actualIndex))
                        {
                            actualIndex = FindNearestPaletteIndex(pixel, palette);
                            SetCachedNearestPaletteIndex(nearestCacheKeys, nearestCacheValues, rgbKey, actualIndex);
                        }

                        pixels[pixelRowOffset + x] = (byte)actualIndex;
                    }
                }
            }
            finally
            {
                ArrayPool<int>.Shared.Return(nearestCacheKeys);
                ArrayPool<byte>.Shared.Return(nearestCacheValues);
            }

            return;
        }

        ApplyFloydSteinbergDither(raster, palette, pixels);
    }

    private static void QuantizePixelsRgb332(TerminalRasterImage raster, Span<byte> pixels)
    {
        var source = raster.PixelBytes.Span;
        var bytesPerPixel = TerminalRasterImage.GetBytesPerPixel(raster.PixelFormat);
        for (var y = 0; y < raster.PixelHeight; y++)
        {
            var row = source.Slice(y * raster.StrideBytes);
            var pixelRowOffset = y * raster.PixelWidth;
            for (var x = 0; x < raster.PixelWidth; x++)
            {
                var offset = x * bytesPerPixel;
                pixels[pixelRowOffset + x] = (byte)((row[offset] & 0xE0) | ((row[offset + 1] & 0xE0) >> 3) | (row[offset + 2] >> 6));
            }
        }
    }

    private static void ApplyFloydSteinbergDither(TerminalRasterImage raster, List<TerminalImageColor> palette, Span<byte> pixels)
    {
        var width = raster.PixelWidth;
        var height = raster.PixelHeight;
        var redBuffer = ArrayPool<float>.Shared.Rent(pixels.Length);
        var greenBuffer = ArrayPool<float>.Shared.Rent(pixels.Length);
        var blueBuffer = ArrayPool<float>.Shared.Rent(pixels.Length);
        try
        {
            var red = redBuffer.AsSpan(0, pixels.Length);
            var green = greenBuffer.AsSpan(0, pixels.Length);
            var blue = blueBuffer.AsSpan(0, pixels.Length);
            var source = raster.PixelBytes.Span;
            var bytesPerPixel = TerminalRasterImage.GetBytesPerPixel(raster.PixelFormat);

            for (var y = 0; y < height; y++)
            {
                var row = source.Slice(y * raster.StrideBytes);
                var rowIndex = y * width;
                for (var x = 0; x < width; x++)
                {
                    var index = rowIndex + x;
                    var offset = x * bytesPerPixel;
                    red[index] = row[offset];
                    green[index] = row[offset + 1];
                    blue[index] = row[offset + 2];
                }
            }

            for (var y = 0; y < height; y++)
            {
                for (var x = 0; x < width; x++)
                {
                    var index = (y * width) + x;
                    var oldColor = new TerminalImageColor(ClampToByte(red[index]), ClampToByte(green[index]), ClampToByte(blue[index]));
                    var paletteIndex = FindNearestPaletteIndex(oldColor, palette);
                    var newColor = palette[paletteIndex];
                    pixels[index] = (byte)paletteIndex;

                    var errorR = red[index] - newColor.R;
                    var errorG = green[index] - newColor.G;
                    var errorB = blue[index] - newColor.B;

                    AddDitherError(red, green, blue, width, height, x + 1, y, errorR, errorG, errorB, 7f / 16f);
                    AddDitherError(red, green, blue, width, height, x - 1, y + 1, errorR, errorG, errorB, 3f / 16f);
                    AddDitherError(red, green, blue, width, height, x, y + 1, errorR, errorG, errorB, 5f / 16f);
                    AddDitherError(red, green, blue, width, height, x + 1, y + 1, errorR, errorG, errorB, 1f / 16f);
                }
            }
        }
        finally
        {
            ArrayPool<float>.Shared.Return(redBuffer);
            ArrayPool<float>.Shared.Return(greenBuffer);
            ArrayPool<float>.Shared.Return(blueBuffer);
        }
    }

    private static void AddDitherError(Span<float> red, Span<float> green, Span<float> blue, int width, int height, int x, int y, float errorR, float errorG, float errorB, float factor)
    {
        if ((uint)x >= (uint)width || (uint)y >= (uint)height)
        {
            return;
        }

        var index = (y * width) + x;
        red[index] += errorR * factor;
        green[index] += errorG * factor;
        blue[index] += errorB * factor;
    }

    private static byte ClampToByte(float value)
        => (byte)Math.Clamp((int)MathF.Round(value), byte.MinValue, byte.MaxValue);

    private static List<TerminalImageColor> BuildPalette(TerminalRasterImage raster, int maxColors)
    {
        var exactPalette = new List<TerminalImageColor>(Math.Min(maxColors, 256));
        var seen = new HashSet<int>();
        var exceededExactPalette = false;
        var source = raster.PixelBytes.Span;
        var bytesPerPixel = TerminalRasterImage.GetBytesPerPixel(raster.PixelFormat);

        for (var y = 0; y < raster.PixelHeight && !exceededExactPalette; y++)
        {
            var row = source.Slice(y * raster.StrideBytes);
            for (var x = 0; x < raster.PixelWidth; x++)
            {
                var offset = x * bytesPerPixel;
                var rgbKey = ToRgbKey(row[offset], row[offset + 1], row[offset + 2]);
                if (!seen.Add(rgbKey))
                {
                    continue;
                }

                if (exactPalette.Count < maxColors)
                {
                    exactPalette.Add(new TerminalImageColor(row[offset], row[offset + 1], row[offset + 2]));
                    continue;
                }

                exceededExactPalette = true;
                break;
            }
        }

        if (!exceededExactPalette)
        {
            if (exactPalette.Count == 0)
            {
                exactPalette.Add(TerminalImageColor.Black);
            }

            return exactPalette;
        }

        return BuildHistogramPalette(raster, maxColors);
    }

    private static List<TerminalImageColor> BuildHistogramPalette(TerminalRasterImage raster, int maxColors)
    {
        const int ChannelBits = 5;
        const int ChannelShift = 8 - ChannelBits;
        const int BinCount = 1 << (ChannelBits * 3);

        var countsBuffer = ArrayPool<int>.Shared.Rent(BinCount);
        var redSumsBuffer = ArrayPool<long>.Shared.Rent(BinCount);
        var greenSumsBuffer = ArrayPool<long>.Shared.Rent(BinCount);
        var blueSumsBuffer = ArrayPool<long>.Shared.Rent(BinCount);
        var usedBins = new List<int>(Math.Min(BinCount, maxColors * 4));
        var source = raster.PixelBytes.Span;
        var bytesPerPixel = TerminalRasterImage.GetBytesPerPixel(raster.PixelFormat);
        try
        {
            var counts = countsBuffer.AsSpan(0, BinCount);
            var redSums = redSumsBuffer.AsSpan(0, BinCount);
            var greenSums = greenSumsBuffer.AsSpan(0, BinCount);
            var blueSums = blueSumsBuffer.AsSpan(0, BinCount);
            counts.Clear();
            redSums.Clear();
            greenSums.Clear();
            blueSums.Clear();

            for (var y = 0; y < raster.PixelHeight; y++)
            {
                var row = source.Slice(y * raster.StrideBytes);
                for (var x = 0; x < raster.PixelWidth; x++)
                {
                    var offset = x * bytesPerPixel;
                    var red = row[offset];
                    var green = row[offset + 1];
                    var blue = row[offset + 2];
                    var bin = ((red >> ChannelShift) << (ChannelBits * 2))
                        | ((green >> ChannelShift) << ChannelBits)
                        | (blue >> ChannelShift);
                    if (counts[bin] == 0)
                    {
                        usedBins.Add(bin);
                    }

                    counts[bin]++;
                    redSums[bin] += red;
                    greenSums[bin] += green;
                    blueSums[bin] += blue;
                }
            }

            usedBins.Sort((left, right) =>
            {
                var countComparison = countsBuffer[right].CompareTo(countsBuffer[left]);
                return countComparison != 0 ? countComparison : left.CompareTo(right);
            });

            var palette = new List<TerminalImageColor>(Math.Min(maxColors, usedBins.Count));
            for (var i = 0; i < usedBins.Count && palette.Count < maxColors; i++)
            {
                var bin = usedBins[i];
                var count = counts[bin];
                if (count <= 0)
                {
                    continue;
                }

                palette.Add(new TerminalImageColor(
                    (byte)((redSums[bin] + (count / 2)) / count),
                    (byte)((greenSums[bin] + (count / 2)) / count),
                    (byte)((blueSums[bin] + (count / 2)) / count)));
            }

            if (palette.Count == 0)
            {
                palette.Add(TerminalImageColor.Black);
            }

            return palette;
        }
        finally
        {
            ArrayPool<int>.Shared.Return(countsBuffer);
            ArrayPool<long>.Shared.Return(redSumsBuffer);
            ArrayPool<long>.Shared.Return(greenSumsBuffer);
            ArrayPool<long>.Shared.Return(blueSumsBuffer);
        }
    }

    private static int FindNearestPaletteIndex(TerminalImageColor color, List<TerminalImageColor> palette)
    {
        var bestIndex = 0;
        var bestDistance = int.MaxValue;
        var paletteSpan = CollectionsMarshal.AsSpan(palette);
        for (var i = 0; i < paletteSpan.Length; i++)
        {
            var candidate = paletteSpan[i];
            var dr = color.R - candidate.R;
            var dg = color.G - candidate.G;
            var db = color.B - candidate.B;
            var distance = (dr * dr) + (dg * dg) + (db * db);
            if (distance < bestDistance)
            {
                bestDistance = distance;
                bestIndex = i;
            }
        }

        return bestIndex;
    }

    private const int NearestPaletteCacheSize = 4096;

    private static int ToRgbKey(byte red, byte green, byte blue) => (red << 16) | (green << 8) | blue;

    private static bool TryGetCachedNearestPaletteIndex(int[] keys, byte[] values, int rgbKey, out int paletteIndex)
    {
        var slot = GetNearestCacheSlot(rgbKey);
        if (keys[slot] == rgbKey)
        {
            paletteIndex = values[slot];
            return true;
        }

        paletteIndex = 0;
        return false;
    }

    private static void SetCachedNearestPaletteIndex(int[] keys, byte[] values, int rgbKey, int paletteIndex)
    {
        var slot = GetNearestCacheSlot(rgbKey);
        keys[slot] = rgbKey;
        values[slot] = (byte)paletteIndex;
    }

    private static int GetNearestCacheSlot(int rgbKey)
        => (int)(((uint)rgbKey * 2654435761u) & (NearestPaletteCacheSize - 1));

    private static int ToSixelPercent(byte channel) => (int)Math.Round(channel * 100d / 255d, MidpointRounding.AwayFromZero);

    private static void AppendRun(PooledAsciiByteBuffer builder, byte sixel, ref byte last, ref int repeat, bool useRunLengthEncoding)
    {
        if (repeat == 0)
        {
            last = sixel;
            repeat = 1;
            return;
        }

        if (last == sixel)
        {
            repeat++;
            return;
        }

        FlushRun(builder, last, repeat, useRunLengthEncoding);
        last = sixel;
        repeat = 1;
    }

    private static void FlushRun(PooledAsciiByteBuffer builder, byte sixel, int repeat, bool useRunLengthEncoding)
    {
        if (repeat <= 0)
        {
            return;
        }

        if (useRunLengthEncoding && repeat >= 4)
        {
            builder.Append((byte)'!').AppendInt(repeat).Append(sixel);
            return;
        }

        builder.AppendRepeat(sixel, repeat);
    }
}

internal sealed class PooledAsciiByteBuffer : IDisposable
{
    private byte[] _buffer;
    private int _length;

    public PooledAsciiByteBuffer(int initialCapacity = 4096)
    {
        _buffer = ArrayPool<byte>.Shared.Rent(Math.Max(1, initialCapacity));
    }

    public PooledAsciiByteBuffer Append(byte value)
    {
        EnsureCapacity(_length + 1);
        _buffer[_length++] = value;
        return this;
    }

    public PooledAsciiByteBuffer AppendAscii(ReadOnlySpan<char> text)
    {
        if (text.IsEmpty)
        {
            return this;
        }

        EnsureCapacity(_length + text.Length);
        var destination = _buffer.AsSpan(_length, text.Length);
        for (var i = 0; i < text.Length; i++)
        {
            destination[i] = (byte)text[i];
        }

        _length += text.Length;
        return this;
    }

    public PooledAsciiByteBuffer AppendInt(int value)
    {
        EnsureCapacity(_length + 11);
        if (!Utf8Formatter.TryFormat(value, _buffer.AsSpan(_length), out var written))
        {
            throw new InvalidOperationException("Unable to format an integer as ASCII.");
        }

        _length += written;
        return this;
    }

    public PooledAsciiByteBuffer AppendRepeat(byte value, int count)
    {
        if (count <= 0)
        {
            return this;
        }

        EnsureCapacity(_length + count);
        _buffer.AsSpan(_length, count).Fill(value);
        _length += count;
        return this;
    }

    public byte[] ToArray()
    {
        if (_length == 0)
        {
            return [];
        }

        var result = GC.AllocateUninitializedArray<byte>(_length);
        _buffer.AsSpan(0, _length).CopyTo(result);
        return result;
    }

    public void Dispose()
    {
        var buffer = _buffer;
        _buffer = [];
        _length = 0;
        if (buffer.Length != 0)
        {
            ArrayPool<byte>.Shared.Return(buffer);
        }
    }

    private void EnsureCapacity(int minimumCapacity)
    {
        if ((uint)minimumCapacity <= (uint)_buffer.Length)
        {
            return;
        }

        var newCapacity = _buffer.Length;
        do
        {
            newCapacity = newCapacity <= int.MaxValue / 2 ? newCapacity * 2 : int.MaxValue;
            if (newCapacity < minimumCapacity && newCapacity == int.MaxValue)
            {
                throw new OutOfMemoryException("The terminal image payload is too large.");
            }
        }
        while (newCapacity < minimumCapacity);

        var newBuffer = ArrayPool<byte>.Shared.Rent(newCapacity);
        _buffer.AsSpan(0, _length).CopyTo(newBuffer);
        ArrayPool<byte>.Shared.Return(_buffer);
        _buffer = newBuffer;
    }
}

internal static class SkiaPngImageEncoder
{
    public static byte[] EncodeRgbaToPng(TerminalRasterImage raster)
    {
        if (raster.PixelFormat != TerminalPixelFormat.Rgba32)
        {
            throw new InvalidOperationException("PNG encoding expects RGBA raster pixels.");
        }

        using var bitmap = new SKBitmap(new SKImageInfo(raster.PixelWidth, raster.PixelHeight, SKColorType.Rgba8888, SKAlphaType.Unpremul));
        var destination = bitmap.GetPixels();
        if (destination == IntPtr.Zero)
        {
            throw new InvalidOperationException("Unable to allocate a Skia bitmap for PNG encoding.");
        }

        var tightStride = checked(raster.PixelWidth * 4);
        if (raster.StrideBytes == tightStride)
        {
            var tightBytes = raster.PixelBytes[..checked(tightStride * raster.PixelHeight)];
            if (MemoryMarshal.TryGetArray(tightBytes, out var segment) && segment.Array is not null)
            {
                Marshal.Copy(segment.Array, segment.Offset, destination, segment.Count);
            }
            else
            {
                var copy = tightBytes.ToArray();
                Marshal.Copy(copy, 0, destination, copy.Length);
            }
        }
        else
        {
            var tightBytes = new byte[checked(tightStride * raster.PixelHeight)];
            var source = raster.PixelBytes.Span;
            for (var y = 0; y < raster.PixelHeight; y++)
            {
                source.Slice(y * raster.StrideBytes, tightStride).CopyTo(tightBytes.AsSpan(y * tightStride, tightStride));
            }

            Marshal.Copy(tightBytes, 0, destination, tightBytes.Length);
        }

        using var image = SKImage.FromBitmap(bitmap);
        using var data = image.Encode(SKEncodedImageFormat.Png, 100);
        if (data is null)
        {
            throw new InvalidOperationException("Unable to encode raster image as PNG.");
        }

        return data.ToArray();
    }
}

