// Copyright (c) Alexandre Mutel. All rights reserved.
// Licensed under the BSD-Clause 2 license.
// See license.txt file in the project root for full license information.

using System.Collections.Concurrent;
using System.Globalization;
using System.Security.Cryptography;
using System.Text;
using System.Threading;
using XenoAtom.Ansi;
using XenoAtom.Terminal;

namespace XenoAtom.Terminal.Graphics;

/// <summary>
/// Describes an image size in pixels or terminal cells depending on context.
/// </summary>
/// <param name="Width">The width.</param>
/// <param name="Height">The height.</param>
public readonly record struct TerminalImageSize(int Width, int Height)
{
    /// <summary>
    /// Gets a zero size.
    /// </summary>
    public static TerminalImageSize Empty => default;

    /// <summary>
    /// Gets a value indicating whether either dimension is less than or equal to zero.
    /// </summary>
    public bool IsEmpty => Width <= 0 || Height <= 0;

    /// <summary>
    /// Throws when the size is empty.
    /// </summary>
    /// <param name="paramName">The parameter name to use in the exception.</param>
    /// <exception cref="ArgumentOutOfRangeException">The size is empty.</exception>
    public void ThrowIfEmpty(string paramName)
    {
        if (IsEmpty)
        {
            throw new ArgumentOutOfRangeException(paramName, "Image dimensions must be greater than zero.");
        }
    }
}

/// <summary>
/// Describes an RGBA color used by the terminal graphics pipeline.
/// </summary>
/// <param name="R">The red channel.</param>
/// <param name="G">The green channel.</param>
/// <param name="B">The blue channel.</param>
/// <param name="A">The alpha channel.</param>
public readonly record struct TerminalImageColor(byte R, byte G, byte B, byte A = 255)
{
    /// <summary>
    /// Gets an opaque black color.
    /// </summary>
    public static TerminalImageColor Black => new(0, 0, 0);

    /// <summary>
    /// Gets an opaque white color.
    /// </summary>
    public static TerminalImageColor White => new(255, 255, 255);

    internal int ToRgbKey() => (R << 16) | (G << 8) | B;
}

/// <summary>
/// Identifies the encoded or raw image format of a frame.
/// </summary>
public enum TerminalImageFormat
{
    /// <summary>
    /// The image format is unknown.
    /// </summary>
    Unknown = 0,

    /// <summary>
    /// Portable Network Graphics.
    /// </summary>
    Png,

    /// <summary>
    /// JPEG/JFIF image data.
    /// </summary>
    Jpeg,

    /// <summary>
    /// WebP image data.
    /// </summary>
    Webp,

    /// <summary>
    /// GIF image data. The v1 pipeline uses the first frame only.
    /// </summary>
    Gif,

    /// <summary>
    /// Tightly packed raw RGB pixels, three bytes per pixel.
    /// </summary>
    RawRgb24,

    /// <summary>
    /// Tightly packed raw RGBA pixels, four bytes per pixel.
    /// </summary>
    RawRgba32,
}

/// <summary>
/// Identifies the pixel format of a raster image.
/// </summary>
public enum TerminalPixelFormat
{
    /// <summary>
    /// Tightly packed RGB pixels, three bytes per pixel.
    /// </summary>
    Rgb24 = 0,

    /// <summary>
    /// Tightly packed RGBA pixels, four bytes per pixel.
    /// </summary>
    Rgba32 = 1,
}

/// <summary>
/// Controls how a source image is mapped to a target pixel rectangle.
/// </summary>
public enum TerminalImageScaleMode
{
    /// <summary>
    /// Preserve aspect ratio and fit inside the target rectangle.
    /// </summary>
    Fit = 0,

    /// <summary>
    /// Preserve aspect ratio and cover the target rectangle, cropping overflow.
    /// </summary>
    Fill = 1,

    /// <summary>
    /// Stretch to exactly the target rectangle.
    /// </summary>
    Stretch = 2,

    /// <summary>
    /// Center at natural size without scaling, clipping overflow.
    /// </summary>
    Center = 3,
}

/// <summary>
/// Controls the quality/speed tradeoff used by the default rasterizer.
/// </summary>
public enum TerminalImageResamplingQuality
{
    /// <summary>
    /// Nearest-neighbor sampling.
    /// </summary>
    Nearest = 0,

