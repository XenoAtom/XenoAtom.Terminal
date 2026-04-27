// Copyright (c) Alexandre Mutel. All rights reserved.
// Licensed under the BSD-Clause 2 license.
// See license.txt file in the project root for full license information.

using System.Collections.Concurrent;
using System.Text;
using SkiaSharp;
using XenoAtom.Terminal.Backends;
using XenoAtom.Terminal.Graphics;

namespace XenoAtom.Terminal.Tests;

[TestClass]
public class TerminalGraphicsPackageTests
{
    [TestCleanup]
    public void Cleanup()
    {
        Terminal.ResetForTests();
    }

    [TestMethod]
    public async Task IdentifyAsync_DetectsCommonEncodedFormats()
    {
        var rasterizer = TerminalImageRasterizer.Default;

        await AssertFormatAndRasterizeAsync(CreateEncodedImage(SKEncodedImageFormat.Png), TerminalImageFormat.Png, 2, 2);
        await AssertFormatAndRasterizeAsync(CreateEncodedImage(SKEncodedImageFormat.Jpeg), TerminalImageFormat.Jpeg, 2, 2);
        await AssertFormatAndRasterizeAsync(CreateEncodedImage(SKEncodedImageFormat.Webp), TerminalImageFormat.Webp, 2, 2);
        await AssertFormatAndRasterizeAsync(Convert.FromBase64String("R0lGODdhAQABAIAAAP///wAAACwAAAAAAQABAAACAkQBADs="), TerminalImageFormat.Gif, 1, 1);

        async Task AssertFormatAndRasterizeAsync(byte[] bytes, TerminalImageFormat format, int width, int height)
        {
            var info = await rasterizer.IdentifyAsync(bytes);
            Assert.AreEqual(format, info.Format);
            Assert.AreEqual(width, info.PixelWidth);
            Assert.AreEqual(height, info.PixelHeight);

            await using var sourceFrame = new TerminalImageFrame
            {
                Format = info.Format,
                Data = bytes,
                PixelWidth = info.PixelWidth,
                PixelHeight = info.PixelHeight,
            };
            await using var raster = await rasterizer.RasterizeAsync(sourceFrame, new TerminalRasterizeRequest(new TerminalImageSize(width, height)));
            Assert.AreEqual(width, raster.PixelWidth);
            Assert.AreEqual(height, raster.PixelHeight);
        }
    }

    [TestMethod]
    public async Task RasterizeAsync_FitCentersImageAndFlattensAlphaWithMatte()
    {
        var pixels = new byte[]
        {
            255, 0, 0, 255,
            0, 0, 255, 255,
        };
        await using var frame = new TerminalImageFrame
        {
            Format = TerminalImageFormat.RawRgba32,
            Data = pixels,
            PixelWidth = 2,
            PixelHeight = 1,
        };

        await using var raster = await TerminalImageRasterizer.Default.RasterizeAsync(
            frame,
            new TerminalRasterizeRequest(new TerminalImageSize(4, 4), TerminalImageScaleMode.Fit, PreserveAspectRatio: true, TerminalImageColor.White, TerminalImageResamplingQuality.Nearest));

        Assert.AreEqual(4, raster.PixelWidth);
        Assert.AreEqual(4, raster.PixelHeight);
        Assert.AreEqual(new TerminalImageColor(255, 255, 255), raster.GetPixel(0, 0));
        Assert.AreEqual(255, raster.GetPixel(0, 1).A);
    }

    [TestMethod]
    public async Task RasterizeAsync_CenterCropsSourceWhenTargetIsSmaller()
    {
        var pixels = new byte[4 * 2 * 4];
        for (var y = 0; y < 2; y++)
        {
            WritePixel(pixels, 4, 0, y, new TerminalImageColor(255, 0, 0));
            WritePixel(pixels, 4, 1, y, new TerminalImageColor(0, 255, 0));
            WritePixel(pixels, 4, 2, y, new TerminalImageColor(0, 0, 255));
            WritePixel(pixels, 4, 3, y, new TerminalImageColor(255, 255, 255));
        }

        await using var frame = new TerminalImageFrame
        {
            Format = TerminalImageFormat.RawRgba32,
            Data = pixels,
            PixelWidth = 4,
            PixelHeight = 2,
        };

        await using var raster = await TerminalImageRasterizer.Default.RasterizeAsync(
            frame,
            new TerminalRasterizeRequest(new TerminalImageSize(2, 2), TerminalImageScaleMode.Center, PreserveAspectRatio: true, MatteColor: null, TerminalImageResamplingQuality.Nearest));

        Assert.AreNotEqual(new TerminalImageColor(255, 0, 0), raster.GetPixel(0, 0));
        Assert.AreNotEqual(new TerminalImageColor(255, 255, 255), raster.GetPixel(1, 0));
    }

