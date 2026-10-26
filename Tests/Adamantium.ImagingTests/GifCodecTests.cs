using System;
using System.IO;
using System.Linq;
using System.Security.Cryptography;
using Adamantium.Imaging;
using Adamantium.Imaging.Gif;
using NUnit.Framework;

namespace Adamantium.ImagingTests;

/// <summary>
/// The GIF codec against readers independent of it - every frame as Pillow and ffmpeg both compose it - and against
/// itself: pictures written and read back, LZW through every table size, the palette reduced near the colors it
/// stands for, broken files refused or read as far as they go.
/// </summary>
[TestFixture]
public class GifCodecTests
{
    private static string Asset(string name) => TestAssets.Path($"gif/{name}");

    private static GifImage Read(string name) => GifImage.Read(File.ReadAllBytes(Asset(name)));

    private static string Hash(GifImage gif, int frames)
    {
        using var sha = SHA256.Create();
        for (uint i = 0; i < frames; i++)
        {
            var pixels = gif.GetRawPixels(i);
            sha.TransformBlock(pixels, 0, pixels.Length, null, 0);
        }

        sha.TransformFinalBlock([], 0, 0);
        return Convert.ToHexString(sha.Hash);
    }

    // Hashes of the frames Pillow 12 and ffmpeg 8 both decode, transparent pixels as zeros.
    [TestCase("bubble_animation.gif", "864DF4EEFFA0915495428746FE49D519E1128F9C6DDCD444C06D2951ACF69CA3")]
    [TestCase("interlaced.gif", "8DB1F1692C879D044EFD0378BB5FDAD998160E1A66D541166CEA693DC8750CF8")]
    [TestCase("RotatingEarth2.gif", "2222CFE44F52F445736CBC82B5FF24AC287FEBF1EBA8612E58B40EE669C72149")]
    [TestCase("Rotating_earth.gif", "E9025ED6949CDD5139E7D9429FBF0697F4FD77E1C8587A97D4DA7946BE9EE602")]
    [TestCase("infinity.gif", "D92B0786C4E19B05B2219010BC1111DDA93B05793FB8776AA9F0DBA863E5807F")]
    [TestCase("dispose-background.gif", "643389F1ABADE3400DF657114424859BA3A213820716E071392372AF56E1D524")]
    [TestCase("dispose-previous.gif", "D7C65941D50B2E0A8F9360CEA9BF0BCB26EB024AEE015B8249EEACC5D5BB99BF")]
    [TestCase("local-tables.gif", "B49FD1673941740D36F90DC9D4885C1274A4D57326A6AC8972BCC05A74717486")]
    [TestCase("interlaced-still.gif", "42001A1ABA3985F087F3B6171FE5390DFF969247C5A84F78C14F34E4E0AFF063")]
    public void EveryFrame_IsAsPillowAndFfmpegCompose(string name, string expected)
    {
        var gif = Read(name);

        Assert.That(Hash(gif, (int)gif.FramesCount), Is.EqualTo(expected));
    }

    // A file cut off in its last frame keeps the frames before as ffmpeg reads them, and the last as far as it goes.
    [Test]
    public void AFileCutOff_KeepsTheFramesBefore()
    {
        var gif = Read("truncated.gif");

        Assert.That(gif.FramesCount, Is.EqualTo(6));
        Assert.That(Hash(gif, 5), Is.EqualTo("7624F8F11ACA1BAD469892F6101ADCCDB7E56481173CB6473F8F1F6CFC4D9A77"));
        Assert.That(() => gif.GetRawPixels(5), Throws.Nothing);
    }

    [Test]
    public void TimingAndPlays_AreRead()
    {
        var gif = Read("dispose-previous.gif");

        Assert.That(gif.Frames.Select(frame => frame.Delay.TotalMilliseconds),
            Is.EqualTo(new[] { 30, 40, 50, 60, 70, 80 }));
        Assert.That(gif.Frames.Select(frame => frame.Disposal), Is.All.EqualTo(GifDisposal.Previous));
        // NETSCAPE2.0 says 3 repeats: four plays.
        Assert.That(gif.NumberOfReplays, Is.EqualTo(4));
    }

    // Frames copied out one by one, and played - early frames asked for between them - are the frames in order.
    [Test]
    public void FramesCopiedWhileOthersArePlayed_AreTheFramesInOrder()
    {
        var alone = Read("infinity.gif");
        var shared = Read("infinity.gif");
        var frame = new byte[((IRawBitmap)shared).TotalSizeInBytes];
        var expected = new byte[frame.Length];

        for (uint i = 0; i < shared.FramesCount; i++)
        {
            shared.CopyFrame(i, frame);
            alone.CopyFrame(i, expected);
            Assert.That(frame.AsSpan().SequenceEqual(expected), Is.True, $"copied frame {i}");

            var played = i / 2;
            Assert.That(shared.GetRawPixels(played).AsSpan().SequenceEqual(alone.GetRawPixels(played)), Is.True,
                $"played frame {played}");
        }
    }