    /// <summary>
    /// Linear sampling.
    /// </summary>
    Linear = 1,

    /// <summary>
    /// Medium quality sampling.
    /// </summary>
    Medium = 2,

    /// <summary>
    /// High quality sampling.
    /// </summary>
    High = 3,
}

/// <summary>
/// Describes image metadata discovered without producing a terminal payload.
/// </summary>
/// <param name="Format">The image format.</param>
/// <param name="PixelWidth">The image width in pixels.</param>
/// <param name="PixelHeight">The image height in pixels.</param>
/// <param name="HasAlpha">Whether the image has an alpha channel.</param>
/// <param name="FrameCount">The number of frames when known.</param>
public readonly record struct TerminalImageInfo(TerminalImageFormat Format, int PixelWidth, int PixelHeight, bool HasAlpha, int? FrameCount = null);

/// <summary>
/// Describes a request for a frame from a terminal image source.
/// </summary>
/// <param name="FrameIndex">The requested frame index. The v1 pipeline uses zero for static images.</param>
/// <param name="Timestamp">The requested timestamp for real-time sources, when applicable.</param>
public readonly record struct TerminalImageFrameRequest(int FrameIndex, TimeSpan? Timestamp)
{
    /// <summary>
    /// Gets a request for the first/static frame.
    /// </summary>
    public static TerminalImageFrameRequest Default => default;
}

/// <summary>
/// Provides notification data for a newly available real-time image frame.
/// </summary>
/// <param name="version">The monotonic source frame version.</param>
/// <param name="timestamp">The source frame timestamp.</param>
public sealed class TerminalImageFrameAvailableEventArgs(long version, TimeSpan timestamp) : EventArgs
{
    /// <summary>
    /// Gets the monotonic source frame version.
    /// </summary>
    public long Version { get; } = version;

    /// <summary>
    /// Gets the source frame timestamp.
    /// </summary>
    public TimeSpan Timestamp { get; } = timestamp;
}

/// <summary>
/// Identifies an image source that can notify consumers when new real-time frames are available.
/// </summary>
/// <remarks>
/// Implementations should also derive from <see cref="TerminalImageSource"/> so existing encoding APIs can retrieve the
/// latest frame through <see cref="TerminalImageSource.GetFrameAsync"/>. Frame notifications are scheduling hints only:
/// consumers may coalesce them and request only the latest frame.
/// </remarks>
public interface ITerminalRealtimeImageSource : IAsyncDisposable
{
    /// <summary>
    /// Occurs when a newer frame is available from the source.
    /// </summary>
    event EventHandler<TerminalImageFrameAvailableEventArgs>? FrameAvailable;

    /// <summary>
    /// Gets the minimum interval the source recommends between presentation attempts.
    /// </summary>
    /// <remarks>
    /// Consumers may use this value to throttle redraws. A value of <see cref="TimeSpan.Zero"/> indicates that the source
    /// does not provide a recommended throttle interval.
    /// </remarks>
    TimeSpan MinimumFrameInterval { get; }

    /// <summary>
    /// Gets the latest frame available from this source.
    /// </summary>
    /// <param name="request">The frame request.</param>
    /// <param name="cancellationToken">A cancellation token.</param>
    /// <returns>The latest frame, or <see langword="null"/> when no frame is available.</returns>
    ValueTask<TerminalImageFrame?> GetLatestFrameAsync(TerminalImageFrameRequest request, CancellationToken cancellationToken = default);
}

/// <summary>
/// Describes a request to rasterize an image frame to pixels.
/// </summary>
/// <param name="TargetPixelSize">The output pixel size.</param>
/// <param name="ScaleMode">The image scale mode.</param>
/// <param name="PreserveAspectRatio">Whether aspect ratio should be preserved when the scale mode supports it.</param>
/// <param name="MatteColor">The matte color used to flatten alpha, or <see langword="null"/> to preserve alpha.</param>
/// <param name="Quality">The resampling quality.</param>
public readonly record struct TerminalRasterizeRequest(
    TerminalImageSize TargetPixelSize,
    TerminalImageScaleMode ScaleMode,
    bool PreserveAspectRatio,
    TerminalImageColor? MatteColor,
    TerminalImageResamplingQuality Quality)
{
    /// <summary>
    /// Initializes a new instance of the <see cref="TerminalRasterizeRequest"/> struct using fit/high-quality defaults.
    /// </summary>
    /// <param name="targetPixelSize">The output pixel size.</param>
    public TerminalRasterizeRequest(TerminalImageSize targetPixelSize)
        : this(targetPixelSize, TerminalImageScaleMode.Fit, PreserveAspectRatio: true, MatteColor: null, TerminalImageResamplingQuality.High)
    {
    }
}