    [TestMethod]
    public async Task RasterizeAsync_RawRgbaSameSize_FlattensAlphaWithoutResampling()
    {
        var pixels = new byte[]
        {
            255, 0, 0, 128,
        };
        await using var frame = new TerminalImageFrame
        {
            Format = TerminalImageFormat.RawRgba32,
            Data = pixels,
            PixelWidth = 1,
            PixelHeight = 1,
        };

        await using var raster = await TerminalImageRasterizer.Default.RasterizeAsync(
            frame,
            new TerminalRasterizeRequest(new TerminalImageSize(1, 1), TerminalImageScaleMode.Stretch, PreserveAspectRatio: false, TerminalImageColor.Black, TerminalImageResamplingQuality.Nearest));

        Assert.AreEqual(new TerminalImageColor(128, 0, 0, 255), raster.GetPixel(0, 0));
    }

    [TestMethod]
    public async Task RasterizeAsync_RawRgbaSameSize_FlattensNonOpaqueTailAfterOpaquePixels()
    {
        var pixels = new byte[12 * 4];
        for (var x = 0; x < 12; x++)
        {
            WritePixel(pixels, 12, x, 0, new TerminalImageColor(0, 255, 0));
        }

        WritePixel(pixels, 12, 11, 0, new TerminalImageColor(100, 0, 0, 128));
        await using var frame = new TerminalImageFrame
        {
            Format = TerminalImageFormat.RawRgba32,
            Data = pixels,
            PixelWidth = 12,
            PixelHeight = 1,
        };

        await using var raster = await TerminalImageRasterizer.Default.RasterizeAsync(
            frame,
            new TerminalRasterizeRequest(new TerminalImageSize(12, 1), TerminalImageScaleMode.Stretch, PreserveAspectRatio: false, TerminalImageColor.Black, TerminalImageResamplingQuality.Nearest));

        Assert.AreEqual(new TerminalImageColor(50, 0, 0, 255), raster.GetPixel(11, 0));
    }

    [TestMethod]
    public async Task KittyEncoder_ProducesChunkedRawPayload()
    {
        await using var frame = CreateRawFrame(4, 4, new TerminalImageColor(12, 34, 56));
        var encoder = new KittyTerminalImageEncoder();
        var encoded = await encoder.EncodeAsync(
            frame,
            new TerminalImageEncodeRequest(
                TerminalGraphicsProtocol.Kitty,
                new TerminalImageSize(4, 4),
                new TerminalImageSize(2, 2),
                null,
                TerminalImageScaleMode.Stretch,
                null,
                false,
                TerminalImageResamplingQuality.Nearest,
                8,
                42,
                7));

        Assert.AreEqual(TerminalGraphicsProtocol.Kitty, encoded.Protocol);
        StringAssert.Contains(encoded.Parameters, "a=T");
        StringAssert.Contains(encoded.Parameters, "f=32");
        StringAssert.Contains(encoded.Parameters, "i=42");
        StringAssert.Contains(encoded.Parameters, "p=7");
        Assert.IsGreaterThan(1, encoded.Chunks.Count);
        Assert.IsGreaterThan(encoded.Chunks[0].Length, encoded.PayloadByteLength);
    }