    // A picture of 256 colors or fewer, transparent pixels among them, comes back exactly - frames, delays, plays.
    [Test]
    public void FewColors_AreWrittenExactly()
    {
        var random = new Random(3);
        var colors = Enumerable.Range(0, 200).Select(_ => new[]
        {
            (byte)random.Next(256), (byte)random.Next(256), (byte)random.Next(256), (byte)255
        }).ToArray();
        var frames = Enumerable.Range(0, 3).Select(_ =>
        {
            var pixels = new byte[37 * 23 * 4];
            for (var i = 0; i < 37 * 23; i++)
            {
                (i % 11 == 0 ? new byte[4] : colors[random.Next(colors.Length)]).CopyTo(pixels, i * 4);
            }

            return pixels;
        }).ToArray();
        var stream = new MemoryStream();

        GifEncoder.Encode(new FrameBitmap(37, 23, SurfaceFormat.R8G8B8A8.UNorm, frames), stream);
        var read = GifImage.Read(stream.ToArray());

        Assert.That(read.FramesCount, Is.EqualTo(3));
        for (uint i = 0; i < 3; i++)
        {
            Assert.That(read.GetRawPixels(i), Is.EqualTo(frames[i]), $"frame {i}");
        }

        Assert.That(read.Frames.Select(frame => frame.DelayCentiseconds), Is.All.EqualTo(10));
        Assert.That(read.NumberOfReplays, Is.Zero);
    }

    // A frame with transparent pixels after an opaque one shows them transparent, not the frame before through them;
    // a frame that changes a little is written as that little.
    [Test]
    public void FramesAfterOneAnother_AreShownAsThemselves()
    {
        byte[] red = [200, 0, 0, 255], blue = [0, 0, 200, 255], clear = [0, 0, 0, 0];
        byte[] Frame(params byte[][] pixels) => pixels.SelectMany(pixel => pixel).ToArray();
        byte[][] frames =
        [
            Frame(red, red, red, red), Frame(red, blue, red, red), Frame(red, blue, blue, red),
            Frame(clear, blue, red, clear)
        ];
        var stream = new MemoryStream();

        GifEncoder.Encode(new FrameBitmap(2, 2, SurfaceFormat.R8G8B8A8.UNorm, frames), stream);
        var read = GifImage.Read(stream.ToArray());

        Assert.That(Enumerable.Range(0, 4).Select(i => read.GetRawPixels((uint)i)), Is.EqualTo(frames));
        Assert.That(read.Frames[1], Is.EqualTo(new GifFrame(1, 0, 1, 1, 10, GifDisposal.None)));
        Assert.That(read.Frames[2], Is.EqualTo(new GifFrame(0, 0, 2, 2, 10, GifDisposal.Background)));
    }

    // A GIF written again keeps its frames, delays and plays.
    [Test]
    public void AGifWrittenAgain_KeepsItsFramesAndTiming()
    {
        var source = Read("dispose-previous.gif");
        var stream = new MemoryStream();

        GifEncoder.Encode(source, stream);
        var read = GifImage.Read(stream.ToArray());

        Assert.That(Hash(read, 6), Is.EqualTo(Hash(source, 6)));
        Assert.That(read.Frames.Select(frame => frame.DelayCentiseconds), Is.EqualTo(new[] { 3, 4, 5, 6, 7, 8 }));
        Assert.That(read.NumberOfReplays, Is.EqualTo(4));
    }

    // Many colors are reduced to 256 near the colors they stand for: a field of 32768 colors, with and without
    // dithering - Pillow's median cut gets 35.4 dB of it, ImageMagick 35.4 plain and 34.8 dithered.
    [TestCase(true, 33.0)]
    [TestCase(false, 35.5)]
    public void ManyColors_AreReducedNearThem(bool dither, double leastPsnr)
    {
        const int width = 256, height = 128;
        var pixels = new byte[width * height * 4];
        for (var y = 0; y < height; y++)
        for (var x = 0; x < width; x++)
        {
            var at = (y * width + x) * 4;
            (pixels[at], pixels[at + 1], pixels[at + 2], pixels[at + 3]) =
                ((byte)x, (byte)(y * 2), (byte)((x + y) / 2), 255);
        }

        var quantized = ColorQuantizer.Quantize(pixels, width, height, 256, dither);
        double error = 0;
        for (var i = 0; i < width * height; i++)
        {
            for (var c = 0; c < 3; c++)
            {
                double d = quantized.Palette[quantized.Indices[i] * 3 + c] - pixels[i * 4 + c];
                error += d * d;
            }
        }

        var psnr = 10 * Math.Log10(255.0 * 255 / (error / (width * height * 3)));
        Assert.That(quantized.Colors, Is.LessThanOrEqualTo(256));
        Assert.That(quantized.TransparentIndex, Is.EqualTo(-1));
        Assert.That(psnr, Is.GreaterThan(leastPsnr));
    }