/// <summary>
/// Describes a request to encode an image frame for a terminal graphics protocol.
/// </summary>
/// <param name="Protocol">The protocol to encode.</param>
/// <param name="PixelSize">The target pixel size.</param>
/// <param name="CellSize">The target terminal cell size.</param>
/// <param name="PixelMetrics">The terminal pixel metrics used to derive the target size, when available.</param>
/// <param name="ScaleMode">The scale mode.</param>
/// <param name="MatteColor">The matte color used when the target protocol cannot preserve alpha.</param>
/// <param name="PreserveAspectRatio">Whether aspect ratio should be preserved.</param>
/// <param name="Quality">The raster resampling quality.</param>
/// <param name="MaxPayloadChunkBytes">The maximum payload chunk size for protocols that support chunking.</param>
/// <param name="ImageId">An optional retained image id for protocols that support it.</param>
/// <param name="PlacementId">An optional retained placement id for protocols that support it.</param>
public readonly record struct TerminalImageEncodeRequest(
    TerminalGraphicsProtocol Protocol,
    TerminalImageSize PixelSize,
    TerminalImageSize CellSize,
    TerminalPixelMetrics? PixelMetrics,
    TerminalImageScaleMode ScaleMode,
    TerminalImageColor? MatteColor,
    bool PreserveAspectRatio,
    TerminalImageResamplingQuality Quality,
    int MaxPayloadChunkBytes,
    int? ImageId,
    int? PlacementId)
{
    /// <summary>
    /// Initializes a new instance of the <see cref="TerminalImageEncodeRequest"/> struct using high-quality defaults.
    /// </summary>
    /// <param name="protocol">The protocol to encode.</param>
    /// <param name="pixelSize">The target pixel size.</param>
    /// <param name="cellSize">The target terminal cell size.</param>
    /// <param name="pixelMetrics">The terminal pixel metrics used to derive the target size, when available.</param>
    /// <param name="scaleMode">The scale mode.</param>
    /// <param name="matteColor">The matte color used when alpha must be flattened.</param>
    /// <param name="preserveAspectRatio">Whether aspect ratio should be preserved.</param>
    public TerminalImageEncodeRequest(
        TerminalGraphicsProtocol protocol,
        TerminalImageSize pixelSize,
        TerminalImageSize cellSize,
        TerminalPixelMetrics? pixelMetrics,
        TerminalImageScaleMode scaleMode,
        TerminalImageColor? matteColor,
        bool preserveAspectRatio)
        : this(
            protocol,
            pixelSize,
            cellSize,
            pixelMetrics,
            scaleMode,
            matteColor,
            preserveAspectRatio,
            TerminalImageResamplingQuality.High,
            AnsiKittyGraphicsSequences.DefaultMaxPayloadChunkChars,
            ImageId: null,
            PlacementId: null)
    {
    }

    internal int NormalizedMaxPayloadChunkBytes => MaxPayloadChunkBytes <= 0 ? AnsiKittyGraphicsSequences.DefaultMaxPayloadChunkChars : MaxPayloadChunkBytes;
}

/// <summary>
/// Represents a source frame to be decoded, rasterized, or encoded for terminal graphics.
/// </summary>
public sealed class TerminalImageFrame : IAsyncDisposable
{
    /// <summary>
    /// Gets the frame format.
    /// </summary>
    public required TerminalImageFormat Format { get; init; }

    /// <summary>
    /// Gets the encoded or raw frame bytes.
    /// </summary>
    public required ReadOnlyMemory<byte> Data { get; init; }

    /// <summary>
    /// Gets the source frame width in pixels.
    /// </summary>
    public required int PixelWidth { get; init; }

    /// <summary>
    /// Gets the source frame height in pixels.
    /// </summary>
    public required int PixelHeight { get; init; }

    /// <summary>
    /// Gets an optional stable source identity used by cache keys.
    /// </summary>
    public string? SourceId { get; init; }