    [TestMethod]
    public async Task ITerm2Encoder_ProducesInlineFilePayload()
    {
        var png = CreateEncodedImage(SKEncodedImageFormat.Png);
        await using var frame = new TerminalImageFrame
        {
            Format = TerminalImageFormat.Png,
            Data = png,
            PixelWidth = 2,
            PixelHeight = 2,
            SourceId = "png",
        };

        var encoder = new ITerm2TerminalImageEncoder();
        var encoded = await encoder.EncodeAsync(
            frame,
            new TerminalImageEncodeRequest(
                TerminalGraphicsProtocol.ITerm2,
                new TerminalImageSize(2, 2),
                new TerminalImageSize(1, 1),
                null,
                TerminalImageScaleMode.Fit,
                null,
                true));

        Assert.AreEqual(TerminalGraphicsProtocol.ITerm2, encoded.Protocol);
        StringAssert.Contains(encoded.Parameters, "inline=1");
        StringAssert.Contains(encoded.Parameters, "width=2px");
        StringAssert.Contains(encoded.Parameters, "height=2px");
        CollectionAssert.AreEqual(Encoding.ASCII.GetBytes(Convert.ToBase64String(png)), encoded.PayloadUtf8.ToArray());
    }

    [TestMethod]
    public async Task SixelEncoder_ProducesDeterministicPaletteAndPayload()
    {
        var pixels = new byte[2 * 6 * 4];
        for (var y = 0; y < 6; y++)
        {
            WritePixel(pixels, 2, 0, y, new TerminalImageColor(255, 0, 0));
            WritePixel(pixels, 2, 1, y, new TerminalImageColor(0, 255, 0));
        }

        await using var frame = new TerminalImageFrame
        {
            Format = TerminalImageFormat.RawRgba32,
            Data = pixels,
            PixelWidth = 2,
            PixelHeight = 6,
            SourceId = "sixel",
        };

        var encoder = new SixelTerminalImageEncoder(options: new TerminalSixelEncoderOptions { MaxColors = 16, UseRunLengthEncoding = false });
        var encoded = await encoder.EncodeAsync(
            frame,
            new TerminalImageEncodeRequest(
                TerminalGraphicsProtocol.Sixel,
                new TerminalImageSize(2, 6),
                new TerminalImageSize(2, 1),
                null,
                TerminalImageScaleMode.Stretch,
                TerminalImageColor.Black,
                false,
                TerminalImageResamplingQuality.Nearest,
                1024,
                null,
                null));

        Assert.AreEqual("0;1", encoded.Parameters);
        var payload = Encoding.ASCII.GetString(encoded.PayloadUtf8.Span);
        StringAssert.StartsWith(payload, "\"1;1;2;6");
        StringAssert.Contains(payload, "#0;2;100;0;0");
        StringAssert.Contains(payload, "#1;2;0;100;0");
        StringAssert.Contains(payload, "#0~?");
        StringAssert.Contains(payload, "#1?~");
    }

    [TestMethod]
    public async Task SixelEncoder_QuantizesPaletteFromWholeImage()
    {
        var pixels = new byte[4 * 6 * 4];
        for (var y = 0; y < 6; y++)
        {
            WritePixel(pixels, 4, 0, y, new TerminalImageColor(255, 0, 0));
            WritePixel(pixels, 4, 1, y, new TerminalImageColor(0, 255, 0));
            WritePixel(pixels, 4, 2, y, new TerminalImageColor(0, 0, 255));
            WritePixel(pixels, 4, 3, y, new TerminalImageColor(0, 0, 255));
        }

        await using var frame = new TerminalImageFrame
        {
            Format = TerminalImageFormat.RawRgba32,
            Data = pixels,
            PixelWidth = 4,
            PixelHeight = 6,
            SourceId = "sixel-histogram-palette",
        };

        var encoder = new SixelTerminalImageEncoder(options: new TerminalSixelEncoderOptions { MaxColors = 2, UseRunLengthEncoding = false, EnableDithering = false });
        var encoded = await encoder.EncodeAsync(
            frame,
            new TerminalImageEncodeRequest(
                TerminalGraphicsProtocol.Sixel,
                new TerminalImageSize(4, 6),
                new TerminalImageSize(4, 1),
                null,
                TerminalImageScaleMode.Stretch,
                TerminalImageColor.Black,
                false,
                TerminalImageResamplingQuality.Nearest,
                1024,
                null,
                null));

        var payload = Encoding.ASCII.GetString(encoded.PayloadUtf8.Span);
        StringAssert.Contains(payload, "#0;2;0;0;100");
    }