    // Colors close together still get a palette entry each as far as the palette goes: 256 grays and a transparent
    // pixel keep 255 grays; 4096 colors of 0 to 15 a channel all 256 entries, within a level of them - the boxes of 16
    // colors have means between two levels, a palette entry is whole levels.
    [Test]
    public void CloseColors_FillThePalette()
    {
        var grays = new byte[257 * 4];
        for (var i = 0; i < 256; i++)
        {
            (grays[i * 4], grays[i * 4 + 1], grays[i * 4 + 2], grays[i * 4 + 3]) = ((byte)i, (byte)i, (byte)i, 255);
        }

        var cube = new byte[4096 * 4];
        for (var i = 0; i < 4096; i++)
        {
            (cube[i * 4], cube[i * 4 + 1], cube[i * 4 + 2], cube[i * 4 + 3]) =
                ((byte)(i & 15), (byte)(i >> 4 & 15), (byte)(i >> 8), 255);
        }

        var gray = ColorQuantizer.Quantize(grays, 257, 1, 256, false);
        var colors = ColorQuantizer.Quantize(cube, 4096, 1, 256, false);
        double error = 0;
        for (var i = 0; i < 4096; i++)
        {
            for (var c = 0; c < 3; c++)
            {
                double d = colors.Palette[colors.Indices[i] * 3 + c] - cube[i * 4 + c];
                error += d * d;
            }
        }

        Assert.That(gray.Colors, Is.EqualTo(256));
        Assert.That(gray.TransparentIndex, Is.EqualTo(255));
        Assert.That(colors.Colors, Is.EqualTo(256));
        Assert.That(error / (4096 * 3), Is.LessThan(1));
    }

    // A GIF that plays once is written to play once: no loop count, which would mean forever.
    [Test]
    public void AnAnimationPlayedOnce_IsWrittenToPlayOnce()
    {
        var stream = new MemoryStream();

        GifEncoder.Encode(new FrameBitmap(1, 1, SurfaceFormat.R8G8B8A8.UNorm, [[1, 2, 3, 255], [4, 5, 6, 255]]) is var
            bitmap ? new Once(bitmap) : null, stream);

        Assert.That(GifImage.Read(stream.ToArray()).NumberOfReplays, Is.EqualTo(1));
    }

    private sealed class Once(IRawBitmap inner) : IRawBitmap
    {
        public uint Width => inner.Width;
        public uint Height => inner.Height;
        public SurfaceFormat PixelFormat => inner.PixelFormat;
        public bool IsMultiFrame => inner.IsMultiFrame;
        public bool HasMipLevels => false;
        public uint MipLevelsCount => 0;
        public uint NumberOfReplays => 1;
        public uint FramesCount => inner.FramesCount;
        public byte[] GetRawPixels(uint frameIndex) => inner.GetRawPixels(frameIndex);
        public FrameData GetMipLevelData(uint mipLevel) => inner.GetMipLevelData(mipLevel);
        public ImageDescription GetImageDescription() => inner.GetImageDescription();
        public FrameData GetFrameData(uint frameIndex) => inner.GetFrameData(frameIndex);
    }

    // A byte of no block kind between frames ends the file there, as browsers end it: the frames before are kept.
    [Test]
    public void ABlockOfNoKind_EndsTheFile()
    {
        var stream = new MemoryStream();
        GifEncoder.Encode(new FrameBitmap(1, 1, SurfaceFormat.R8G8B8A8.UNorm, [[1, 2, 3, 255], [4, 5, 6, 255]]), stream);
        var gif = stream.ToArray();
        gif[^1] = 0;

        Assert.That(GifImage.Read(gif).FramesCount, Is.EqualTo(2));
    }

