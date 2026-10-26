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

    // A file header and an info header of 40 bytes, then what follows it.
    private static byte[] Bmp(int width, int height, ushort bits, uint compression, params byte[] after)
    {
        var file = new byte[54 + after.Length];
        "BM"u8.CopyTo(file);
        BitConverter.GetBytes(file.Length).CopyTo(file, 2);
        BitConverter.GetBytes(54 + (compression == 3 ? 12 : 0) + (bits <= 8 ? 4 << bits : 0)).CopyTo(file, 10);
        BitConverter.GetBytes(40).CopyTo(file, 14);
        BitConverter.GetBytes(width).CopyTo(file, 18);
        BitConverter.GetBytes(height).CopyTo(file, 22);
        BitConverter.GetBytes((ushort)1).CopyTo(file, 26);
        BitConverter.GetBytes(bits).CopyTo(file, 28);
        BitConverter.GetBytes(compression).CopyTo(file, 30);
        after.CopyTo(file, 54);
        return file;
    }

    // A header whose size its file cannot hold, a run-length one past the pixels read, a height of int's least: refused,
    // nothing their size made first.
    [Test]
    public void AHostileHeader_IsRefusedBeforeAnythingIsMade()
    {
        var before = GC.GetAllocatedBytesForCurrentThread();
        Assert.That(() => BmpImage.Read(Bmp(20000, 20000, 24, 0, 0, 0, 0, 0)), Throws.InstanceOf<InvalidDataException>());
        Assert.That(() => BmpImage.Read(Bmp(60000, 60000, 8, 1, new byte[1024 + 2])),
            Throws.InstanceOf<NotSupportedException>());
        Assert.That(() => BmpImage.Read(Bmp(1, int.MinValue, 24, 0, new byte[4])),
            Throws.InstanceOf<InvalidDataException>());
        Assert.That(GC.GetAllocatedBytesForCurrentThread() - before, Is.LessThan(1 << 20));
    }

    // A core header's height is an unsigned word: 40000 rows, not a picture stored top-down.
    [Test]
    public void ACoreHeadersHeight_IsUnsigned()
    {
        var file = new byte[26 + 4 * 40000];
        "BM"u8.CopyTo(file);
        BitConverter.GetBytes(26).CopyTo(file, 10);
        BitConverter.GetBytes(12).CopyTo(file, 14);
        BitConverter.GetBytes((ushort)1).CopyTo(file, 18);
        BitConverter.GetBytes((ushort)40000).CopyTo(file, 20);
        BitConverter.GetBytes((ushort)1).CopyTo(file, 22);
        BitConverter.GetBytes((ushort)24).CopyTo(file, 24);

        Assert.That(BmpImage.Read(file).Height, Is.EqualTo(40000));
    }

    // A mask whose bits are not side by side scales to its own largest value: no channel runs into the next.
    [Test]
    public void ABrokenMask_StaysInItsChannel()
    {
        var file = Bmp(1, 1, 32, 3, 0b101, 0, 0, 0, 0, 0, 0, 0, 0, 0, 0, 0, 0b101, 0, 0, 0);

        Assert.That(BmpImage.Read(file).GetRawPixels(0), Is.EqualTo(new byte[] { 255, 0, 0, 255 }));
    }

    // A run-length file cut off reads as far as it goes, the rest the table's first color as ImageMagick and Pillow
    // read it; one that holds a PNG is not read.
    [Test]
    public void RunsCutOff_ReadAsFarAsTheyGo()
    {
        var whole = File.ReadAllBytes(Asset("rle8.bmp"));
        var cut = BmpImage.Read(whole.AsSpan(0, whole.Length / 2)).GetRawPixels(0);
        var png = (byte[])whole.Clone();
        png[30] = 5;
        var table = 14 + BitConverter.ToInt32(whole, 14);

        Assert.That(cut[..4], Is.EqualTo(new byte[] { whole[table + 2], whole[table + 1], whole[table], 255 }),
            "the top row, which bottom-up data reaches last, is not there");
        Assert.That(() => BmpImage.Read(png), Throws.InstanceOf<NotSupportedException>());
    }
}