    [TestMethod]
    public async Task SixelEncoder_FixedRgb332Palette_UsesDirectColorIndexing()
    {
        var pixels = new byte[2 * 6 * 4];
        for (var y = 0; y < 6; y++)
        {
            WritePixel(pixels, 2, 0, y, new TerminalImageColor(255, 0, 0));
            WritePixel(pixels, 2, 1, y, new TerminalImageColor(0, 255, 0));
        }

        await using var frame = new TerminalImageFrame
        {
            Format = TerminalImageFormat.RawRgba32,
            Data = pixels,
            PixelWidth = 2,
            PixelHeight = 6,
            SourceId = "sixel-rgb332",
        };

        var encoder = new SixelTerminalImageEncoder(options: new TerminalSixelEncoderOptions
        {
            PaletteMode = TerminalSixelPaletteMode.FixedRgb332,
            EnableDithering = false,
            UseRunLengthEncoding = false,
        });
        var encoded = await encoder.EncodeAsync(
            frame,
            new TerminalImageEncodeRequest(
                TerminalGraphicsProtocol.Sixel,
                new TerminalImageSize(2, 6),
                new TerminalImageSize(2, 1),
                null,
                TerminalImageScaleMode.Stretch,
                TerminalImageColor.Black,
                false,
                TerminalImageResamplingQuality.Nearest,
                1024,
                null,
                null));

        var payload = Encoding.ASCII.GetString(encoded.PayloadUtf8.Span);
        StringAssert.Contains(payload, "#224;2;100;0;0");
        StringAssert.Contains(payload, "#28;2;0;100;0");
        StringAssert.Contains(payload, "#224~?");
        StringAssert.Contains(payload, "#28?~");
        StringAssert.Contains(encoded.CacheKey, "palette=rgb332");
    }

    [TestMethod]
    public async Task SixelEncoder_DitheringChangesQuantizedPayloadAndCacheKey()
    {
        var pixels = new byte[4 * 6 * 4];
        for (var y = 0; y < 6; y++)
        {
            WritePixel(pixels, 4, 0, y, TerminalImageColor.Black);
            WritePixel(pixels, 4, 1, y, TerminalImageColor.White);
            WritePixel(pixels, 4, 2, y, new TerminalImageColor(128, 128, 128));
            WritePixel(pixels, 4, 3, y, new TerminalImageColor(128, 128, 128));
        }

        await using var frame = new TerminalImageFrame
        {
            Format = TerminalImageFormat.RawRgba32,
            Data = pixels,
            PixelWidth = 4,
            PixelHeight = 6,
            SourceId = "sixel-dither",
        };

        var request = new TerminalImageEncodeRequest(
            TerminalGraphicsProtocol.Sixel,
            new TerminalImageSize(4, 6),
            new TerminalImageSize(4, 1),
            null,
            TerminalImageScaleMode.Stretch,
            TerminalImageColor.Black,
            false,
            TerminalImageResamplingQuality.Nearest,
            1024,
            null,
            null);

        var nearest = new SixelTerminalImageEncoder(options: new TerminalSixelEncoderOptions { MaxColors = 2, UseRunLengthEncoding = false, EnableDithering = false });
        var dithered = new SixelTerminalImageEncoder(options: new TerminalSixelEncoderOptions { MaxColors = 2, UseRunLengthEncoding = false, EnableDithering = true });

        var nearestEncoded = await nearest.EncodeAsync(frame, request);
        var ditheredEncoded = await dithered.EncodeAsync(frame, request);

        var nearestPayload = Encoding.ASCII.GetString(nearestEncoded.PayloadUtf8.Span);
        var ditheredPayload = Encoding.ASCII.GetString(ditheredEncoded.PayloadUtf8.Span);
        Assert.AreNotEqual(nearestPayload, ditheredPayload);
        StringAssert.Contains(nearestEncoded.CacheKey, "dither=False");
        StringAssert.Contains(ditheredEncoded.CacheKey, "dither=True");
    }

