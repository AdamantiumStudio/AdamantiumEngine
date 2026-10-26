using System;
using System.IO;
using System.Linq;
using Adamantium.Imaging;
using Adamantium.Imaging.Tga;
using NUnit.Framework;

namespace Adamantium.ImagingTests;

/// <summary>
/// The TGA codec against readers independent of it - Pillow, ImageMagick, and TGA 2.0's own arithmetic where neither
/// reads as it says - and against itself: pictures written and read back, broken files refused.
/// </summary>
[TestFixture]
public class TgaCodecTests
{
    private static string Asset(string name) => TestAssets.Path($"tga/{name}");

    [TestCase("palette-rle-bottom")]
    [TestCase("gray-alpha-top")]
    [TestCase("gray-rle-top")]
    [TestCase("rgba-rle-bottom")]
    [TestCase("rgb555-alpha")]
    [TestCase("right-to-left")]
    [TestCase("premultiplied")]
    [TestCase("map-from-10")]
    public void APicture_ReadsAsItsReference(string name)
    {
        var tga = TgaImage.Read(File.ReadAllBytes(Asset(name + ".tga")));

        Assert.That(tga.GetRawPixels(0), Is.EqualTo(File.ReadAllBytes(Asset(name + ".rgba"))));
    }

    [Test]
    public void TheImageId_IsRead() =>
        Assert.That(TgaImage.Read(File.ReadAllBytes(Asset("right-to-left.tga"))).Id, Is.EqualTo("right-to-left"));

    // Gray, opaque color and translucent color, with and without run-length packets, come back exactly.
    [Test]
    public void Pictures_AreWrittenExactly([Values] bool compress, [Values(0, 1, 2)] int kind)
    {
        var random = new Random(kind);
        var pixels = new byte[29 * 17 * 4];
        for (var i = 0; i < 29 * 17; i++)
        {
            // Runs and noise.
            var value = (byte)(i % 9 < 4 ? i / 9 : random.Next(256));
            (pixels[i * 4], pixels[i * 4 + 1], pixels[i * 4 + 2], pixels[i * 4 + 3]) = kind switch
            {
                0 => (value, value, value, (byte)255),
                1 => (value, (byte)(value ^ 0x5A), (byte)random.Next(256), (byte)255),
                _ => (value, (byte)random.Next(256), (byte)7, (byte)random.Next(256))
            };
        }

        var stream = new MemoryStream();
        TgaEncoder.Encode(new FrameBitmap(29, 17, SurfaceFormat.R8G8B8A8.UNorm, [pixels]), stream, compress);

        Assert.That(TgaImage.Read(stream.ToArray()).GetRawPixels(0), Is.EqualTo(pixels));
    }

    // A file shorter than its pixels, plain or packed, and a picture of no size are refused.
    [Test]
    public void ABrokenFile_IsRefused()
    {
        var whole = File.ReadAllBytes(Asset("rgba-rle-bottom.tga"));
        var plain = File.ReadAllBytes(Asset("right-to-left.tga"));
        byte[] empty = [0, 0, 2, 0, 0, 0, 0, 0, 0, 0, 0, 0, 0, 0, 1, 0, 24, 0];

        Assert.Multiple(() =>
        {
            Assert.That(() => TgaImage.Read(whole.AsSpan(0, whole.Length / 2)), Throws.InstanceOf<InvalidDataException>());
            Assert.That(() => TgaImage.Read(plain.AsSpan(0, plain.Length - 1)), Throws.InstanceOf<InvalidDataException>());
            Assert.That(() => TgaImage.Read(empty), Throws.InstanceOf<InvalidDataException>());
            Assert.That(() => TgaImage.Read(whole.AsSpan(0, 10)), Throws.InstanceOf<InvalidDataException>());
        });
    }

    // 65535² pixels in a few packed bytes is a bomb, refused before anything is made.
    [Test]
    public void AHugePictureInAFewBytes_IsRefused()
    {
        byte[] tga = [0, 0, 10, 0, 0, 0, 0, 0, 0, 0, 0, 0, 0xFF, 0xFF, 0xFF, 0xFF, 32, 0x28, 0xFF, 1, 2, 3, 4];

        Assert.That(() => TgaImage.Read(tga), Throws.InstanceOf<InvalidDataException>());
    }

    // A 32-bit pixel's fourth byte is alpha though the descriptor counts no alpha bits, as Pillow and ImageMagick read
    // it; an extension area whose offset is past any file is no extension area.
    [TestCase(false)]
    [TestCase(true)]
    public void A32BitPixelsFourthByte_IsAlpha(bool hostileFooter)
    {
        byte[] tga = [0, 0, 2, 0, 0, 0, 0, 0, 0, 0, 0, 0, 1, 0, 1, 0, 32, 0x20, 10, 20, 30, 77];
        if (hostileFooter)
        {
            tga = [.. tga, 0xFF, 0xFF, 0xFF, 0xFF, 0, 0, 0, 0, .. "TRUEVISION-XFILE.\0"u8];
        }

        Assert.That(TgaImage.Read(tga).GetRawPixels(0), Is.EqualTo(new byte[] { 30, 20, 10, 77 }));
    }

    // Indices the color map has no entry for - below its first, past its last - are opaque black.
    [Test]
    public void IndicesPastTheColorMap_AreOpaqueBlack()
    {
        byte[] tga = [0, 1, 1, 2, 0, 1, 0, 24, 0, 0, 0, 0, 3, 0, 1, 0, 8, 0x20, 1, 2, 3, 0, 2, 9];

        Assert.That(TgaImage.Read(tga).GetRawPixels(0),
            Is.EqualTo(new byte[] { 0, 0, 0, 255, 3, 2, 1, 255, 0, 0, 0, 255 }));
    }

    [Test]
    public void AnUnknownImageType_IsNotRead()
    {
        byte[] tga = [0, 0, 32, 0, 0, 0, 0, 0, 0, 0, 0, 0, 1, 0, 1, 0, 8, 0, 7];

        Assert.That(() => TgaImage.Read(tga), Throws.InstanceOf<NotSupportedException>());
    }
}
