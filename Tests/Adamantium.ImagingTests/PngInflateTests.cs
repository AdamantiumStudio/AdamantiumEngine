using System;
using System.IO;
using Adamantium.Imaging;
using Adamantium.Imaging.Png;
using NUnit.Framework;
using CompressionLevel = System.IO.Compression.CompressionLevel;
using static Adamantium.ImagingTests.TestPng;

namespace Adamantium.ImagingTests;

/// <summary>
/// The PNG decoder against streams it did not write itself: zlib at every level (stored, fixed and dynamic blocks), a
/// truncated stream, and a low-bit-depth image unfiltered in place.
/// </summary>
[TestFixture]
public class PngInflateTests
{
    [TestCase(CompressionLevel.NoCompression)]
    [TestCase(CompressionLevel.Fastest)]
    [TestCase(CompressionLevel.Optimal)]
    [TestCase(CompressionLevel.SmallestSize)]
    public void ZlibAtAnyLevel_DecodesToTheSamePixels(CompressionLevel level)
    {
        const int width = 300, height = 200;
        var pixels = Gradient(width, height);

        var raw = new byte[height * (1 + width * 4)];
        for (var y = 0; y < height; y++)
        {
            Array.Copy(pixels, y * width * 4, raw, y * (1 + width * 4) + 1, width * 4);
        }

        var png = Build(width, height, 8, 6, Zlib(raw, level));
        var decoded = BitmapLoader.Load(new MemoryStream(png)).GetRawPixels(0);

        Assert.That(decoded, Is.EqualTo(pixels));
    }

    [Test, Timeout(10000)]
    public void TruncatedStream_ThrowsInsteadOfHanging()
    {
        const int width = 64, height = 64;
        var raw = new byte[height * (1 + width * 4)];
        new Random(1).NextBytes(raw);
        for (var y = 0; y < height; y++)
        {
            raw[y * (1 + width * 4)] = 0;
        }

        var zlib = Zlib(raw, CompressionLevel.Optimal);
        var png = Build(width, height, 8, 6, zlib[..(zlib.Length / 2)]);

        Assert.Throws<InvalidDataException>(() => BitmapLoader.Load(new MemoryStream(png)).GetRawPixels(0));
    }

    // 1-bit gray at a width that is not a multiple of 8 is unfiltered in place, every filter type in turn.
    [Test]
    public void OneBitGray_WithPaddingBits_DecodesEveryFilter()
    {
        const int width = 509, height = 10;
        const int lineBytes = (width + 7) / 8;
        var random = new Random(7);

        var lines = new byte[height][];
        var filtered = new byte[height * (1 + lineBytes)];
        for (var y = 0; y < height; y++)
        {
            lines[y] = new byte[lineBytes];
            random.NextBytes(lines[y]);
            var previous = y > 0 ? lines[y - 1] : new byte[lineBytes];
            var type = (byte)(y % 5);
            filtered[y * (1 + lineBytes)] = type;
            for (var x = 0; x < lineBytes; x++)
            {
                int left = x > 0 ? lines[y][x - 1] : 0;
                int up = previous[x];
                int upLeft = x > 0 ? previous[x - 1] : 0;
                var predicted = type switch
                {
                    1 => left,
                    2 => up,
                    3 => (left + up) >> 1,
                    4 => Paeth(left, up, upLeft),
                    _ => 0
                };
                filtered[y * (1 + lineBytes) + 1 + x] = (byte)(lines[y][x] - predicted);
            }
        }

        var png = Build(width, height, 1, 0, Zlib(filtered, CompressionLevel.Optimal));
        var decoded = BitmapLoader.Load(new MemoryStream(png)).GetRawPixels(0);

        for (var y = 0; y < height; y++)
        {
            for (var x = 0; x < width; x++)
            {
                var bit = (lines[y][x >> 3] >> (7 - (x & 7))) & 1;
                var expected = bit == 1 ? 255 : 0;
                var at = (y * width + x) * 4;
                if (decoded[at] != expected || decoded[at + 3] != 255)
                {
                    Assert.Fail($"pixel ({x}, {y}): expected {expected}, got {decoded[at]} alpha {decoded[at + 3]}");
                }
            }
        }
    }

    private static int Paeth(int a, int b, int c)
    {
        var p = a + b - c;
        int pa = Math.Abs(p - a), pb = Math.Abs(p - b), pc = Math.Abs(p - c);
        return pa <= pb && pa <= pc ? a : pb <= pc ? b : c;
    }

    private static byte[] Gradient(int width, int height)
    {
        var pixels = new byte[width * height * 4];
        for (var y = 0; y < height; y++)
        {
            for (var x = 0; x < width; x++)
            {
                var i = (y * width + x) * 4;
                pixels[i] = (byte)(x * 255 / (width - 1));
                pixels[i + 1] = (byte)(y * 255 / (height - 1));
                pixels[i + 2] = (byte)((x ^ y) & 0xFF);
                pixels[i + 3] = (byte)(255 - (x & 0x3F));
            }
        }

        return pixels;
    }

}