    [TestMethod]
    public async Task ImageMemoryCache_RoundTripsEncodedImageByKey()
    {
        await using var frame = CreateRawFrame(1, 1, new TerminalImageColor(1, 2, 3));
        var request = new TerminalImageEncodeRequest(
            TerminalGraphicsProtocol.Kitty,
            new TerminalImageSize(1, 1),
            new TerminalImageSize(1, 1),
            null,
            TerminalImageScaleMode.Stretch,
            null,
            false);
        var encoder = new KittyTerminalImageEncoder();
        var encoded = await encoder.EncodeAsync(frame, request);
        var cache = new TerminalImageMemoryCache();
        var keyWithoutMatte = TerminalImageCacheKeys.Create(frame, request, "test-encoder");
        var keyWithMatte = TerminalImageCacheKeys.Create(frame, request with { MatteColor = TerminalImageColor.Black }, "test-encoder");

        cache.StoreEncoded(encoded);

        Assert.AreNotEqual(keyWithoutMatte, keyWithMatte);
        Assert.IsTrue(cache.TryGetEncoded(encoded.CacheKey, out var cached));
        Assert.AreSame(encoded, cached);
    }

    [TestMethod]
    public async Task EncodingService_ReusesSixelCacheByOptionAwareKey()
    {
        var source = TerminalImageSource.FromRgba32(new byte[] { 12, 34, 56, 255 }, 1, 1, "sixel-cache");
        var service = new TerminalImageEncodingService();
        var cache = new TerminalImageMemoryCache();
        var request = new TerminalImageEncodeRequest(
            TerminalGraphicsProtocol.Sixel,
            new TerminalImageSize(1, 1),
            new TerminalImageSize(1, 1),
            null,
            TerminalImageScaleMode.Stretch,
            TerminalImageColor.Black,
            false,
            TerminalImageResamplingQuality.Nearest,
            1024,
            null,
            null);

        var first = await service.EncodeAsync(source, TerminalImageFrameRequest.Default, request, cache);
        var second = await service.EncodeAsync(source, TerminalImageFrameRequest.Default, request, cache);

        Assert.IsNotNull(first);
        Assert.AreSame(first, second);
        Assert.AreEqual(1, cache.HitCount);
        Assert.AreEqual(1, cache.MissCount);
        Assert.AreEqual(1, cache.StoreCount);
        StringAssert.Contains(first.CacheKey, "dither=False");
    }

    [TestMethod]
    public async Task EncodingService_ReusesSixelCacheWhenMatteColorIsDefaulted()
    {
        var source = TerminalImageSource.FromRgba32(new byte[] { 12, 34, 56, 255 }, 1, 1, "sixel-default-matte-cache");
        var service = new TerminalImageEncodingService();
        var cache = new TerminalImageMemoryCache();
        var request = new TerminalImageEncodeRequest(
            TerminalGraphicsProtocol.Sixel,
            new TerminalImageSize(1, 1),
            new TerminalImageSize(1, 1),
            null,
            TerminalImageScaleMode.Stretch,
            null,
            false,
            TerminalImageResamplingQuality.Nearest,
            1024,
            null,
            null);

        var first = await service.EncodeAsync(source, TerminalImageFrameRequest.Default, request, cache);
        var second = await service.EncodeAsync(source, TerminalImageFrameRequest.Default, request, cache);

        Assert.IsNotNull(first);
        Assert.AreSame(first, second);
        Assert.AreEqual(1, cache.HitCount);
        Assert.AreEqual(1, cache.MissCount);
        Assert.AreEqual(1, cache.StoreCount);
        StringAssert.Contains(first.CacheKey, "matte=0,0,0,255");
    }

