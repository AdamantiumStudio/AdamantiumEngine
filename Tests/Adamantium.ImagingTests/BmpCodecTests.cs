using System;
using System.IO;
using Adamantium.Imaging;
using Adamantium.Imaging.Bmp;
using NUnit.Framework;

namespace Adamantium.ImagingTests;

/// <summary>
/// The BMP codec against readers independent of it - Pillow and ImageMagick, and the Bitmap Storage page's own layout
/// for run-length and 4-bit files written by hand - and against itself: pictures written and read back, broken files
/// refused.
/// </summary>
[TestFixture]
public class BmpCodecTests
{
    private static string Asset(string name) => TestAssets.Path($"bmp/{name}");

    [TestCase("palette1")]
    [TestCase("palette4")]
    [TestCase("palette8")]
    [TestCase("gray")]
    [TestCase("rgb24")]
    [TestCase("rgba-v5")]
    [TestCase("rle4")]
    [TestCase("rle8")]
    [TestCase("top-down")]
    public void APicture_ReadsAsItsReference(string name) =>
        Assert.That(BmpImage.Read(File.ReadAllBytes(Asset(name + ".bmp"))).GetRawPixels(0),
            Is.EqualTo(File.ReadAllBytes(Asset(name + ".rgba"))));

    // Channels of 5 and 6 bits stretched to 8 round as ImageMagick does or a level off.
    [Test]
    public void FiveSixFive_ReadsWithinALevel()
    {
        var read = BmpImage.Read(File.ReadAllBytes(Asset("rgb565.bmp"))).GetRawPixels(0);
        var reference = File.ReadAllBytes(Asset("rgb565.rgba"));

        Assert.That(read, Has.Length.EqualTo(reference.Length));
        for (var i = 0; i < read.Length; i++)
        {
            Assert.That(read[i], Is.EqualTo(reference[i]).Within(1), $"byte {i}");
        }
    }

    // Opaque pictures go as 24 bits, translucent ones as 32 with their alpha; widths whose rows need padding too.
    [Test]
    public void Pictures_AreWrittenExactly([Values(1, 5, 8)] int width, [Values] bool translucent)
    {
        var random = new Random(width);
        var pixels = new byte[width * 3 * 4];
        random.NextBytes(pixels);
        for (var i = 3; i < pixels.Length; i += 4)
        {
            pixels[i] = translucent ? pixels[i] : (byte)255;
        }

        var stream = new MemoryStream();
        BmpEncoder.Encode(new FrameBitmap((uint)width, 3, SurfaceFormat.R8G8B8A8.UNorm, [pixels]), stream);
        var bytes = stream.ToArray();

        Assert.That(BmpImage.Read(bytes).GetRawPixels(0), Is.EqualTo(pixels));
        Assert.That(BitConverter.ToUInt16(bytes, 28), Is.EqualTo(translucent ? 32 : 24));
    }

    [Test]
    public void ABrokenFile_IsRefused()
    {
        var whole = File.ReadAllBytes(Asset("rgb24.bmp"));

        Assert.Multiple(() =>
        {
            Assert.That(() => BmpImage.Read(whole.AsSpan(0, whole.Length - 10)), Throws.InstanceOf<InvalidDataException>());
            Assert.That(() => BmpImage.Read(whole.AsSpan(0, 20)), Throws.InstanceOf<InvalidDataException>());
            Assert.That(() => BmpImage.Read("PNG"u8.ToArray()), Throws.InstanceOf<InvalidDataException>());
        });
    }

    // A run-length file cut off reads as far as it goes, the rest transparent; one that holds a PNG is not read.
    [Test]
    public void RunsCutOff_ReadAsFarAsTheyGo()
    {
        var whole = File.ReadAllBytes(Asset("rle8.bmp"));
        var cut = BmpImage.Read(whole.AsSpan(0, whole.Length / 2)).GetRawPixels(0);
        var png = (byte[])whole.Clone();
        png[30] = 5;

        Assert.That(cut[3], Is.EqualTo(0), "the top row, which bottom-up data reaches last, is not there");
        Assert.That(() => BmpImage.Read(png), Throws.InstanceOf<NotSupportedException>());
    }
}
