using System;
using System.IO;
using Adamantium.Imaging;
using Adamantium.Imaging.Tiff;
using Adamantium.Vulkan.Core;
using NUnit.Framework;

namespace Adamantium.ImagingTests;

/// <summary>
/// What the TIFF encoder writes reads back the same - every pixel format it takes, every compression, strips and
/// tiles, pages - and the file holds the tags a reader needs.
/// </summary>
[TestFixture]
public class TiffEncodeTests
{
    private static byte[] Pattern(int length, int seed)
    {
        var bytes = new byte[length];
        var random = new Random(seed);
        for (var i = 0; i < length; i++)
        {
            bytes[i] = (byte)(i % 7 == 0 ? random.Next(256) : (i / 5 + seed) % 256);
        }

        return bytes;
    }

    private static TiffImage RoundTrip(IRawBitmap bitmap, TiffSaveOptions options)
    {
        var stream = new MemoryStream();
        TiffEncoder.Save(bitmap, stream, options);
        return TiffImage.Load(stream.ToArray());
    }

    [TestCase(Format.R8_UNORM, Format.R8G8B8A8_UNORM)]
    [TestCase(Format.R8G8B8_UNORM, Format.R8G8B8A8_UNORM)]
    [TestCase(Format.R8G8B8A8_UNORM, Format.R8G8B8A8_UNORM)]
    [TestCase(Format.R16G16B16A16_UNORM, Format.R16G16B16A16_UNORM)]
    [TestCase(Format.R32G32B32A32_SFLOAT, Format.R32G32B32A32_SFLOAT)]
    [TestCase(Format.R16_SINT, Format.R16_SINT)]
    [TestCase(Format.R32_UINT, Format.R32_UINT)]
    public void EveryCompression_ReadsBackTheSamePicture(Format written, Format read)
    {
        const uint width = 45;
        const uint height = 29;
        var size = ((SurfaceFormat)written).SizeInBytes;
        var pixels = Pattern((int)(width * height) * size, 3);
        if (written == Format.R32G32B32A32_SFLOAT)
        {
            for (var i = 0; i < pixels.Length; i += 4)
            {
                BitConverter.TryWriteBytes(pixels.AsSpan(i), (i % 97) / 7f - 3);
            }
        }

        var bitmap = new FrameBitmap(width, height, written, [pixels]);
        foreach (var compression in (TiffCompression[])[TiffCompression.None, TiffCompression.PackBits,
                     TiffCompression.Lzw, TiffCompression.Deflate])
        {
            foreach (var (tile, big) in ((int, bool)[])[(0, false), (16, false), (0, true)])
            {
                var options = new TiffSaveOptions { Compression = compression, TileSize = tile, ForceBigTiff = big };
                var back = RoundTrip(bitmap, options);
                var what = $"{compression}, tile {tile}, BigTIFF {big}";

                Assert.That(back.PixelFormat, Is.EqualTo((SurfaceFormat)read), what);
                Assert.That(back.GetRawPixels(0), Is.EqualTo(Expected(pixels, written)), what);
            }
        }
    }

    [Test]
    public void BlueFirstPixels_AreWrittenRedFirst()
    {
        var bitmap = new FrameBitmap(1, 1, Format.B8G8R8A8_UNORM, [[10, 20, 30, 40]]);

        Assert.That(RoundTrip(bitmap, new TiffSaveOptions()).GetRawPixels(0), Is.EqualTo(new byte[] { 30, 20, 10, 40 }));
    }

    [Test]
    public void EveryFrame_IsAPage()
    {
        var frames = (byte[][])[Pattern(12 * 9 * 4, 1), Pattern(12 * 9 * 4, 2), Pattern(12 * 9 * 4, 3)];
        var back = RoundTrip(new FrameBitmap(12, 9, Format.R8G8B8A8_UNORM, frames), new TiffSaveOptions());

        Assert.That(back.FramesCount, Is.EqualTo(3));
        for (uint i = 0; i < 3; i++)
        {
            Assert.That(back.GetRawPixels(i), Is.EqualTo(frames[i]), $"page {i}");
        }
    }

    [Test]
    public void AnImageSavedAsTiff_LoadsBack()
    {
        var image = Adamantium.Imaging.Image.New2D(7, 5, 1, SurfaceFormat.R8G8B8A8.UNorm);
        var pixels = Pattern(7 * 5 * 4, 9);
        System.Runtime.InteropServices.Marshal.Copy(pixels, 0, image.PixelBuffer[0].DataPointer, pixels.Length);
        var stream = new MemoryStream();

        BitmapLoader.Save(image.ConvertToRawBitmap(ImageFileType.Tiff), stream, ImageFileType.Tiff);

        Assert.That(BitmapLoader.Load(new MemoryStream(stream.ToArray())).GetRawPixels(0), Is.EqualTo(pixels));
    }

    [Test]
    public void AFormatTiffCannotHold_IsRefused() =>
        Assert.That(() => TiffEncoder.Save(new FrameBitmap(1, 1, Format.BC1_RGB_UNORM_BLOCK, [new byte[8]]),
            new MemoryStream()), Throws.InstanceOf<NotSupportedException>());

    private static byte[] Expected(byte[] pixels, Format written)
    {
        var count = written switch
        {
            Format.R8_UNORM => pixels.Length,
            Format.R8G8B8_UNORM => pixels.Length / 3,
            _ => 0
        };
        if (count == 0)
        {
            return pixels;
        }

        var rgba = new byte[count * 4];
        var samples = pixels.Length / count;
        for (var i = 0; i < count; i++)
        {
            for (var c = 0; c < 3; c++)
            {
                rgba[i * 4 + c] = pixels[i * samples + (samples == 1 ? 0 : c)];
            }

            rgba[i * 4 + 3] = 255;
        }

        return rgba;
    }
}
