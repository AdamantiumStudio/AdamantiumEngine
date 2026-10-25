using System;
using System.Buffers.Binary;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using Adamantium.Imaging;
using Adamantium.Imaging.Tiff;
using NUnit.Framework;

namespace Adamantium.ImagingTests;

/// <summary>
/// TIFF files made by ImageMagick (libtiff inside, see TestAssets/Tiff/make-assets.sh) read the same as libtiff reads
/// them: every compression, byte order, layout, depth and color model the files cover, oriented.
/// </summary>
[TestFixture]
public class TiffDecodeTests
{
    internal static string AssetsFolder() => TestAssets.Path("Tiff");

    private static IEnumerable<string> Files(string extension) =>
        Directory.GetFiles(AssetsFolder(), "*" + extension).Select(Path.GetFileNameWithoutExtension).Order();

    private static IRawBitmap Load(string name) => BitmapLoader.Load(Path.Combine(AssetsFolder(), name + ".tif"));

    private static byte[] Expected(string name, string extension) =>
        File.ReadAllBytes(Path.Combine(AssetsFolder(), name + extension));

    [TestCaseSource(nameof(EightBit))]
    public void EightBitColors_ReadAsLibtiffReadsThem(string name)
    {
        var bitmap = Load(name);
        var expected = Expected(name, ".rgba8");

        Assert.That(bitmap.PixelFormat, Is.EqualTo((SurfaceFormat)Adamantium.Vulkan.Core.Format.R8G8B8A8_UNORM));
        Assert.That(MaxDifference(bitmap.GetRawPixels(0), expected, 1),
            Is.LessThanOrEqualTo(name.StartsWith("lab") ? 3 : 1),
            "CIELab goes through floating-point color math, which ImageMagick rounds its own way");
    }

    private static IEnumerable<string> EightBit() => Files(".rgba8").Where(name => !name.StartsWith("pages-"));

    [TestCaseSource(nameof(SixteenBit))]
    public void SixteenBitColors_ReadAsLibtiffReadsThem(string name)
    {
        var bitmap = Load(name);
        var expected = Expected(name, ".rgba16");

        Assert.That(bitmap.PixelFormat, Is.EqualTo((SurfaceFormat)Adamantium.Vulkan.Core.Format.R16G16B16A16_UNORM));
        Assert.That(MaxDifference(bitmap.GetRawPixels(0), expected, 2), Is.LessThanOrEqualTo(1));
    }

    private static IEnumerable<string> SixteenBit() => Files(".rgba16");

    [TestCaseSource(nameof(Floats))]
    public void Floats_ReadAsLibtiffReadsThem(string name)
    {
        var bitmap = Load(name);
        var expected = Expected(name, ".rgbaf");
        var actual = bitmap.GetRawPixels(0);

        Assert.That(bitmap.PixelFormat, Is.EqualTo((SurfaceFormat)Adamantium.Vulkan.Core.Format.R32G32B32A32_SFLOAT));
        Assert.That(actual, Has.Length.EqualTo(expected.Length));
        var worst = 0f;
        for (var i = 0; i < actual.Length; i += 4)
        {
            worst = Math.Max(worst, Math.Abs(BinaryPrimitives.ReadSingleLittleEndian(actual.AsSpan(i))
                                             - BinaryPrimitives.ReadSingleLittleEndian(expected.AsSpan(i))));
        }

        Assert.That(worst, Is.LessThanOrEqualTo(1e-3f));
    }

    private static IEnumerable<string> Floats() => Files(".rgbaf");

    [Test]
    public void APageAfterTheFirst_IsAFrame()
    {
        var bitmap = Load("pages");

        Assert.That(bitmap.FramesCount, Is.EqualTo(3));
        for (uint page = 0; page < 3; page++)
        {
            Assert.That(MaxDifference(bitmap.GetRawPixels(page), Expected($"pages-{page}", ".rgba8"), 1), Is.Zero,
                $"page {page}");
        }
    }

    [Test]
    public void AFileOfAnotherKind_IsNotTiff() =>
        Assert.That(TiffFile.IsTiff("\x89PNG\r\n\x1A\n"u8), Is.False);

    [TestCase(new byte[] { 0x49, 0x49, 0x2A, 0x00 })]
    [TestCase(new byte[] { 0x49, 0x49, 0x2A, 0x00, 0x08, 0x00, 0x00, 0x00 })]
    [TestCase(new byte[] { 0x49, 0x49, 0x2A, 0x00, 0xFF, 0x00, 0x00, 0x00 })]
    [TestCase(new byte[] { 0x49, 0x49, 0x2A, 0x00, 0x08, 0x00, 0x00, 0x00, 0x00, 0x00, 0x08, 0x00, 0x00, 0x00 })]
    public void ABrokenFile_ThrowsInvalidData(byte[] file) =>
        Assert.That(() => TiffImage.Load(file), Throws.InstanceOf<InvalidDataException>());

    private static int MaxDifference(byte[] actual, byte[] expected, int size)
    {
        Assert.That(actual, Has.Length.EqualTo(expected.Length));
        var worst = 0;
        for (var i = 0; i < actual.Length; i += size)
        {
            var a = size == 1 ? actual[i] : BinaryPrimitives.ReadUInt16LittleEndian(actual.AsSpan(i));
            var e = size == 1 ? expected[i] : BinaryPrimitives.ReadUInt16LittleEndian(expected.AsSpan(i));
            worst = Math.Max(worst, Math.Abs(a - e));
        }

        return worst;
    }
}