    /// <summary>
    /// Gets a monotonic source version used by caches and latest-wins encoding.
    /// </summary>
    public long Version { get; init; }

    /// <summary>
    /// Gets an optional frame timestamp for real-time sources.
    /// </summary>
    public TimeSpan Timestamp { get; init; }

    /// <inheritdoc />
    public ValueTask DisposeAsync() => ValueTask.CompletedTask;
}

/// <summary>
/// Provides frames for terminal image rendering.
/// </summary>
public abstract class TerminalImageSource
{
    /// <summary>
    /// Gets a frame from this source.
    /// </summary>
    /// <param name="request">The frame request.</param>
    /// <param name="cancellationToken">A cancellation token.</param>
    /// <returns>The requested frame, or <see langword="null"/> when no frame is available.</returns>
    public abstract ValueTask<TerminalImageFrame?> GetFrameAsync(TerminalImageFrameRequest request, CancellationToken cancellationToken = default);

    /// <summary>
    /// Creates an image source from encoded image bytes.
    /// </summary>
    /// <param name="data">The image bytes.</param>
    /// <param name="sourceId">An optional stable source identity.</param>
    /// <returns>The image source.</returns>
    /// <exception cref="ArgumentException"><paramref name="data"/> is empty.</exception>
    public static TerminalImageSource FromEncodedBytes(ReadOnlyMemory<byte> data, string? sourceId = null)
    {
        if (data.IsEmpty)
        {
            throw new ArgumentException("Image data must not be empty.", nameof(data));
        }

        var copy = data.ToArray();
        return new EncodedBytesImageSource(copy, sourceId ?? CreateMemorySourceId(copy, "encoded"));
    }

    /// <summary>
    /// Creates an image source from raw RGBA pixels.
    /// </summary>
    /// <param name="pixels">The tightly packed RGBA pixels.</param>
    /// <param name="width">The width in pixels.</param>
    /// <param name="height">The height in pixels.</param>
    /// <param name="sourceId">An optional stable source identity.</param>
    /// <returns>The image source.</returns>
    /// <exception cref="ArgumentOutOfRangeException"><paramref name="width"/> or <paramref name="height"/> is not positive.</exception>
    /// <exception cref="ArgumentException"><paramref name="pixels"/> is too small.</exception>
    public static TerminalImageSource FromRgba32(ReadOnlyMemory<byte> pixels, int width, int height, string? sourceId = null)
        => FromRawPixels(pixels, width, height, TerminalImageFormat.RawRgba32, sourceId);

    /// <summary>
    /// Creates an image source from raw RGB pixels.
    /// </summary>
    /// <param name="pixels">The tightly packed RGB pixels.</param>
    /// <param name="width">The width in pixels.</param>
    /// <param name="height">The height in pixels.</param>
    /// <param name="sourceId">An optional stable source identity.</param>
    /// <returns>The image source.</returns>
    /// <exception cref="ArgumentOutOfRangeException"><paramref name="width"/> or <paramref name="height"/> is not positive.</exception>
    /// <exception cref="ArgumentException"><paramref name="pixels"/> is too small.</exception>
    public static TerminalImageSource FromRgb24(ReadOnlyMemory<byte> pixels, int width, int height, string? sourceId = null)
        => FromRawPixels(pixels, width, height, TerminalImageFormat.RawRgb24, sourceId);

    /// <summary>
    /// Creates an image source from a file path.
    /// </summary>
    /// <param name="path">The image file path.</param>
    /// <returns>The image source.</returns>
    /// <exception cref="ArgumentException"><paramref name="path"/> is null or empty.</exception>
    public static TerminalImageSource FromFile(string path)
    {
        ArgumentException.ThrowIfNullOrEmpty(path);
        return new FileImageSource(path);
    }

    private static TerminalImageSource FromRawPixels(ReadOnlyMemory<byte> pixels, int width, int height, TerminalImageFormat format, string? sourceId)
    {
        ArgumentOutOfRangeException.ThrowIfLessThan(width, 1);
        ArgumentOutOfRangeException.ThrowIfLessThan(height, 1);

        var bytesPerPixel = format == TerminalImageFormat.RawRgba32 ? 4 : 3;
        var minimumBytes = checked(width * height * bytesPerPixel);
        if (pixels.Length < minimumBytes)
        {
            throw new ArgumentException("The pixel buffer is smaller than the required dimensions.", nameof(pixels));
        }

        var copy = pixels[..minimumBytes].ToArray();
        return new RawImageSource(copy, width, height, format, sourceId ?? CreateMemorySourceId(copy, $"raw:{format}:{width}x{height}"));
    }

