using System;
using System.IO;
using Adamantium.Graphics.Core;
using Adamantium.Imaging;
using NUnit.Framework;

namespace Adamantium.ImagingTests;

/// <summary>Pins what the JPEG decoder produces, within a tolerance since the format is lossy; the pictures are smooth so
/// quantization alone does not fail them.</summary>
[TestFixture]
public class JpegDecodeTests
{
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
                pixels[i + 2] = 128;
                pixels[i + 3] = 255;
            }
        }
        return pixels;
    }

    private static IRawBitmap DecodeRoundTrip(uint width, uint height, out byte[] expected)
    {
        expected = Gradient(width, height);

        var image = Image.New2D(width, height, 1, SurfaceFormat.R8G8B8A8.UNorm);
        System.Runtime.InteropServices.Marshal.Copy(expected, 0, image.PixelBuffer[0].DataPointer, expected.Length);

        var stream = new MemoryStream();
        BitmapLoader.Save(image.ConvertToRawBitmap(), stream, ImageFileType.Jpg);
        return BitmapLoader.Load(new MemoryStream(stream.ToArray()));
    }

    [TestCase(16u, 16u)]
    [TestCase(64u, 64u)]
    [TestCase(97u, 41u)]   // not a multiple of 8 - the blocks do not tile the picture exactly
    public void DecodesToTheSamePictureWithinTolerance(uint width, uint height)
    {
        var decoded = DecodeRoundTrip(width, height, out var expected);

        Assert.That(decoded, Is.Not.Null, "the decoder must read back what the encoder wrote");
        Assert.That(decoded.Width, Is.EqualTo(width));
        Assert.That(decoded.Height, Is.EqualTo(height));

        var actual = decoded.GetRawPixels(0);
        var channels = (int)(actual.Length / (width * height));
        Assert.That(channels, Is.GreaterThanOrEqualTo(3), "color is expected back");

        // MEAN error, not per-pixel: JPEG's own ringing puts a few edge pixels well off, and asserting on the worst one
        // makes the test about the format rather than about the decoder. A drift in the IDCT moves the whole picture.
        double sum = 0;
        var count = 0;
        for (var i = 0; i < width * height; i++)
        {
            for (var ch = 0; ch < 3; ch++)
            {
                sum += Math.Abs(actual[i * channels + ch] - expected[i * 4 + ch]);
                count++;
            }
        }

        var mean = sum / count;
        Assert.That(mean, Is.LessThan(12.0), $"average channel error {mean:F2} - the decoder is no longer producing the picture it used to");
    }

    // Progressive scans and restart intervals reorder and split the same coefficients: each file decodes exactly as the
    // baseline one of the same picture. Progressive with restarts came out garbled or threw; tiny gray progressive ones
    // read the padding bits of a scan as data.
    [TestCase("color-progressive", "color-baseline")]
    [TestCase("color-progressive-restart", "color-baseline")]
    [TestCase("color-baseline-restart", "color-baseline")]
    [TestCase("gray-8x8-progressive", "gray-8x8-baseline")]
    [TestCase("gray-16x8-progressive", "gray-16x8-baseline")]
    public void ProgressiveAndRestartScans_DecodeAsBaseline(string name, string baseline) =>
        Assert.That(BitmapLoader.Load(TestAssets.Path($"JpegScans/{name}.jpg")).GetRawPixels(0),
            Is.EqualTo(BitmapLoader.Load(TestAssets.Path($"JpegScans/{baseline}.jpg")).GetRawPixels(0)));

    // A grayscale JPEG came out RGBA in name only: three bytes a pixel, the alpha never written.
    [Test]
    public void AGrayscaleJpeg_ComesOutWholeRgbaOpaque()
    {
        var bitmap = BitmapLoader.Load(TestAssets.Path("Gray.jpg"));
        var pixels = bitmap.GetRawPixels(0);

        Assert.That(pixels, Has.Length.EqualTo(bitmap.Width * bitmap.Height * 4));
        for (var i = 0; i < pixels.Length; i += 4)
        {
            Assert.That((pixels[i + 1], pixels[i + 2], pixels[i + 3]), Is.EqualTo((pixels[i], pixels[i], (byte)255)),
                $"pixel {i / 4}");
        }
    }
}
