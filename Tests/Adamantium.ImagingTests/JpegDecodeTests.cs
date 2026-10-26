using System;
using System.IO;
using Adamantium.Graphics.Core;
using Adamantium.Imaging;
using Adamantium.Imaging.Jpeg;
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

    // Pillow writes T.81's K.3 tables when it does not optimize: the stream without its DHT segments decodes the same,
    // by the tables the decoder falls back on.
    [Test]
    public void AStreamWithoutHuffmanTables_IsReadByTheStandardOnes()
    {
        var file = System.IO.File.ReadAllBytes(TestAssets.Path("JpegScans/color-standard-tables.jpg"));
        var stripped = new System.Collections.Generic.List<byte>();
        for (var at = 0; at < file.Length;)
        {
            if (file[at] == 0xFF && file[at + 1] == 0xC4)
            {
                at += 2 + (file[at + 2] << 8 | file[at + 3]);
                continue;
            }

            if (file[at] == 0xFF && file[at + 1] == 0xDA)
            {
                stripped.AddRange(file.AsSpan(at).ToArray());
                break;
            }

            stripped.Add(file[at++]);
        }

        Assert.That(new JpegDecoder(new MemoryStream(stripped.ToArray())).Decode().GetRawPixels(0),
            Is.EqualTo(new JpegDecoder(new MemoryStream(file)).Decode().GetRawPixels(0)));
    }

    // Adobe's CMYK keeps its inks inverted: the quarters of pure cyan, magenta, yellow and black come out as such.
    [Test]
    public void AnAdobeCmykJpeg_ComesOutInItsColors()
    {
        var bitmap = BitmapLoader.Load(TestAssets.Path("JpegScans/cmyk-adobe.jpg"));
        var pixels = bitmap.GetRawPixels(0);

        (int X, int Y, byte R, byte G, byte B)[] quarters =
            [(3, 3, 0, 255, 255), (12, 3, 255, 0, 255), (3, 12, 255, 255, 0), (12, 12, 0, 0, 0)];
        foreach (var (x, y, r, g, b) in quarters)
        {
            var at = (y * 16 + x) * 4;
            Assert.That(new[] { pixels[at], pixels[at + 1], pixels[at + 2] },
                Is.EqualTo(new[] { r, g, b }).Within(8), $"the quarter at ({x}, {y})");
        }
    }

    // The preview is the DC terms alone: a progressive file's first scans hold the same ones as the baseline file of the
    // picture, so the previews are equal - with the AC scans never decoded.
    [Test]
    public void ThePreviewOfAProgressiveFile_IsThatOfItsBaseline() =>
        Assert.That(new JpegDecoder(System.IO.File.OpenRead(TestAssets.Path("JpegScans/color-progressive.jpg")))
                .DecodePreview().GetRawPixels(0),
            Is.EqualTo(new JpegDecoder(System.IO.File.OpenRead(TestAssets.Path("JpegScans/color-baseline.jpg")))
                .DecodePreview().GetRawPixels(0)));

    // Broken and hostile streams are refused as the decoder says it refuses them, at once: a Huffman table of more
    // codes than its lengths hold, components whose factors do not divide the largest, a gigapixel frame in 2 KB.
    [TestCase("overfull-huffman", typeof(InvalidDataException))]
    [TestCase("factors-not-dividing", typeof(NotSupportedException))]
    [TestCase("gigapixel-in-2k", typeof(InvalidDataException))]
    public void AHostileStream_IsRefusedAtOnce(string name, Type refusal)
    {
        var watch = System.Diagnostics.Stopwatch.StartNew();

        Assert.That(() => new JpegDecoder(System.IO.File.OpenRead(TestAssets.Path($"JpegHostile/{name}.jpg"))).Decode(),
            Throws.TypeOf(refusal));
        Assert.That(watch.ElapsedMilliseconds, Is.LessThan(2000));
    }

    [Test]
    public void TheSize_IsReadFromTheFrameHeaderAlone() =>
        Assert.That(new JpegDecoder(System.IO.File.OpenRead(TestAssets.Path("JpegScans/color-baseline.jpg"))).Size,
            Is.EqualTo((33u, 31u)));

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
