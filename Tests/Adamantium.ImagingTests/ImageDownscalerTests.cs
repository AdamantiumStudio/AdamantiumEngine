using System;
using Adamantium.Imaging;
using Adamantium.Vulkan.Core;
using NUnit.Framework;

namespace Adamantium.ImagingTests;

/// <summary>Pixels shrunk to fit: each the average of the area it covers, colors by their alpha, the aspect kept.</summary>
[TestFixture]
public class ImageDownscalerTests
{
    [Test]
    public void PixelsThatFit_AreThemselves()
    {
        var pixels = new byte[4 * 3 * 4];

        Assert.That(ImageDownscaler.FitWithin(pixels, 4, 3, Format.R8G8B8A8_UNORM, 4, 3, out var w, out var h),
            Is.SameAs(pixels));
        Assert.That((w, h), Is.EqualTo((4u, 3u)));
    }

    // 4×2 to 2×1: each new pixel the four under it; a transparent one lends no color.
    [Test]
    public void APixel_IsTheAverageOfItsArea_ColorsByAlpha()
    {
        byte[] pixels =
        [
            200, 0, 0, 255, 0, 0, 0, 0, 10, 20, 30, 255, 30, 40, 50, 255,
            100, 0, 0, 255, 0, 0, 0, 0, 50, 60, 70, 255, 70, 80, 90, 255
        ];

        var shrunk = ImageDownscaler.FitWithin(pixels, 4, 2, Format.R8G8B8A8_UNORM, 2, 2, out var w, out var h);

        Assert.That((w, h), Is.EqualTo((2u, 1u)));
        Assert.That(shrunk, Is.EqualTo(new byte[] { 150, 0, 0, 128, 40, 50, 60, 255 }));
    }

    // 3 to 2 a side: a pixel's area takes a pixel and a half, the middle one shared.
    [Test]
    public void APartlyCoveredPixel_CountsByHowMuchOfItIsCovered()
    {
        byte[] pixels = [0, 90, 180];

        var shrunk = ImageDownscaler.FitWithin(pixels, 3, 1, Format.R8_UNORM, 2, 1, out var w, out _);

        Assert.That(w, Is.EqualTo(2));
        Assert.That(shrunk, Is.EqualTo(new byte[] { 30, 150 }));
    }

    [Test]
    public void SixteenBitPixels_AreShrunkAtSixteenBits()
    {
        byte[] pixels = [0x00, 0x10, 0x00, 0x20, 0x00, 0x30, 0xFF, 0xFF, 0x00, 0x30, 0x00, 0x40, 0x00, 0x50, 0xFF, 0xFF];

        var shrunk = ImageDownscaler.FitWithin(pixels, 2, 1, Format.R16G16B16A16_UNORM, 1, 1, out _, out _);

        Assert.That(shrunk, Is.EqualTo(new byte[] { 0x00, 0x20, 0x00, 0x30, 0x00, 0x40, 0xFF, 0xFF }));
    }

    // A wide picture keeps its aspect: 1000×10 within 100×100 is 100×1.
    [Test]
    public void TheAspect_IsKept()
    {
        var pixels = new byte[1000 * 10 * 3];
        new Random(1).NextBytes(pixels);

        var shrunk = ImageDownscaler.FitWithin(pixels, 1000, 10, Format.B8G8R8_UNORM, 100, 100, out var w, out var h);

        Assert.That((w, h), Is.EqualTo((100u, 1u)));
        Assert.That(shrunk, Has.Length.EqualTo(100 * 3));
    }

    // Black and white sRGB pixels average to half the light, which sRGB writes as 188 - not the 128 of their codes.
    [Test]
    public void SrgbPixels_AreAveragedAsLight()
    {
        byte[] pixels = [0, 0, 0, 255, 255, 255, 255, 255];

        var shrunk = ImageDownscaler.FitWithin(pixels, 2, 1, Format.R8G8B8A8_SRGB, 1, 1, out _, out _);

        Assert.That(shrunk, Is.EqualTo(new byte[] { 188, 188, 188, 255 }));
    }

    // Shrunk in bands on several cores, a tall picture is what one pass over it gives: every pixel the average of its
    // area, the rows where bands meet included.
    [TestCase(1000u, 433u)]
    [TestCase(4097u, 4096u)]
    public void ATallPicture_IsShrunkWhole(uint height, uint fitted)
    {
        const uint width = 7;
        var pixels = new byte[width * height];
        new Random((int)height).NextBytes(pixels);

        var shrunk = ImageDownscaler.FitWithin(pixels, width, height, Format.R8_UNORM, width, fitted, out var w, out var h);

        var scaleX = (double)width / w;
        var scaleY = (double)height / h;
        for (var ty = 0; ty < h; ty++)
        {
            for (var tx = 0; tx < w; tx++)
            {
                var sum = 0.0;
                for (var y = (int)(ty * scaleY); y < Math.Min(height, Math.Ceiling((ty + 1) * scaleY)); y++)
                {
                    var dy = Math.Min(y + 1, (ty + 1) * scaleY) - Math.Max(y, ty * scaleY);
                    for (var x = (int)(tx * scaleX); x < Math.Min(width, Math.Ceiling((tx + 1) * scaleX)); x++)
                    {
                        var dx = Math.Min(x + 1, (tx + 1) * scaleX) - Math.Max(x, tx * scaleX);
                        sum += pixels[y * width + x] * dx * dy;
                    }
                }

                Assert.That(shrunk[ty * w + tx], Is.EqualTo(sum / (scaleX * scaleY)).Within(1), $"pixel {tx}, {ty}");
            }
        }
    }

    [Test]
    public void APictureOfNoPixels_IsItself()
    {
        var pixels = Array.Empty<byte>();

        Assert.That(ImageDownscaler.FitWithin(pixels, 0, 9000, Format.R8_UNORM, 100, 100, out var w, out var h),
            Is.SameAs(pixels));
        Assert.That((w, h), Is.EqualTo((0u, 9000u)));
    }

    [Test]
    public void ACompressedFormat_IsNotShrunk()
    {
        Assert.That(ImageDownscaler.Supports(Format.BC1_RGBA_UNORM_BLOCK), Is.False);
        Assert.That(() => ImageDownscaler.FitWithin(new byte[8], 8, 8, Format.BC1_RGBA_UNORM_BLOCK, 4, 4, out _, out _),
            Throws.InstanceOf<NotSupportedException>());
    }
}