    private static string CreateMemorySourceId(ReadOnlySpan<byte> data, string prefix)
        => prefix + ":" + Convert.ToHexString(SHA256.HashData(data));

    private sealed class EncodedBytesImageSource(byte[] data, string? sourceId) : TerminalImageSource
    {
        private TerminalImageInfo? _info;

        public override async ValueTask<TerminalImageFrame?> GetFrameAsync(TerminalImageFrameRequest request, CancellationToken cancellationToken = default)
        {
            cancellationToken.ThrowIfCancellationRequested();
            var info = _info ??= await TerminalImageRasterizer.Default.IdentifyAsync(data, cancellationToken).ConfigureAwait(false);
            return new TerminalImageFrame
            {
                Format = info.Format,
                Data = data,
                PixelWidth = info.PixelWidth,
                PixelHeight = info.PixelHeight,
                SourceId = sourceId,
                Version = 0,
                Timestamp = request.Timestamp ?? TimeSpan.Zero,
            };
        }
    }

    private sealed class RawImageSource(byte[] data, int width, int height, TerminalImageFormat format, string? sourceId) : TerminalImageSource
    {
        public override ValueTask<TerminalImageFrame?> GetFrameAsync(TerminalImageFrameRequest request, CancellationToken cancellationToken = default)
        {
            cancellationToken.ThrowIfCancellationRequested();
            return ValueTask.FromResult<TerminalImageFrame?>(new TerminalImageFrame
            {
                Format = format,
                Data = data,
                PixelWidth = width,
                PixelHeight = height,
                SourceId = sourceId,
                Version = 0,
                Timestamp = request.Timestamp ?? TimeSpan.Zero,
            });
        }
    }

    private sealed class FileImageSource : TerminalImageSource
    {
        private readonly string _path;
        private TerminalImageInfo? _info;
        private byte[]? _data;
        private long _version = long.MinValue;

        public FileImageSource(string path)
        {
            _path = Path.GetFullPath(path);
        }

        public override async ValueTask<TerminalImageFrame?> GetFrameAsync(TerminalImageFrameRequest request, CancellationToken cancellationToken = default)
        {
            cancellationToken.ThrowIfCancellationRequested();
            var version = File.GetLastWriteTimeUtc(_path).Ticks;
            var data = _data;
            var info = _info;
            if (data is null || info is null || _version != version)
            {
                data = await File.ReadAllBytesAsync(_path, cancellationToken).ConfigureAwait(false);
                info = await TerminalImageRasterizer.Default.IdentifyAsync(data, cancellationToken).ConfigureAwait(false);
                _data = data;
                _info = info;
                _version = version;
            }

            return new TerminalImageFrame
            {
                Format = info.Value.Format,
                Data = data,
                PixelWidth = info.Value.PixelWidth,
                PixelHeight = info.Value.PixelHeight,
                SourceId = _path,
                Version = version,
                Timestamp = request.Timestamp ?? TimeSpan.Zero,
            };
        }
    }
}

/// <summary>
/// Represents a raster image with explicit pixel format and stride.
/// </summary>
public sealed class TerminalRasterImage : IAsyncDisposable
{
    /// <summary>
    /// Initializes a new instance of the <see cref="TerminalRasterImage"/> class.
    /// </summary>
    /// <param name="pixelWidth">The width in pixels.</param>
    /// <param name="pixelHeight">The height in pixels.</param>
    /// <param name="pixelFormat">The pixel format.</param>
    /// <param name="pixelBytes">The pixel bytes.</param>
    /// <param name="strideBytes">The stride in bytes.</param>
    /// <exception cref="ArgumentOutOfRangeException">A dimension or stride is invalid.</exception>
    /// <exception cref="ArgumentException"><paramref name="pixelBytes"/> is too small for the provided dimensions.</exception>
    public TerminalRasterImage(int pixelWidth, int pixelHeight, TerminalPixelFormat pixelFormat, ReadOnlyMemory<byte> pixelBytes, int strideBytes)
    {
        ArgumentOutOfRangeException.ThrowIfLessThan(pixelWidth, 1);
        ArgumentOutOfRangeException.ThrowIfLessThan(pixelHeight, 1);

        var bytesPerPixel = GetBytesPerPixel(pixelFormat);
        var minimumStride = checked(pixelWidth * bytesPerPixel);
        if (strideBytes < minimumStride)
        {
            throw new ArgumentOutOfRangeException(nameof(strideBytes), "The stride is smaller than the width multiplied by bytes-per-pixel.");
        }

        var minimumLength = checked(strideBytes * pixelHeight);
        if (pixelBytes.Length < minimumLength)
        {
            throw new ArgumentException("The pixel buffer is smaller than the required dimensions and stride.", nameof(pixelBytes));
        }

        PixelWidth = pixelWidth;
        PixelHeight = pixelHeight;
        PixelFormat = pixelFormat;
        PixelBytes = pixelBytes;
        StrideBytes = strideBytes;
    }

