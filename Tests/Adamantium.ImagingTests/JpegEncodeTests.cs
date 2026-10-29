using System;
using System.IO;
using Adamantium.Imaging;
using Adamantium.Imaging.Jpeg;
using NUnit.Framework;

namespace Adamantium.ImagingTests;

/// <summary>A color picture written with its chroma shared by two or four pixels says so in its frame header, reads back
/// as the picture it was, and takes less room than one with chroma for every pixel.</summary>
[TestFixture]
public class JpegEncodeTests
{
    private static IRawBitmap Picture(uint width, uint height)
    {
        var pixels = new byte[width * height * 4];
        for (var y = 0; y < height; y++)
        {
            for (var x = 0; x < width; x++)
            {
                var i = (y * width + x) * 4;
                pixels[i] = (byte)(x * 255 / Math.Max(1, width - 1));
                pixels[i + 1] = (byte)(y * 255 / Math.Max(1, height - 1));
                pixels[i + 2] = (byte)(128 + 100 * Math.Sin(x * 0.2) * Math.Cos(y * 0.15));
                pixels[i + 3] = 255;
            }
        }

        var image = Image.New2D(width, height, 1, SurfaceFormat.R8G8B8A8.UNorm);
        System.Runtime.InteropServices.Marshal.Copy(pixels, 0, image.PixelBuffer[0].DataPointer, pixels.Length);
        return image.ConvertToRawBitmap();
    }

    private static byte[] Encode(IRawBitmap picture, JpegSubsampling subsampling)
    {
        using var stream = new MemoryStream();
        JpegEncoder.Encode(picture, stream, 90, subsampling);
        return stream.ToArray();
    }

    // The luma component's sampling factors in the frame header (B.2.2): across in the high nibble, down in the low.
    private static int LumaFactors(byte[] file)
    {
        for (var i = 2; i + 11 < file.Length; i++)
        {
            if (file[i] == 0xFF && file[i + 1] == 0xC0)
            {
                return file[i + 11];
            }
        }

        throw new InvalidDataException("No baseline frame header.");
    }

    [TestCase(JpegSubsampling.None, 0x11)]
    [TestCase(JpegSubsampling.HalfWidth, 0x21)]
    [TestCase(JpegSubsampling.Half, 0x22)]
    public void TheFrameSaysHowTheChromaIsShared(JpegSubsampling subsampling, int factors)
    {
        Assert.That(LumaFactors(Encode(Picture(64, 64), subsampling)), Is.EqualTo(factors));
    }

    [TestCase(JpegSubsampling.HalfWidth, 64u, 64u)]
    [TestCase(JpegSubsampling.Half, 64u, 64u)]
    [TestCase(JpegSubsampling.Half, 97u, 41u)]   // not whole MCUs: the edge pixels are repeated to fill them
    [TestCase(JpegSubsampling.HalfWidth, 1u, 1u)]
    public void ReadsBackAsThePicture(JpegSubsampling subsampling, uint width, uint height)
    {
        var picture = Picture(width, height);
        var expected = picture.GetRawPixels(0);

        var decoded = new JpegDecoder(new MemoryStream(Encode(picture, subsampling))).Decode();

        Assert.That((decoded.Width, decoded.Height), Is.EqualTo((width, height)));
        var actual = decoded.GetRawPixels(0);
        double sum = 0;
        for (var i = 0; i < width * height; i++)
        {
            for (var channel = 0; channel < 3; channel++)
            {
                sum += Math.Abs(actual[i * 4 + channel] - expected[i * 4 + channel]);
            }
        }

        Assert.That(sum / (width * height * 3), Is.LessThan(4.0));
    }

    [Test]
    public void SharedChroma_TakesLessRoom()
    {
        var picture = Picture(256, 256);

        var full = Encode(picture, JpegSubsampling.None).Length;
        var half = Encode(picture, JpegSubsampling.Half).Length;

        Assert.That(half, Is.LessThan(full));
    }
}