    [TestMethod]
    public async Task EncodingService_AppliesConfiguredSixelOptions()
    {
        var source = TerminalImageSource.FromRgba32(new byte[] { 255, 0, 0, 255 }, 1, 1, "sixel-rgb332-cache");
        var service = new TerminalImageEncodingService(sixelOptions: new TerminalSixelEncoderOptions
        {
            PaletteMode = TerminalSixelPaletteMode.FixedRgb332,
            EnableDithering = false,
        });
        var request = new TerminalImageEncodeRequest(
            TerminalGraphicsProtocol.Sixel,
            new TerminalImageSize(1, 1),
            new TerminalImageSize(1, 1),
            null,
            TerminalImageScaleMode.Stretch,
            TerminalImageColor.Black,
            false,
            TerminalImageResamplingQuality.Nearest,
            1024,
            null,
            null);
        var cache = new TerminalImageMemoryCache();

        var encoded = await service.EncodeAsync(source, TerminalImageFrameRequest.Default, request, cache);
        var cached = await service.EncodeAsync(source, TerminalImageFrameRequest.Default, request, cache);

        Assert.IsNotNull(encoded);
        Assert.AreSame(encoded, cached);
        StringAssert.Contains(encoded.CacheKey, "palette=rgb332");
        StringAssert.Contains(Encoding.ASCII.GetString(encoded.PayloadUtf8.Span), "#224");
    }

    [TestMethod]
    public async Task EncodingService_EncodeLatest_DropsSupersededFrames()
    {
        var firstGate = new TaskCompletionSource<TerminalImageFrame?>(TaskCreationOptions.RunContinuationsAsynchronously);
        var secondGate = new TaskCompletionSource<TerminalImageFrame?>(TaskCreationOptions.RunContinuationsAsynchronously);
        var source = new QueuedImageSource(firstGate.Task, secondGate.Task);
        var service = new TerminalImageEncodingService();
        var request = new TerminalImageEncodeRequest(
            TerminalGraphicsProtocol.Sixel,
            new TerminalImageSize(1, 1),
            new TerminalImageSize(1, 1),
            null,
            TerminalImageScaleMode.Stretch,
            TerminalImageColor.Black,
            false,
            TerminalImageResamplingQuality.Nearest,
            1024,
            null,
            null);

        var first = service.EncodeLatestAsync(source, TerminalImageFrameRequest.Default, request).AsTask();
        var second = service.EncodeLatestAsync(source, TerminalImageFrameRequest.Default, request).AsTask();

        firstGate.SetResult(CreateRawFrame(1, 1, new TerminalImageColor(255, 0, 0)));
        secondGate.SetResult(CreateRawFrame(1, 1, new TerminalImageColor(0, 255, 0)));

        Assert.IsNull(await first);
        Assert.IsNotNull(await second);
    }

    [TestMethod]
    public async Task WriteImageAsync_WritesKittySequenceToVirtualBackend()
    {
        var backend = new InMemoryTerminalBackend();
        Terminal.Initialize(backend, new TerminalOptions { ForceAnsi = true });
        var source = TerminalImageSource.FromRgba32(new byte[] { 255, 0, 0, 255 }, 1, 1, "direct");

        var encoded = await Terminal.Instance.WriteImageAsync(source, new TerminalImageWriteOptions
        {
            Protocol = TerminalGraphicsProtocol.Kitty,
            PixelSize = new TerminalImageSize(1, 1),
            CellSize = new TerminalImageSize(1, 1),
            ScaleMode = TerminalImageScaleMode.Stretch,
            PreserveAspectRatio = false,
            MaxPayloadChunkBytes = 128,
        });

        Assert.IsNotNull(encoded);
        var output = backend.GetOutText();
        StringAssert.StartsWith(output, "\x1b_G");
        StringAssert.Contains(output, "a=T");
        StringAssert.Contains(output, "f=32");
        StringAssert.Contains(output, "m=0;");
        StringAssert.EndsWith(output, "\x1b\\");
    }

    [TestMethod]
    public async Task WriteImageAsync_WritesFallbackText_WhenProtocolUnavailable()
    {
        var backend = new InMemoryTerminalBackend();
        var options = new TerminalOptions { ForceAnsi = true };
        options.Graphics.DisableGraphics = true;
        Terminal.Initialize(backend, options);
        var source = TerminalImageSource.FromRgba32(new byte[] { 255, 0, 0, 255 }, 1, 1, "direct-fallback");

        var encoded = await Terminal.Instance.WriteImageAsync(source, new TerminalImageWriteOptions
        {
            FallbackText = "[image unavailable]",
        });

        Assert.IsNull(encoded);
        Assert.AreEqual("[image unavailable]", backend.GetOutText());
    }

