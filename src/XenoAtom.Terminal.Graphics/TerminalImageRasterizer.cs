// Copyright (c) Alexandre Mutel. All rights reserved.
// Licensed under the BSD-Clause 2 license.
// See license.txt file in the project root for full license information.

using System.Numerics;
using System.Runtime.InteropServices;
using SkiaSharp;

namespace XenoAtom.Terminal.Graphics;

/// <summary>
/// Default SkiaSharp-backed image rasterizer used by <c>XenoAtom.Terminal.Graphics</c>.
/// </summary>
/// <remarks>
/// This type intentionally exposes only XenoAtom terminal graphics abstractions. SkiaSharp types are kept internal to the implementation.
/// </remarks>
public sealed class TerminalImageRasterizer : ITerminalImageRasterizer
{
    private static readonly Vector<byte> RgbaAlphaLaneMask = CreateRgbaAlphaLaneMask();

    /// <summary>
    /// Gets the shared default SkiaSharp-backed rasterizer instance.
    /// </summary>
    public static TerminalImageRasterizer Default { get; } = new();

    /// <summary>
    /// Gets the backend version string used in cache keys.
    /// </summary>
    public static string BackendVersion => "skia:" + typeof(SKBitmap).Assembly.GetName().Version;

    /// <inheritdoc />
    public ValueTask<TerminalImageInfo> IdentifyAsync(ReadOnlyMemory<byte> encodedImage, CancellationToken cancellationToken = default)
    {
        cancellationToken.ThrowIfCancellationRequested();
        if (encodedImage.IsEmpty)
        {
            throw new ArgumentException("Encoded image data must not be empty.", nameof(encodedImage));
        }

        using var stream = CreateReadOnlyStream(encodedImage);
        using var codec = SKCodec.Create(stream);
        if (codec is null)
        {
            throw new InvalidDataException("The image format is not supported by the default terminal image rasterizer.");
        }

        var info = codec.Info;
        return ValueTask.FromResult(new TerminalImageInfo(
            MapFormat(codec.EncodedFormat),
            info.Width,
            info.Height,
            info.AlphaType != SKAlphaType.Opaque,
            codec.FrameCount <= 0 ? null : codec.FrameCount));
    }

    /// <inheritdoc />
    public ValueTask<TerminalRasterImage> RasterizeAsync(TerminalImageFrame frame, TerminalRasterizeRequest request, CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(frame);
        cancellationToken.ThrowIfCancellationRequested();

        var targetSize = request.TargetPixelSize;
        if (targetSize.IsEmpty)
        {
            targetSize = new TerminalImageSize(frame.PixelWidth, frame.PixelHeight);
        }
        targetSize.ThrowIfEmpty(nameof(request));

        if (TryCreateDirectRawRaster(frame, targetSize, request, out var directRaster))
        {
            return ValueTask.FromResult(directRaster);
        }

        using var sourceBitmap = DecodeFrameToBitmap(frame);
        using var destinationBitmap = new SKBitmap(new SKImageInfo(targetSize.Width, targetSize.Height, SKColorType.Rgba8888, request.MatteColor.HasValue ? SKAlphaType.Opaque : SKAlphaType.Unpremul));
        using var canvas = new SKCanvas(destinationBitmap);

        if (request.MatteColor is { } matte)
        {
            canvas.Clear(new SKColor(matte.R, matte.G, matte.B, 255));
        }
        else
        {
            canvas.Clear(SKColors.Transparent);
        }

        var (sourceRect, destinationRect) = ResolveRects(sourceBitmap.Width, sourceBitmap.Height, targetSize.Width, targetSize.Height, request.ScaleMode, request.PreserveAspectRatio);
        using var paint = new SKPaint
        {
            IsAntialias = request.Quality != TerminalImageResamplingQuality.Nearest,
            IsDither = request.Quality is TerminalImageResamplingQuality.Medium or TerminalImageResamplingQuality.High,
        };

        canvas.DrawBitmap(sourceBitmap, sourceRect, destinationRect, paint);
        canvas.Flush();

        var pixelBytes = CopyBitmapBytes(destinationBitmap);
        if (request.MatteColor.HasValue)
        {
            for (var i = 3; i < pixelBytes.Length; i += 4)
            {
                pixelBytes[i] = 255;
            }
        }

        return ValueTask.FromResult(new TerminalRasterImage(destinationBitmap.Width, destinationBitmap.Height, TerminalPixelFormat.Rgba32, pixelBytes, destinationBitmap.RowBytes));
    }

