using System;
using System.IO;
using Adamantium.Graphics.Core;
using Adamantium.Imaging;
using Adamantium.Imaging.Jpeg;
using NUnit.Framework;

namespace Adamantium.ImagingTests;

/// <summary>The eighth-scale preview decode (block averages, no IDCT) against the full one: same size, same colors in the
/// same places.</summary>
[TestFixture]
public class JpegPreviewDecodeTests
{
    // Smooth, with a strong diagonal: shifted output shows up as a color error, and a plane placed wrong shows up as
    // the wrong hue rather than as noise.
    private static byte[] Picture(uint width, uint height)
    {
        var pixels = new byte[width * height * 4];
        for (var y = 0; y < height; y++)
        {
            for (var x = 0; x < width; x++)
            {
                var i = (y * width + x) * 4;
                pixels[i] = (byte)(x * 255 / Math.Max(1, width - 1));
                pixels[i + 1] = (byte)(y * 255 / Math.Max(1, height - 1));
                pixels[i + 2] = (byte)(255 - x * 255 / Math.Max(1, width - 1));
                pixels[i + 3] = 255;
            }
        }
        return pixels;
    }

    private static byte[] EncodeJpeg(uint width, uint height)
    {
        var image = Image.New2D(width, height, 1, SurfaceFormat.R8G8B8A8.UNorm);
        var pixels = Picture(width, height);
        System.Runtime.InteropServices.Marshal.Copy(pixels, 0, image.PixelBuffer[0].DataPointer, pixels.Length);

        var stream = new MemoryStream();
        BitmapLoader.Save(image.ConvertToRawBitmap(), stream, ImageFileType.Jpg);
        return stream.ToArray();
    }

    // Sizes that are not whole blocks too: the edge blocks' DC terms take in the encoder's padding, nothing else.
    [TestCase(64u, 64u)]
    [TestCase(128u, 96u)]
    [TestCase(100u, 70u)]
    [TestCase(33u, 17u)]
    public void PreviewIsAnEighthOfTheFullPicture(uint width, uint height)
    {
        var jpeg = EncodeJpeg(width, height);

        var preview = new JpegDecoder(new MemoryStream(jpeg)).DecodePreview();
        var full = new JpegDecoder(new MemoryStream(jpeg)).Decode();

        Assert.That(preview.Width, Is.EqualTo((width + 7) / 8), "an eighth of the width, rounded up");
        Assert.That(preview.Height, Is.EqualTo((height + 7) / 8), "an eighth of the height, rounded up");

        var previewPixels = preview.GetRawPixels(0);
        var fullPixels = full.GetRawPixels(0);
        var channels = (int)(fullPixels.Length / (full.Width * full.Height));

        // Each preview pixel against its block's average. Edge blocks are measured separately: their DC term includes the
        // encoder's padding, which the full decode never shows.
        double worst = 0;
        double worstEdge = 0;
        for (var by = 0u; by < preview.Height; by++)
        {
            for (var bx = 0u; bx < preview.Width; bx++)
            {
                for (var ch = 0; ch < 3; ch++)
                {
                    long sum = 0;
                    var count = 0;
                    for (var y = by * 8; y < Math.Min((by + 1) * 8, full.Height); y++)
                    {
                        for (var x = bx * 8; x < Math.Min((bx + 1) * 8, full.Width); x++)
                        {
                            sum += fullPixels[(y * full.Width + x) * channels + ch];
                            count++;
                        }
                    }

                    var expected = (double)sum / Math.Max(1, count);
                    var actual = previewPixels[(by * preview.Width + bx) * channels + ch];
                    var error = Math.Abs(actual - expected);

                    var partial = count < 64;   // this block runs off the edge of the picture
                    if (partial) worstEdge = Math.Max(worstEdge, error);
                    else worst = Math.Max(worst, error);
                }
            }
        }

        // The blocks fully inside the picture are what actually proves the traversal: a misplaced plane or an
        // off-by-one walk shows up here immediately, since a preview pixel would then carry a neighbor's color.
        Assert.That(worst, Is.LessThan(4.0), $"interior block differs by {worst:F1} from its average");

        // The encoder pads an edge block with the edge's pixels, so its DC term stays near the visible average.
        Assert.That(worstEdge, Is.LessThan(12.0), $"edge block differs by {worstEdge:F1} from its visible average");
    }
}