    /// <summary>
    /// Gets the width in pixels.
    /// </summary>
    public int PixelWidth { get; }

    /// <summary>
    /// Gets the height in pixels.
    /// </summary>
    public int PixelHeight { get; }

    /// <summary>
    /// Gets the pixel format.
    /// </summary>
    public TerminalPixelFormat PixelFormat { get; }

    /// <summary>
    /// Gets the pixel bytes.
    /// </summary>
    public ReadOnlyMemory<byte> PixelBytes { get; }

    /// <summary>
    /// Gets the stride in bytes.
    /// </summary>
    public int StrideBytes { get; }

    /// <summary>
    /// Gets the color at the specified pixel location.
    /// </summary>
    /// <param name="x">The x coordinate.</param>
    /// <param name="y">The y coordinate.</param>
    /// <returns>The pixel color.</returns>
    /// <exception cref="ArgumentOutOfRangeException">The coordinate is outside the image bounds.</exception>
    public TerminalImageColor GetPixel(int x, int y)
    {
        if ((uint)x >= (uint)PixelWidth)
        {
            throw new ArgumentOutOfRangeException(nameof(x));
        }

        if ((uint)y >= (uint)PixelHeight)
        {
            throw new ArgumentOutOfRangeException(nameof(y));
        }

        var offset = (y * StrideBytes) + (x * GetBytesPerPixel(PixelFormat));
        var span = PixelBytes.Span;
        return PixelFormat == TerminalPixelFormat.Rgba32
            ? new TerminalImageColor(span[offset], span[offset + 1], span[offset + 2], span[offset + 3])
            : new TerminalImageColor(span[offset], span[offset + 1], span[offset + 2]);
    }

    /// <inheritdoc />
    public ValueTask DisposeAsync() => ValueTask.CompletedTask;

    internal static int GetBytesPerPixel(TerminalPixelFormat pixelFormat) => pixelFormat switch
    {
        TerminalPixelFormat.Rgb24 => 3,
        TerminalPixelFormat.Rgba32 => 4,
        _ => throw new ArgumentOutOfRangeException(nameof(pixelFormat)),
    };
}

/// <summary>
/// Decodes image bytes and rasterizes frames to neutral terminal graphics pixel buffers.
/// </summary>
public interface ITerminalImageRasterizer
{
    /// <summary>
    /// Identifies encoded image metadata.
    /// </summary>
    /// <param name="encodedImage">The encoded image bytes.</param>
    /// <param name="cancellationToken">A cancellation token.</param>
    /// <returns>The image metadata.</returns>
    ValueTask<TerminalImageInfo> IdentifyAsync(ReadOnlyMemory<byte> encodedImage, CancellationToken cancellationToken = default);

    /// <summary>
    /// Rasterizes the specified frame.
    /// </summary>
    /// <param name="frame">The source frame.</param>
    /// <param name="request">The rasterization request.</param>
    /// <param name="cancellationToken">A cancellation token.</param>
    /// <returns>The raster image.</returns>
    ValueTask<TerminalRasterImage> RasterizeAsync(TerminalImageFrame frame, TerminalRasterizeRequest request, CancellationToken cancellationToken = default);
}

/// <summary>
/// Encodes frames for a terminal graphics protocol.
/// </summary>
public interface ITerminalImageEncoder
{
    /// <summary>
    /// Gets the protocol handled by this encoder.
    /// </summary>
    TerminalGraphicsProtocol Protocol { get; }