    [TestMethod]
    public async Task WriteImageAsync_CanReserveCellArea()
    {
        var backend = new InMemoryTerminalBackend();
        Terminal.Initialize(backend, new TerminalOptions { ForceAnsi = true });
        var source = TerminalImageSource.FromRgba32(new byte[] { 255, 0, 0, 255 }, 1, 1, "direct-reserve");

        var encoded = await Terminal.Instance.WriteImageAsync(source, new TerminalImageWriteOptions
        {
            Protocol = TerminalGraphicsProtocol.Kitty,
            PixelSize = new TerminalImageSize(1, 1),
            CellSize = new TerminalImageSize(2, 2),
            ScaleMode = TerminalImageScaleMode.Stretch,
            PreserveAspectRatio = false,
            MaxPayloadChunkBytes = 128,
            ReserveCellArea = true,
        });

        Assert.IsNotNull(encoded);
        var output = backend.GetOutText();
        StringAssert.Contains(output, "\x1b[?2026h");
        StringAssert.Contains(output, "\x1b_G");
        StringAssert.Contains(output, "a=T");
        Assert.IsGreaterThan(0, output.IndexOf("\x1b_G", StringComparison.Ordinal));
    }

    private static byte[] CreateEncodedImage(SKEncodedImageFormat format)
    {
        if (format == SKEncodedImageFormat.Webp)
        {
            var webp = TryCreateSkiaEncodedImage(format);
            if (webp is not null)
            {
                return webp;
            }

            return Convert.FromBase64String("UklGRjwAAABXRUJQVlA4IDAAAACwAgCdASoCAAIAAgA0JaQAA3AA/vuUAAA=");
        }

        return TryCreateSkiaEncodedImage(format) ?? throw new InvalidOperationException($"Unable to create a {format} test image.");
    }

    private static byte[]? TryCreateSkiaEncodedImage(SKEncodedImageFormat format)
    {
        using var bitmap = new SKBitmap(new SKImageInfo(2, 2, SKColorType.Rgba8888, SKAlphaType.Unpremul));
        bitmap.SetPixel(0, 0, SKColors.Red);
        bitmap.SetPixel(1, 0, SKColors.Green);
        bitmap.SetPixel(0, 1, SKColors.Blue);
        bitmap.SetPixel(1, 1, SKColors.White);
        using var image = SKImage.FromBitmap(bitmap);
        using var data = image.Encode(format, 100);
        return data?.ToArray();
    }

    private static TerminalImageFrame CreateRawFrame(int width, int height, TerminalImageColor color)
    {
        var pixels = new byte[width * height * 4];
        for (var y = 0; y < height; y++)
        {
            for (var x = 0; x < width; x++)
            {
                WritePixel(pixels, width, x, y, color);
            }
        }

        return new TerminalImageFrame
        {
            Format = TerminalImageFormat.RawRgba32,
            Data = pixels,
            PixelWidth = width,
            PixelHeight = height,
            SourceId = $"raw:{width}x{height}:{color.R},{color.G},{color.B},{color.A}",
        };
    }

    private static void WritePixel(byte[] pixels, int width, int x, int y, TerminalImageColor color)
    {
        var offset = ((y * width) + x) * 4;
        pixels[offset] = color.R;
        pixels[offset + 1] = color.G;
        pixels[offset + 2] = color.B;
        pixels[offset + 3] = color.A;
    }

    private sealed class QueuedImageSource : TerminalImageSource
    {
        private readonly ConcurrentQueue<Task<TerminalImageFrame?>> _frames;

        public QueuedImageSource(params Task<TerminalImageFrame?>[] frames)
        {
            _frames = new ConcurrentQueue<Task<TerminalImageFrame?>>(frames);
        }

        public override ValueTask<TerminalImageFrame?> GetFrameAsync(TerminalImageFrameRequest request, CancellationToken cancellationToken = default)
        {
            if (!_frames.TryDequeue(out var frame))
            {
                return ValueTask.FromResult<TerminalImageFrame?>(null);
            }

            return new ValueTask<TerminalImageFrame?>(frame.WaitAsync(cancellationToken));
        }
    }
}
