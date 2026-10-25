using System;
using System.IO;
using Adamantium.Graphics.Core;
using Adamantium.Imaging;
using NUnit.Framework;

namespace Adamantium.ImagingTests;

/// <summary>
/// The PNG codec's own round trip: decode what we encoded, and get the same picture back. Found while wiring pictures
/// into drag-drop, where the encoder threw on an image its own decoder had just produced.
/// </summary>
[TestFixture]
public class PngRoundTripTests
{
    // A picture with real variety, so filtering and compression have something to chew on rather than a flat color
    // that hides mistakes.
    private static byte[] Gradient(uint width, uint height)
    {
        var pixels = new byte[width * height * 4];
        for (var y = 0; y < height; y++)
        {
            for (var x = 0; x < width; x++)
            {
                var i = (y * width + x) * 4;
                pixels[i] = (byte)(x * 255 / Math.Max(1, width - 1));
                pixels[i + 1] = (byte)(y * 255 / Math.Max(1, height - 1));
                pixels[i + 2] = (byte)((x + y) & 0xFF);
                pixels[i + 3] = 255;
            }
        }
        return pixels;
    }

    private static IRawBitmap Encode(uint width, uint height, out byte[] png)
    {
        var pixels = Gradient(width, height);
        var image = Image.New2D(width, height, 1, SurfaceFormat.R8G8B8A8.UNorm);
        var buffer = image.PixelBuffer[0];
        System.Runtime.InteropServices.Marshal.Copy(pixels, 0, buffer.DataPointer, pixels.Length);

        var stream = new MemoryStream();
        BitmapLoader.Save(image.ConvertToRawBitmap(), stream, ImageFileType.Png);
        png = stream.ToArray();
        return BitmapLoader.Load(new MemoryStream(png));
    }

    // A tIME chunk of the current time went into every file: the same picture saved a second apart differed, and what
    // compared files to skip rewriting them rewrote them.
    [Test]
    public void ThePictureEncodedAgain_IsTheSameBytes_WithNoTimeInIt()
    {
        Encode(16, 16, out var first);
        Encode(16, 16, out var second);

        Assert.That(System.Text.Encoding.ASCII.GetString(first), Does.Not.Contain("tIME"));
        Assert.That(second, Is.EqualTo(first));
    }

    [TestCase(2u, 2u)]
    [TestCase(64u, 64u)]
    [TestCase(257u, 129u)]   // not a multiple of anything - catches stride assumptions
    public void EncodeThenDecode_GivesTheSamePictureBack(uint width, uint height)
    {
        var decoded = Encode(width, height, out var png);

        Assert.That(png, Is.Not.Empty, "the encoder must produce something");
        Assert.That(decoded, Is.Not.Null, "and our own decoder must read it back");
        Assert.That(decoded.Width, Is.EqualTo(width));
        Assert.That(decoded.Height, Is.EqualTo(height));

        var expected = Gradient(width, height);
        var actual = decoded.GetRawPixels(0);
        Assert.That(actual.Length, Is.GreaterThanOrEqualTo(expected.Length));
        for (var i = 0; i < expected.Length; i++)
        {
            if (actual[i] != expected[i]) Assert.Fail($"pixel byte {i}: expected {expected[i]}, got {actual[i]}");
        }
    }

    // The decoded buffer must describe the PIXELS, not the IDAT block it came out of: the decoder sizes it with the
    // per-scanline filter byte included, so every consumer that computes width*height*bpp sees a buffer that is
    // `height` bytes too long and has to guess whether the extra bytes are leading, trailing or interleaved.
    [Test]
    public void DecodedBuffer_IsExactlyThePixels()
    {
        var decoded = Encode(64, 64, out _);

        Assert.That(decoded.GetRawPixels(0).Length, Is.EqualTo(64 * 64 * 4));
    }

    // Re-encoding a picture that came out of our own decoder must work: that is the whole transcoding path (a JPEG or a
    // GIF arriving on the clipboard and leaving as PNG), and it threw IndexOutOfRangeException.
    [Test]
    public void ReEncodingADecodedImage_DoesNotThrow()
    {
        var decoded = Encode(257, 129, out _);

        var again = new MemoryStream();
        Assert.DoesNotThrow(() => BitmapLoader.Save(decoded, again, ImageFileType.Png));
        Assert.That(again.Length, Is.GreaterThan(0));
    }
}
