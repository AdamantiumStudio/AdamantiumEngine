using System;
using System.IO;
using System.Linq;
using Adamantium.Imaging;
using Adamantium.Imaging.Png;
using NUnit.Framework;
using CompressionLevel = System.IO.Compression.CompressionLevel;
using static Adamantium.ImagingTests.TestPng;

namespace Adamantium.ImagingTests;

/// <summary>
/// The PNG codec against readers independent of it - Adam7 as Pillow reads it, APNG frames as ffmpeg composes them -
/// and against itself: 16 bits and metadata kept through a write and a read, broken files refused.
/// </summary>
[TestFixture]
public class PngCodecTests
{
    private static string Asset(string name) => TestAssets.Path($"Png/{name}");

    [Test]
    public void TheCrc_IsPngs() => Assert.That(PngCrc.Compute("IEND"u8), Is.EqualTo(0xAE426082u));

    [Test]
    public void AnInterlacedImage_ReadsAsPillowReadsIt() =>
        Assert.That(BitmapLoader.Load(Asset("adam7.png")).GetRawPixels(0),
            Is.EqualTo(File.ReadAllBytes(Asset("adam7.rgba8"))));

    // Frames disposed of as Previous and blended Over, translucent over translucent: within rounding of ffmpeg.
    [Test]
    public void AnAnimationsFrames_ComposeAsApngSays()
    {
        var image = (PngImage)BitmapLoader.Load(Asset("apng-over-previous.png"));
        var expected = File.ReadAllBytes(Asset("apng-over-previous.rgba8"));
        var frameBytes = 40 * 30 * 4;

        Assert.That((image.FramesCount, image.NumberOfReplays), Is.EqualTo((5u, 2u)));
        Assert.That(image.Frames.Select(frame => frame.Delay.TotalMilliseconds), Is.EqualTo(new[] { 50, 60, 70, 80, 90 }));
        for (uint k = 0; k < 5; k++)
        {
            var actual = image.GetRawPixels(k);
            for (var i = 0; i < frameBytes; i += 4)
            {
                var transparent = actual[i + 3] == 0 && expected[k * frameBytes + i + 3] == 0;
                for (var c = transparent ? 3 : 0; c < 4; c++)
                {
                    Assert.That(actual[i + c], Is.EqualTo(expected[k * frameBytes + i + c]).Within(2),
                        $"frame {k}, pixel {i / 4}, channel {c}");
                }
            }
        }
    }

    // Frames asked for out of order compose the same as in order.
    [Test]
    public void FramesAskedForBackwards_AreTheFramesForwards()
    {
        var forwards = (PngImage)BitmapLoader.Load(Asset("apng-over-previous.png"));
        var backwards = (PngImage)BitmapLoader.Load(Asset("apng-over-previous.png"));
        var expected = Enumerable.Range(0, 5).Select(k => forwards.GetRawPixels((uint)k)).ToArray();

        for (var k = 4; k >= 0; k--)
        {
            backwards.ReleaseDecodedFrames();
            Assert.That(backwards.GetRawPixels((uint)k), Is.EqualTo(expected[k]), $"frame {k}");
        }
    }

    [Test]
    public void SixteenBitPixels_ComeBackSixteenBit()
    {
        var pixels = new byte[3 * 2 * 8];
        new Random(5).NextBytes(pixels);
        var source = new FrameBitmap(3, 2, SurfaceFormat.R16G16B16A16.UNorm, [pixels]);
        var stream = new MemoryStream();

        PngEncoder.Encode(source, stream);
        var read = PngImage.Read(stream.ToArray());

        Assert.That(read.PixelFormat, Is.EqualTo(SurfaceFormat.R16G16B16A16.UNorm));
        Assert.That(read.GetRawPixels(0), Is.EqualTo(pixels));
    }

    [Test]
    public void Metadata_IsWrittenAndReadBack()
    {
        var source = PngImage.Read(Build(1, 1, 8, 2, Zlib([0, 1, 2, 3], CompressionLevel.Optimal),
            ("gAMA", [0, 0, 0xB1, 0x8F]), ("tEXt", [.. "Title"u8, 0, .. "Map"u8]),
            ("pHYs", [0, 0, 0x0B, 0x13, 0, 0, 0x0B, 0x13, 1]), ("tIME", [0x07, 0xEA, 10, 26, 9, 30, 0])));
        source.Metadata.Texts.Add(new PngText("Comment", "Москва", "ru", "Комментарий", International: true,
            Compressed: true));
        var stream = new MemoryStream();

        PngEncoder.Encode(source, stream);
        var read = PngImage.Read(stream.ToArray()).Metadata;

        Assert.That(read.Gamma, Is.EqualTo(0.45455));
        Assert.That(read.Texts, Is.EqualTo(source.Metadata.Texts));
        Assert.That(read.PixelsPerUnit, Is.EqualTo((2835u, 2835u, true)));
        Assert.That(read.Time, Is.EqualTo(new DateTime(2026, 10, 26, 9, 30, 0, DateTimeKind.Utc)));
    }