    // LZW of the least code size, 1 - which GIF89a leaves out and files have: the width grows past 2 bits.
    [Test]
    public void LzwOfCodeSizeOne_GrowsItsWidth()
    {
        var stream = new MemoryStream();
        // Clear 2, then 1 1 1 1 1 1: codes 2 (2 bits), 1 (2 bits), 1 (3 bits) and the run, end 3.
        byte[] indices = [1, 1, 1, 1, 1, 1, 1, 1];
        GifLzw.Encode(indices, 2, stream);
        var blocks = stream.ToArray();
        var data = blocks.AsSpan(1, blocks[0]).ToArray();
        var output = new byte[8];

        Assert.That(GifLzw.Decode(data, 2, output), Is.EqualTo(8));
        Assert.That(output, Is.All.EqualTo(1));
        // Hand-packed at code size 1, as giflib reads it: clear (2) and 1 at 2 bits - the table starts at 4, already past
        // 2 bits, so the width is 3 from there - then 1, code 4 ("1 1") and the end (3) at 3 bits.
        Array.Clear(output);
        Assert.That(GifLzw.Decode([0x16, 0x0E], 1, output.AsSpan(0, 4)), Is.EqualTo(4));
        Assert.That(output.AsSpan(0, 4).ToArray(), Is.All.EqualTo(1));
    }

    // LZW through every code size, tables filled past 4096 codes and cleared, and a stream broken off.
    [TestCase(2)]
    [TestCase(5)]
    [TestCase(8)]
    public void Lzw_GivesBackWhatItPacked(int codeSize)
    {
        var random = new Random(codeSize);
        // Runs and noise: long strings and a table that fills.
        var indices = new byte[200_000];
        for (var i = 0; i < indices.Length; i++)
        {
            indices[i] = (byte)(i % 1000 < 500 ? (i / 1000) % (1 << codeSize) : random.Next(1 << codeSize));
        }

        var stream = new MemoryStream();
        GifLzw.Encode(indices, codeSize, stream);
        var blocks = stream.ToArray();
        var data = new MemoryStream();
        for (var at = 0; blocks[at] != 0; at += blocks[at] + 1)
        {
            data.Write(blocks, at + 1, blocks[at]);
        }

        var output = new byte[indices.Length];
        Assert.That(GifLzw.Decode(data.ToArray(), codeSize, output), Is.EqualTo(indices.Length));
        Assert.That(output, Is.EqualTo(indices));
        var half = new byte[indices.Length];
        var got = GifLzw.Decode(data.ToArray().AsSpan(0, (int)data.Length / 2), codeSize, half);
        Assert.That(half.AsSpan(0, got).SequenceEqual(indices.AsSpan(0, got)), Is.True);
    }

    // A screen of 65535² in a few bytes is a bomb, refused before anything is made.
    [Test]
    public void AHugeScreenInAFewBytes_IsRefused()
    {
        byte[] gif = [.. "GIF89a"u8, 0xFF, 0xFF, 0xFF, 0xFF, 0, 0, 0, 0x2C, 0, 0, 0, 0, 1, 0, 1, 0, 0, 2, 2, 0x44, 0x01, 0, 0x3B];

        Assert.That(() => GifImage.Read(gif), Throws.InstanceOf<InvalidDataException>());
    }

    // Frames that each claim the whole screen with no pixels in their data cost as little as their data, not a screen
    // apiece: only the saved canvases are made as they are played.
    [Test]
    public void FramesClaimingTheScreenWithoutData_CostNoScreenEach()
    {
        const int side = 1024, frames = 64;
        var file = new MemoryStream();
        file.Write([.. "GIF89a"u8, 0x00, 0x04, 0x00, 0x04, 0x80, 0, 0, 0, 0, 0, 255, 255, 255]);
        for (var i = 0; i < frames; i++)
        {
            // A whole-screen image whose data is clear and end, at code size 2.
            file.Write([0x2C, 0, 0, 0, 0, 0x00, 0x04, 0x00, 0x04, 0, 2, 1, 0x2C, 0]);
        }

        file.WriteByte(0x3B);
        var gif = GifImage.Read(file.ToArray());
        var canvas = new byte[side * side * 4];
        gif.CopyFrame(0, canvas);

        var before = GC.GetAllocatedBytesForCurrentThread();
        for (uint i = 1; i < frames; i++)
        {
            gif.CopyFrame(i, canvas);
        }

        var saved = frames / 16 * canvas.Length;
        Assert.That(GC.GetAllocatedBytesForCurrentThread() - before, Is.LessThan(saved + canvas.Length));
        Assert.That(canvas, Is.All.EqualTo(0));
    }

    [TestCase(new byte[] { 0x47, 0x49, 0x46, 0x38, 0x39, 0x61, 1, 0, 1, 0, 0, 0, 0, 0x3B })]
    [TestCase(new byte[] { 0x47, 0x49, 0x46, 0x38, 0x39, 0x61, 1, 0, 1, 0, 0, 0, 0, 0x99 })]
    [TestCase(new byte[] { 0x50, 0x4E, 0x47 })]
    public void AFileOfNoImage_IsRefused(byte[] gif) =>
        Assert.That(() => GifImage.Read(gif), Throws.InstanceOf<InvalidDataException>());
}
