using System;
using System.IO;
using Adamantium.Imaging;
using Adamantium.Vulkan.Core;
using NUnit.Framework;
using Image = Adamantium.Imaging.Image;

namespace Adamantium.ImagingTests;

/// <summary>A picture written to a file: one its encoder refuses leaves a file already there as it was, and a type
/// nothing writes is refused.</summary>
[TestFixture]
public class ImageSaveTests
{
    [TestCase(ImageFileType.Gif, Format.BC7_UNORM_BLOCK)]
    [TestCase(ImageFileType.Png, Format.BC7_UNORM_BLOCK)]
    [TestCase(ImageFileType.Bmp, Format.BC7_UNORM_BLOCK)]
    [TestCase(ImageFileType.Tga, Format.BC7_UNORM_BLOCK)]
    [TestCase(ImageFileType.Jpg, Format.BC7_UNORM_BLOCK)]
    [TestCase(ImageFileType.Tiff, Format.BC7_UNORM_BLOCK)]
    [TestCase(ImageFileType.Dds, Format.R64_SFLOAT)]
    public void APictureTheEncoderRefuses_LeavesTheFileThere(ImageFileType type, Format format)
    {
        using var image = Image.New2D(4, 4, format);
        var path = Path.Combine(TestContext.CurrentContext.WorkDirectory, $"refused-{type}.bin");
        File.WriteAllBytes(path, [1, 2, 3]);
        try
        {
            Assert.That(() => image.Save(path, type), Throws.InstanceOf<NotSupportedException>());
            Assert.That(() => image.ConvertToRawBitmap().Save(path, type), Throws.InstanceOf<NotSupportedException>());
            Assert.That(File.ReadAllBytes(path), Is.EqualTo(new byte[] { 1, 2, 3 }));
        }
        finally
        {
            File.Delete(path);
        }
    }

    [Test]
    public void ATypeNothingWrites_IsRefused()
    {
        using var image = Image.New2D(1, 1, Format.R8G8B8A8_UNORM);
        var path = Path.Combine(TestContext.CurrentContext.WorkDirectory, "unknown.bin");

        Assert.That(() => image.Save(new MemoryStream(), ImageFileType.Unknown), Throws.InstanceOf<NotSupportedException>());
        Assert.That(() => image.Save(path, ImageFileType.Unknown), Throws.InstanceOf<NotSupportedException>());
        Assert.That(File.Exists(path), Is.False);
    }
}
