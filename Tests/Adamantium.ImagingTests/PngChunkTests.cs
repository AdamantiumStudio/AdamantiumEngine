using System;
using System.IO;
using Adamantium.Imaging;
using Adamantium.Imaging.Png;
using NUnit.Framework;
using CompressionLevel = System.IO.Compression.CompressionLevel;
using static Adamantium.ImagingTests.TestPng;

namespace Adamantium.ImagingTests;

/// <summary>
/// Chunks around the image data: the background color keeps all three channels, palettes and their transparency
/// color the pixels, and an unknown chunk stops the decode only when it is critical.
/// </summary>
[TestFixture]
public class PngChunkTests
{
    private static byte[] Rgb2x2(params (string Type, byte[] Data)[] chunks) =>
        Build(2, 2, 8, 2, Zlib(new byte[2 * (1 + 2 * 3)], CompressionLevel.Optimal), chunks);

    // The background's samples are of the image's depth; they come out scaled to 16 bits.
    [Test]
    public void RgbBackground_KeepsItsGreen()
    {
        var png = Rgb2x2(("bKGD", [0x00, 0x11, 0x00, 0x22, 0x00, 0x33]));

        var metadata = ((PngImage)BitmapLoader.Load(new MemoryStream(png))).Metadata;

        Assert.That(metadata.Background, Is.EqualTo(((ushort)0x1111, (ushort)0x2222, (ushort)0x3333)));
    }

    [Test]
    public void APaletteBackground_IsItsPaletteEntry()
    {
        var png = Build(2, 1, 8, 3, Zlib([0, 0, 1], CompressionLevel.Optimal),
            ("PLTE", [10, 20, 30, 40, 50, 60]), ("bKGD", [1]));

        var metadata = ((PngImage)BitmapLoader.Load(new MemoryStream(png))).Metadata;

        Assert.That(metadata.Background, Is.EqualTo(((ushort)(40 * 257), (ushort)(50 * 257), (ushort)(60 * 257))));
    }

    [Test]
    public void APaletteImage_DecodesToItsColorsAndAlphas()
    {
        var png = Build(2, 1, 8, 3, Zlib([0, 0, 1], CompressionLevel.Optimal),
            ("PLTE", [10, 20, 30, 40, 50, 60]), ("tRNS", [128]));

        var pixels = BitmapLoader.Load(new MemoryStream(png)).GetRawPixels(0);

        Assert.That(pixels, Is.EqualTo(new byte[] { 10, 20, 30, 128, 40, 50, 60, 255 }));
    }

    [Test]
    public void UnknownAncillaryChunk_IsSkipped()
    {
        var png = Rgb2x2(("sBIT", [8, 8, 8]));

        Assert.That(BitmapLoader.Load(new MemoryStream(png)).GetRawPixels(0), Has.Length.EqualTo(2 * 2 * 4));
    }

    [Test]
    public void UnknownCriticalChunk_StopsTheDecode()
    {
        var png = Rgb2x2(("ABCD", [1]));

        Assert.Throws<NotSupportedException>(() => BitmapLoader.Load(new MemoryStream(png)));
    }
}