    private static bool TryCreateDirectRawRaster(TerminalImageFrame frame, TerminalImageSize targetSize, TerminalRasterizeRequest request, out TerminalRasterImage raster)
    {
        raster = null!;
        if (frame.Format != TerminalImageFormat.RawRgba32 ||
            frame.PixelWidth != targetSize.Width ||
            frame.PixelHeight != targetSize.Height)
        {
            return false;
        }

        var stride = checked(frame.PixelWidth * 4);
        var requiredLength = checked(stride * frame.PixelHeight);
        if (frame.Data.Length < requiredLength)
        {
            return false;
        }

        if (request.MatteColor is null || IsOpaqueRgba32(frame.Data.Span[..requiredLength]))
        {
            raster = new TerminalRasterImage(frame.PixelWidth, frame.PixelHeight, TerminalPixelFormat.Rgba32, frame.Data[..requiredLength], stride);
            return true;
        }

        raster = new TerminalRasterImage(frame.PixelWidth, frame.PixelHeight, TerminalPixelFormat.Rgba32, FlattenRgba32ToMatte(frame.Data.Span[..requiredLength], request.MatteColor.Value), stride);
        return true;
    }

    private static bool IsOpaqueRgba32(ReadOnlySpan<byte> bytes)
    {
        var i = 0;
        if (Vector.IsHardwareAccelerated && bytes.Length >= Vector<byte>.Count)
        {
            var mask = RgbaAlphaLaneMask;
            var vectorLength = Vector<byte>.Count;
            var vectorLimit = bytes.Length - vectorLength;
            for (; i <= vectorLimit; i += vectorLength)
            {
                var vector = new Vector<byte>(bytes.Slice(i, vectorLength));
                if (!Vector.EqualsAll(Vector.BitwiseAnd(vector, mask), mask))
                {
                    return false;
                }
            }
        }

        for (i += 3; i < bytes.Length; i += 4)
        {
            if (bytes[i] != byte.MaxValue)
            {
                return false;
            }
        }

        return true;
    }

    private static Vector<byte> CreateRgbaAlphaLaneMask()
    {
        var mask = new byte[Vector<byte>.Count];
        for (var i = 3; i < mask.Length; i += 4)
        {
            mask[i] = byte.MaxValue;
        }

        return new Vector<byte>(mask);
    }

    private static byte[] FlattenRgba32ToMatte(ReadOnlySpan<byte> source, TerminalImageColor matte)
    {
        var result = GC.AllocateUninitializedArray<byte>(source.Length);
        for (var i = 0; i < source.Length; i += 4)
        {
            var alpha = source[i + 3];
            if (alpha == byte.MaxValue)
            {
                result[i] = source[i];
                result[i + 1] = source[i + 1];
                result[i + 2] = source[i + 2];
            }
            else if (alpha == 0)
            {
                result[i] = matte.R;
                result[i + 1] = matte.G;
                result[i + 2] = matte.B;
            }
            else
            {
                var inverseAlpha = byte.MaxValue - alpha;
                result[i] = BlendOverMatte(source[i], matte.R, alpha, inverseAlpha);
                result[i + 1] = BlendOverMatte(source[i + 1], matte.G, alpha, inverseAlpha);
                result[i + 2] = BlendOverMatte(source[i + 2], matte.B, alpha, inverseAlpha);
            }

            result[i + 3] = byte.MaxValue;
        }

        return result;
    }

    private static byte BlendOverMatte(byte source, byte matte, byte alpha, int inverseAlpha)
        => (byte)(((source * alpha) + (matte * inverseAlpha) + 127) / 255);

    private static SKBitmap DecodeFrameToBitmap(TerminalImageFrame frame)
    {
        if (frame.PixelWidth <= 0 || frame.PixelHeight <= 0)
        {
            throw new InvalidDataException("Image frame dimensions must be greater than zero.");
        }

        if (frame.Format is TerminalImageFormat.RawRgb24 or TerminalImageFormat.RawRgba32)
        {
            return CreateBitmapFromRawFrame(frame);
        }

        using var stream = CreateReadOnlyStream(frame.Data);
        var bitmap = SKBitmap.Decode(stream);
        if (bitmap is null)
        {
            throw new InvalidDataException("The image frame could not be decoded by the default terminal image rasterizer.");
        }

        return bitmap;
    }