    /// <summary>
    /// Encodes a frame for terminal output.
    /// </summary>
    /// <param name="frame">The source frame.</param>
    /// <param name="request">The encode request.</param>
    /// <param name="cancellationToken">A cancellation token.</param>
    /// <returns>The protocol-ready encoded image.</returns>
    ValueTask<TerminalEncodedImage> EncodeAsync(TerminalImageFrame frame, TerminalImageEncodeRequest request, CancellationToken cancellationToken = default);
}

/// <summary>
/// Represents terminal-protocol payload data and metadata produced by an encoder.
/// </summary>
public sealed class TerminalEncodedImage
{
    private string? _payloadText;

    /// <summary>
    /// Gets the protocol for this payload.
    /// </summary>
    public required TerminalGraphicsProtocol Protocol { get; init; }

    /// <summary>
    /// Gets the encoded image width in pixels.
    /// </summary>
    public required int PixelWidth { get; init; }

    /// <summary>
    /// Gets the encoded image height in pixels.
    /// </summary>
    public required int PixelHeight { get; init; }

    /// <summary>
    /// Gets the target cell size.
    /// </summary>
    public TerminalImageSize CellSize { get; init; }

    /// <summary>
    /// Gets the first protocol parameter string used when writing this image.
    /// </summary>
    public string Parameters { get; init; } = string.Empty;

    /// <summary>
    /// Gets the continuation parameter string used by protocols that support chunking.
    /// </summary>
    public string ContinuationParameters { get; init; } = string.Empty;

    /// <summary>
    /// Gets the protocol payload bytes as ASCII/UTF-8 data.
    /// </summary>
    public required ReadOnlyMemory<byte> PayloadUtf8 { get; init; }

    /// <summary>
    /// Gets payload chunks as ASCII/UTF-8 data.
    /// </summary>
    public IReadOnlyList<ReadOnlyMemory<byte>> Chunks { get; init; } = Array.Empty<ReadOnlyMemory<byte>>();

    /// <summary>
    /// Gets a cache key that uniquely describes the source, request, encoder, and backend version.
    /// </summary>
    public string CacheKey { get; init; } = string.Empty;

    /// <summary>
    /// Gets the payload length in bytes.
    /// </summary>
    public int PayloadByteLength => PayloadUtf8.Length;

    internal string GetPayloadText() => _payloadText ??= Encoding.ASCII.GetString(PayloadUtf8.Span);
}

/// <summary>
/// Stores encoded images by deterministic cache key.
/// </summary>
public sealed class TerminalImageMemoryCache
{
    private readonly ConcurrentDictionary<string, TerminalEncodedImage> _encodedImages = new(StringComparer.Ordinal);
    private long _hitCount;
    private long _missCount;
    private long _storeCount;

    /// <summary>
    /// Gets the number of successful encoded-image cache lookups.
    /// </summary>
    public long HitCount => Volatile.Read(ref _hitCount);

    /// <summary>
    /// Gets the number of encoded-image cache lookups that did not find an entry.
    /// </summary>
    public long MissCount => Volatile.Read(ref _missCount);

    /// <summary>
    /// Gets the number of encoded images stored in the cache.
    /// </summary>
    public long StoreCount => Volatile.Read(ref _storeCount);

    /// <summary>
    /// Attempts to get an encoded image from the cache.
    /// </summary>
    /// <param name="cacheKey">The cache key.</param>
    /// <param name="image">The cached image when found.</param>
    /// <returns><see langword="true"/> if a cached image was found.</returns>
    /// <exception cref="ArgumentException"><paramref name="cacheKey"/> is null or empty.</exception>
    public bool TryGetEncoded(string cacheKey, out TerminalEncodedImage image)
    {
        ArgumentException.ThrowIfNullOrEmpty(cacheKey);
        if (_encodedImages.TryGetValue(cacheKey, out image!))
        {
            Interlocked.Increment(ref _hitCount);
            return true;
        }

        Interlocked.Increment(ref _missCount);
        return false;
    }

    /// <summary>
    /// Stores an encoded image in the cache.
    /// </summary>
    /// <param name="image">The encoded image.</param>
    /// <exception cref="ArgumentNullException"><paramref name="image"/> is <see langword="null"/>.</exception>
    /// <exception cref="ArgumentException">The image does not have a cache key.</exception>
    public void StoreEncoded(TerminalEncodedImage image)
    {
        ArgumentNullException.ThrowIfNull(image);
        ArgumentException.ThrowIfNullOrEmpty(image.CacheKey);
        _encodedImages[image.CacheKey] = image;
        Interlocked.Increment(ref _storeCount);
    }