    [Test]
    public void ABrokenCriticalChunk_RefusesTheFile()
    {
        var png = Build(1, 1, 8, 0, Zlib([0, 7], CompressionLevel.Optimal));
        png[16] ^= 1;

        Assert.That(() => PngImage.Read(png).GetRawPixels(0), Throws.InstanceOf<InvalidDataException>());
    }

    // A few bytes of data cannot hold a gigapixel: refused before anything is inflated.
    [Test]
    public void AHugeImageInAFewBytes_IsRefusedAtOnce()
    {
        var png = Build(20000, 20000, 8, 0, Zlib([0, 7], CompressionLevel.Optimal));
        var watch = System.Diagnostics.Stopwatch.StartNew();

        Assert.That(() => PngImage.Read(png).GetRawPixels(0), Throws.InstanceOf<InvalidDataException>());
        Assert.That(watch.ElapsedMilliseconds, Is.LessThan(1000));
    }

    // A picture said to be BGRA is written as such: red stays red.
    [Test]
    public void BgraPixels_AreWrittenInTheirColors()
    {
        var stream = new MemoryStream();

        PngEncoder.Encode(new FrameBitmap(1, 1, SurfaceFormat.B8G8R8A8.UNorm, [[10, 20, 30, 255]]), stream);

        Assert.That(PngImage.Read(stream.ToArray()).GetRawPixels(0), Is.EqualTo(new byte[] { 30, 20, 10, 255 }));
    }

    // A zTXt whose zlib stream is broken - here asking for a preset dictionary - is passed over, the picture read.
    [Test]
    public void ABrokenCompressedText_IsPassedOver()
    {
        var png = Build(1, 1, 8, 0, Zlib([0, 7], CompressionLevel.Optimal),
            ("zTXt", [.. "Bad"u8, 0, 0, 0x78, 0xBB, 0, 0, 0, 1, 0x01]));

        var image = PngImage.Read(png);

        Assert.That(image.Metadata.Texts, Is.Empty);
        Assert.That(image.GetRawPixels(0), Is.EqualTo(new byte[] { 7, 7, 7, 255 }));
    }

    // Texts that inflate past 64 MB all together are a bomb: those past it are passed over.
    [Test]
    public void TextsInflatingPastTheLimit_ArePassedOver()
    {
        var bomb = Zlib(new byte[30 << 20], CompressionLevel.SmallestSize);
        var chunks = Enumerable.Range(0, 4).Select(i => ("zTXt", (byte[])[.. "Big"u8, 0, 0, .. bomb])).ToArray();

        var image = PngImage.Read(Build(1, 1, 8, 0, Zlib([0, 7], CompressionLevel.Optimal), chunks));

        Assert.That(image.Metadata.Texts, Has.Count.EqualTo(2));
    }

    // An animation of one frame not covering the canvas: drawn at its place on a transparent canvas.
    [Test]
    public void ASingleFrameAtAnOffset_IsDrawnInItsPlace()
    {
        var data = Zlib([0, 9, 9], CompressionLevel.Optimal);
        var png = Build(2, 2, 8, 0, Zlib([0, 7, 7, 0, 7, 7], CompressionLevel.Optimal),
            ("acTL", [0, 0, 0, 1, 0, 0, 0, 0]));
        png = Insert(png, "fcTL", [0, 0, 0, 0, 0, 0, 0, 2, 0, 0, 0, 1, 0, 0, 0, 0, 0, 0, 0, 1, 0, 1, 0, 10, 0, 0]);
        png = Insert(png, "fdAT", [0, 0, 0, 1, .. data]);

        var pixels = PngImage.Read(png).GetRawPixels(0);

        Assert.That(pixels, Is.EqualTo(new byte[] { 0, 0, 0, 0, 0, 0, 0, 0, 9, 9, 9, 255, 9, 9, 9, 255 }));
    }

    // Every chunk between IDAT and IEND, as a writer appends them.
    private static byte[] Insert(byte[] png, string type, byte[] data)
    {
        var stream = new MemoryStream();
        stream.Write(png, 0, png.Length - 12);
        TestPng.Chunk(stream, type, data);
        stream.Write(png, png.Length - 12, 12);
        return stream.ToArray();
    }