    private static SKBitmap CreateBitmapFromRawFrame(TerminalImageFrame frame)
    {
        var sourceBytes = frame.Data.Span;
        var pixelCount = checked(frame.PixelWidth * frame.PixelHeight);
        var rgbaBytes = frame.Format == TerminalImageFormat.RawRgba32
            ? null
            : ExpandRgbToRgba(sourceBytes, frame.PixelWidth, frame.PixelHeight);

        var bitmap = new SKBitmap(new SKImageInfo(frame.PixelWidth, frame.PixelHeight, SKColorType.Rgba8888, SKAlphaType.Unpremul));
        var destination = bitmap.GetPixels();
        if (destination == IntPtr.Zero)
        {
            bitmap.Dispose();
            throw new InvalidOperationException("Unable to allocate a Skia bitmap for raw image pixels.");
        }

        if (rgbaBytes is null)
        {
            CopyMemoryToPointer(frame.Data[..checked(pixelCount * 4)], destination);
        }
        else
        {
            Marshal.Copy(rgbaBytes, 0, destination, rgbaBytes.Length);
        }

        return bitmap;
    }

    private static MemoryStream CreateReadOnlyStream(ReadOnlyMemory<byte> data)
    {
        if (MemoryMarshal.TryGetArray(data, out var segment) && segment.Array is not null)
        {
            return new MemoryStream(segment.Array, segment.Offset, segment.Count, writable: false, publiclyVisible: true);
        }

        return new MemoryStream(data.ToArray(), writable: false);
    }

    private static void CopyMemoryToPointer(ReadOnlyMemory<byte> source, IntPtr destination)
    {
        if (MemoryMarshal.TryGetArray(source, out var segment) && segment.Array is not null)
        {
            Marshal.Copy(segment.Array, segment.Offset, destination, segment.Count);
            return;
        }

        var bytes = source.ToArray();
        Marshal.Copy(bytes, 0, destination, bytes.Length);
    }

    private static byte[] ExpandRgbToRgba(ReadOnlySpan<byte> rgbBytes, int width, int height)
    {
        var pixelCount = checked(width * height);
        var expectedBytes = checked(pixelCount * 3);
        if (rgbBytes.Length < expectedBytes)
        {
            throw new InvalidDataException("The raw RGB frame is smaller than the declared dimensions.");
        }

        var rgbaBytes = new byte[checked(pixelCount * 4)];
        var source = 0;
        var destination = 0;
        for (var i = 0; i < pixelCount; i++)
        {
            rgbaBytes[destination++] = rgbBytes[source++];
            rgbaBytes[destination++] = rgbBytes[source++];
            rgbaBytes[destination++] = rgbBytes[source++];
            rgbaBytes[destination++] = 255;
        }

        return rgbaBytes;
    }

    private static byte[] CopyBitmapBytes(SKBitmap bitmap)
    {
        var byteLength = checked(bitmap.RowBytes * bitmap.Height);
        var bytes = new byte[byteLength];
        var source = bitmap.GetPixels();
        if (source == IntPtr.Zero)
        {
            throw new InvalidOperationException("Unable to access Skia bitmap pixels.");
        }

        Marshal.Copy(source, bytes, 0, byteLength);
        return bytes;
    }

    private static (SKRect Source, SKRect Destination) ResolveRects(int sourceWidth, int sourceHeight, int targetWidth, int targetHeight, TerminalImageScaleMode scaleMode, bool preserveAspectRatio)
    {
        var sourceRect = SKRect.Create(0, 0, sourceWidth, sourceHeight);
        if (scaleMode == TerminalImageScaleMode.Stretch || !preserveAspectRatio)
        {
            return (sourceRect, SKRect.Create(0, 0, targetWidth, targetHeight));
        }

        float destinationWidth;
        float destinationHeight;
        switch (scaleMode)
        {
            case TerminalImageScaleMode.Center:
                destinationWidth = sourceWidth;
                destinationHeight = sourceHeight;
                break;
            case TerminalImageScaleMode.Fill:
                var fillScale = Math.Max(targetWidth / (float)sourceWidth, targetHeight / (float)sourceHeight);
                destinationWidth = sourceWidth * fillScale;
                destinationHeight = sourceHeight * fillScale;
                break;
            case TerminalImageScaleMode.Fit:
            default:
                var fitScale = Math.Min(targetWidth / (float)sourceWidth, targetHeight / (float)sourceHeight);
                destinationWidth = sourceWidth * fitScale;
                destinationHeight = sourceHeight * fitScale;
                break;
        }

        var x = (targetWidth - destinationWidth) / 2f;
        var y = (targetHeight - destinationHeight) / 2f;
        return (sourceRect, SKRect.Create(x, y, destinationWidth, destinationHeight));
    }

    private static TerminalImageFormat MapFormat(SKEncodedImageFormat format) => format switch
    {
        SKEncodedImageFormat.Png => TerminalImageFormat.Png,
        SKEncodedImageFormat.Jpeg => TerminalImageFormat.Jpeg,
        SKEncodedImageFormat.Webp => TerminalImageFormat.Webp,
        SKEncodedImageFormat.Gif => TerminalImageFormat.Gif,
        _ => TerminalImageFormat.Unknown,
    };
}
