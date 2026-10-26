using System;
using System.IO;
using System.Linq;
using Adamantium.Imaging;
using Adamantium.Vulkan.Core;
using NUnit.Framework;

namespace Adamantium.ImagingTests;

/// <summary>Every encoder reads the pixels in the order and depth they say they are in.</summary>
[TestFixture]
public class EncoderFormatTests
{
    // 3×2: rows of 9 bytes at 24 bits, padded in BMP.
    private static readonly byte[] Colors =
        [200, 10, 20, 255, 30, 40, 220, 255, 15, 180, 60, 255, 90, 90, 90, 255, 250, 240, 5, 255, 0, 0, 0, 255];

    private static readonly byte[] Grays =
        [0, 0, 0, 255, 40, 40, 40, 255, 90, 90, 90, 255, 130, 130, 130, 255, 200, 200, 200, 255, 255, 255, 255, 255];

    private static readonly Format[] Formats =
    [
        Format.B8G8R8A8_UNORM, Format.R8G8B8_UNORM, Format.B8G8R8_UNORM, Format.R16G16B16A16_UNORM,
        Format.R16G16B16_UNORM, Format.R8_UNORM, Format.R16_UNORM
    ];

    [Test]
    public void PixelsOfAnyOrderOrDepth_AreWrittenInTheirColors(
        [Values(ImageFileType.Bmp, ImageFileType.Tga, ImageFileType.Png, ImageFileType.Tiff, ImageFileType.Gif)]
        ImageFileType type,
        [ValueSource(nameof(Formats))] Format format)
    {
        var gray = format is Format.R8_UNORM or Format.R16_UNORM;
        var expected = gray ? Grays : Colors;
        var stream = new MemoryStream();

        BitmapLoader.Save(new FrameBitmap(3, 2, format, [Pixels(expected, format)]), stream, type);
        stream.Position = 0;

        Assert.That(BitmapLoader.Load(stream).GetRgba8Pixels(), Is.EqualTo(expected));
    }

    // A BMP of 16 bits: 5-5-5 without masks, 5-6-5 by BI_BITFIELDS masks; a red pixel and a blue one.
    [TestCase(0u, new byte[] { 0x00, 0x7C, 0x1F, 0x00 })]
    [TestCase(3u, new byte[] { 0x00, 0xF8, 0x1F, 0x00 })]
    public void ABmpOfSixteenBits_ReadsInItsColors(uint compression, byte[] pixels)
    {
        byte[] masks = compression == 3 ? [0x00, 0xF8, 0, 0, 0xE0, 0x07, 0, 0, 0x1F, 0, 0, 0] : [];
        var offset = 54 + masks.Length;
        var bmp = new MemoryStream();
        var writer = new BinaryWriter(bmp);
        writer.Write("BM"u8);
        writer.Write(offset + pixels.Length);
        writer.Write(0);
        writer.Write(offset);
        writer.Write(40);
        writer.Write(2);
        writer.Write(1);
        writer.Write((ushort)1);
        writer.Write((ushort)16);
        writer.Write(compression);
        writer.Write(new byte[20]);
        writer.Write(masks);
        writer.Write(pixels);
        bmp.Position = 0;

        var read = BitmapLoader.Load(bmp);
        Assert.That((Format)read.PixelFormat, Is.EqualTo(Format.R8G8B8A8_UNORM));
        Assert.That(read.GetRgba8Pixels(), Is.EqualTo(new byte[] { 255, 0, 0, 255, 0, 0, 255, 255 }));
    }

    // A BMP of 24 bits reads as RGBA, a format every device samples - three channels of 8 bits most do not.
    [Test]
    public void ABmpOfTwentyFourBits_ReadsAsRgba()
    {
        var stream = new MemoryStream();
        BitmapLoader.Save(new FrameBitmap(3, 2, Format.R8G8B8A8_UNORM, [Colors]), stream, ImageFileType.Bmp);
        stream.Position = 0;

        var read = BitmapLoader.Load(stream);

        Assert.That((Format)read.PixelFormat, Is.EqualTo(Format.R8G8B8A8_UNORM));
        Assert.That(read.GetRawPixels(0), Is.EqualTo(Colors));
    }

    // RGBA of 8 bits in the format's channels; 16-bit channels repeat the byte, so their high byte is it.
    private static byte[] Pixels(byte[] rgba, Format format)
    {
        int[] order = format switch
        {
            Format.B8G8R8A8_UNORM => [2, 1, 0, 3],
            Format.R8G8B8_UNORM or Format.R16G16B16_UNORM => [0, 1, 2],
            Format.B8G8R8_UNORM => [2, 1, 0],
            Format.R16G16B16A16_UNORM => [0, 1, 2, 3],
            _ => [0]
        };
        var wide = format is Format.R16G16B16A16_UNORM or Format.R16G16B16_UNORM or Format.R16_UNORM;
        return Enumerable.Range(0, rgba.Length / 4)
            .SelectMany(pixel => order.SelectMany(c => wide
                ? new[] { rgba[pixel * 4 + c], rgba[pixel * 4 + c] }
                : new[] { rgba[pixel * 4 + c] }))
            .ToArray();
    }
}