    [Test]
    public void AnEightBitBackground_IsWrittenAtEightBits()
    {
        var source = PngImage.Read(Build(1, 1, 8, 0, Zlib([0, 200], CompressionLevel.Optimal), ("bKGD", [0, 128])));
        var stream = new MemoryStream();

        PngEncoder.Encode(source, stream);

        Assert.That(PngImage.Read(stream.ToArray()).Metadata.Background,
            Is.EqualTo(((ushort)(128 * 257), (ushort)(128 * 257), (ushort)(128 * 257))));
    }

    [TestCase("Key\0word")]
    [TestCase(" Leading")]
    [TestCase("")]
    public void AKeywordPngCannotHold_IsRefused(string keyword)
    {
        var source = PngImage.Read(Build(1, 1, 8, 0, Zlib([0, 7], CompressionLevel.Optimal)));
        source.Metadata.Texts.Add(new PngText(keyword, "text"));

        Assert.That(() => PngEncoder.Encode(source, new MemoryStream()), Throws.ArgumentException);
    }

    [Test]
    public void SixteenBitPixels_AreGivenAsEightBitsWhereAskedFor()
    {
        var bitmap = new FrameBitmap(1, 1, SurfaceFormat.R16G16B16A16.UNorm, [[0x11, 0xAA, 0x22, 0xBB, 0x33, 0xCC, 0, 0xFF]]);

        Assert.That(bitmap.GetRgba8Pixels(), Is.EqualTo(new byte[] { 0xAA, 0xBB, 0xCC, 0xFF }));
    }

    // What the reader keeps a writer can write back: a keyword's stray spaces, text past a NUL, a language tag with an
    // underscore; a keyword of a control character is passed over.
    [Test]
    public void TextsWritersSlipOn_AreReadAsTheyCanBeWritten()
    {
        var source = PngImage.Read(Build(1, 1, 8, 0, Zlib([0, 7], CompressionLevel.Optimal),
            ("tEXt", [.. "Author  "u8, 0, .. "Someone"u8, 0, .. "junk"u8]),
            ("iTXt", [.. "Title"u8, 0, 0, 0, .. "en_US"u8, 0, 0, .. "Map"u8]),
            ("tEXt", [1, .. "Bad"u8, 0, .. "x"u8])));
        var stream = new MemoryStream();

        PngEncoder.Encode(source, stream);

        Assert.That(PngImage.Read(stream.ToArray()).Metadata.Texts, Is.EqualTo(new[]
        {
            new PngText("Author", "Someone"), new PngText("Title", "Map", "en_US", "", International: true)
        }));
    }

    // Two readers asking for frames in turn, one going back, get what each frame composed from the start is - past the
    // canvases saved every 16 frames, through every way of disposing of a frame.
    [Test]
    public void FramesAskedForByTwoReadersInTurn_AreTheFramesComposedAfresh()
    {
        const int count = 40;
        var png = Build(2, 1, 8, 0, Zlib([0, 1, 2], CompressionLevel.Optimal),
            ("acTL", [.. Be(count), .. Be(0)]), ("fcTL", Control(0, 2, 0, 0)));
        for (var k = 1; k < count; k++)
        {
            png = Insert(png, "fcTL", Control(2 * k - 1, 1, k % 2, k % 3));
            png = Insert(png, "fdAT", [.. Be(2 * k), .. Zlib([0, (byte)(k * 6)], CompressionLevel.Optimal)]);
        }

        var afresh = PngImage.Read(png);
        var expected = Enumerable.Range(0, count).Select(k =>
        {
            afresh.ReleaseDecodedFrames();
            return afresh.GetRawPixels((uint)k);
        }).ToArray();
        var image = PngImage.Read(png);

        Assert.That(image.FramesCount, Is.EqualTo(count));
        for (var k = 0; k < count; k++)
        {
            Assert.That(image.GetRawPixels((uint)k), Is.EqualTo(expected[k]), $"frame {k}");
            Assert.That(image.GetRawPixels((uint)(count - 1 - k)), Is.EqualTo(expected[count - 1 - k]),
                $"frame {count - 1 - k}");
        }
    }

    private static byte[] Be(int value) => [(byte)(value >> 24), (byte)(value >> 16), (byte)(value >> 8), (byte)value];

    // fcTL of a frame one row high.
    private static byte[] Control(int sequence, int width, int x, int dispose) =>
        [.. Be(sequence), .. Be(width), .. Be(1), .. Be(x), .. Be(0), 0, 1, 0, 10, (byte)dispose, 0];