    /// <summary>
    /// Clears all cached encoded images.
    /// </summary>
    public void Clear()
    {
        _encodedImages.Clear();
        Interlocked.Exchange(ref _hitCount, 0);
        Interlocked.Exchange(ref _missCount, 0);
        Interlocked.Exchange(ref _storeCount, 0);
    }
}

/// <summary>
/// Provides cache key helpers for terminal graphics encoding.
/// </summary>
public static class TerminalImageCacheKeys
{
    /// <summary>
    /// Creates a deterministic cache key for an encoded image request.
    /// </summary>
    /// <param name="frame">The source frame.</param>
    /// <param name="request">The encode request.</param>
    /// <param name="encoderName">The encoder name/version.</param>
    /// <param name="extra">Optional encoder-specific key data.</param>
    /// <returns>The cache key.</returns>
    /// <exception cref="ArgumentNullException"><paramref name="frame"/> is <see langword="null"/>.</exception>
    /// <exception cref="ArgumentException"><paramref name="encoderName"/> is null or empty.</exception>
    public static string Create(TerminalImageFrame frame, TerminalImageEncodeRequest request, string encoderName, string? extra = null)
    {
        ArgumentNullException.ThrowIfNull(frame);
        ArgumentException.ThrowIfNullOrEmpty(encoderName);

        var source = frame.SourceId ?? Convert.ToHexString(SHA256.HashData(frame.Data.Span));
        var builder = new StringBuilder(256);
        builder.Append(source);
        builder.Append("|v=").Append(frame.Version.ToString(CultureInfo.InvariantCulture));
        builder.Append("|fmt=").Append(frame.Format);
        builder.Append("|protocol=").Append(request.Protocol);
        builder.Append("|px=").Append(request.PixelSize.Width).Append('x').Append(request.PixelSize.Height);
        builder.Append("|cells=").Append(request.CellSize.Width).Append('x').Append(request.CellSize.Height);
        if (request.PixelMetrics is { } metrics)
        {
            builder.Append("|metrics=").Append(metrics.CellPixelWidth).Append('x').Append(metrics.CellPixelHeight).Append('@').Append(metrics.Columns).Append('x').Append(metrics.Rows);
        }

        builder.Append("|scale=").Append(request.ScaleMode);
        builder.Append("|aspect=").Append(request.PreserveAspectRatio ? '1' : '0');
        if (request.MatteColor is { } matte)
        {
            builder.Append("|matte=").Append(matte.R).Append(',').Append(matte.G).Append(',').Append(matte.B).Append(',').Append(matte.A);
        }

        builder.Append("|quality=").Append(request.Quality);
        builder.Append("|chunk=").Append(request.NormalizedMaxPayloadChunkBytes.ToString(CultureInfo.InvariantCulture));
        if (request.ImageId.HasValue)
        {
            builder.Append("|image=").Append(request.ImageId.Value.ToString(CultureInfo.InvariantCulture));
        }

        if (request.PlacementId.HasValue)
        {
            builder.Append("|placement=").Append(request.PlacementId.Value.ToString(CultureInfo.InvariantCulture));
        }

        builder.Append("|encoder=").Append(encoderName);
        if (!string.IsNullOrEmpty(extra))
        {
            builder.Append('|').Append(extra);
        }

        return builder.ToString();
    }
}

internal static class TerminalPayloadChunker
{
    public static IReadOnlyList<ReadOnlyMemory<byte>> Chunk(ReadOnlyMemory<byte> payload, int maxChunkBytes)
    {
        if (maxChunkBytes <= 0 || payload.Length <= maxChunkBytes)
        {
            return payload.IsEmpty ? Array.Empty<ReadOnlyMemory<byte>>() : [payload];
        }

        var chunks = new List<ReadOnlyMemory<byte>>((payload.Length + maxChunkBytes - 1) / maxChunkBytes);
        for (var offset = 0; offset < payload.Length; offset += maxChunkBytes)
        {
            chunks.Add(payload.Slice(offset, Math.Min(maxChunkBytes, payload.Length - offset)));
        }

        return chunks;
    }
}