    // A frame whose data breaks off throws, and leaves the canvas as it was: the frame before is still itself.
    [Test]
    public void AFrameThatFailsToDecode_LeavesTheCanvasAsItWas()
    {
        var png = Build(2, 1, 8, 0, Zlib([0, 1, 1], CompressionLevel.Optimal),
            ("acTL", [.. Be(3), .. Be(0)]), ("fcTL", Control(0, 2, 0, 0)));
        png = Insert(png, "fcTL", Control(1, 1, 0, 1));
        png = Insert(png, "fdAT", [.. Be(2), .. Zlib([0, 9], CompressionLevel.Optimal)]);
        png = Insert(png, "fcTL", Control(3, 1, 0, 0));
        png = Insert(png, "fdAT", [.. Be(4), .. Zlib([0, 5], CompressionLevel.Optimal)[..3]]);
        var image = PngImage.Read(png);

        Assert.That(image.GetRawPixels(1), Is.EqualTo(new byte[] { 9, 9, 9, 255, 1, 1, 1, 255 }));
        Assert.That(() => image.GetRawPixels(2), Throws.InstanceOf<InvalidDataException>());
        image.GetRawPixels(0);
        Assert.That(image.GetRawPixels(1), Is.EqualTo(new byte[] { 9, 9, 9, 255, 1, 1, 1, 255 }));
    }

    // A still picture that is the first frame covers the canvas; one that does not breaks the animation.
    [Test]
    public void AFirstFrameNotCoveringTheCanvas_ReadsAsTheStillPicture()
    {
        var png = Build(2, 1, 8, 0, Zlib([0, 7, 8], CompressionLevel.Optimal),
            ("acTL", [.. Be(1), .. Be(0)]), ("fcTL", Control(0, 1, 1, 0)));

        var image = PngImage.Read(png);

        Assert.That(image.FramesCount, Is.EqualTo(1));
        Assert.That(image.GetRawPixels(0), Is.EqualTo(new byte[] { 7, 7, 7, 255, 8, 8, 8, 255 }));
    }

    // The still picture not shown is refused all the same when its data cannot hold it - the canvas is its size.
    [Test]
    public void AHiddenStillPictureInAFewBytes_IsRefused()
    {
        var png = Build(20000, 20000, 8, 0, Zlib([0, 7], CompressionLevel.Optimal), ("acTL", [.. Be(1), .. Be(0)]));
        png = Insert(png, "fcTL", Control(0, 1, 0, 0));
        png = Insert(png, "fdAT", [.. Be(1), .. Zlib([0, 7], CompressionLevel.Optimal)]);

        Assert.That(() => PngImage.Read(png), Throws.InstanceOf<InvalidDataException>());
    }

    // A profile name with a stray space is read as PNG would have it, so it is written again; with sRGB beside it the
    // profile is kept, as PNG ranks it above sRGB.
    [Test]
    public void AProfile_IsWrittenBackOverSrgb()
    {
        byte[] profile = [1, 2, 3, 4, 5];
        var source = PngImage.Read(Build(1, 1, 8, 0, Zlib([0, 7], CompressionLevel.Optimal),
            ("iCCP", [.. "ICC profile "u8, 0, 0, .. Zlib(profile, CompressionLevel.Optimal)]), ("sRGB", [0])));
        var stream = new MemoryStream();

        PngEncoder.Encode(source, stream);
        var read = PngImage.Read(stream.ToArray()).Metadata;

        Assert.That((read.IccProfileName, read.IccProfile, read.SrgbIntent),
            Is.EqualTo(("ICC profile", profile, (PngRenderingIntent?)null)));
    }

    // A background of a color is kept with gray pixels - they are then not written as gray - and with a palette that
    // has no entry of it.
    [TestCase(0, new byte[] { 0, 128, 128 })]
    [TestCase(2, new byte[] { 0, 10, 20, 30, 10, 20, 30 })]
    public void AColoredBackground_IsKept(byte colorType, byte[] row)
    {
        var source = PngImage.Read(Build(2, 1, 8, colorType, Zlib(row, CompressionLevel.Optimal)));
        source.Metadata.Background = (65535, 0, 0);
        var stream = new MemoryStream();

        PngEncoder.Encode(source, stream);

        Assert.That(PngImage.Read(stream.ToArray()).Metadata.Background, Is.EqualTo(((ushort)65535, (ushort)0, (ushort)0)));
    }

    // Fewer frames than acTL says break the animation: the file reads as the still picture it also is.
    [Test]
    public void ABrokenAnimation_ReadsAsItsStillPicture()
    {
        var png = Build(1, 1, 8, 0, Zlib([0, 7], CompressionLevel.Optimal),
            ("acTL", [0, 0, 0, 2, 0, 0, 0, 0]),
            ("fcTL", [0, 0, 0, 0, 0, 0, 0, 1, 0, 0, 0, 1, 0, 0, 0, 0, 0, 0, 0, 0, 0, 1, 0, 10, 0, 0]));

        var image = PngImage.Read(png);

        Assert.That(image.FramesCount, Is.EqualTo(1));
        Assert.That(image.GetRawPixels(0), Is.EqualTo(new byte[] { 7, 7, 7, 255 }));
    }
}
