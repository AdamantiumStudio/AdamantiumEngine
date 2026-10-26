using System;
using System.IO;
using System.Linq;
using Adamantium.Imaging;
using Adamantium.Imaging.Dds;
using NUnit.Framework;
using Format = Adamantium.Vulkan.Core.Format;
using Image = Adamantium.Imaging.Image;

namespace Adamantium.ImagingTests;

/// <summary>
/// The DDS codec against Pillow - masked pixels read as it reads them, compressed blocks kept as the file has them -
/// and against files laid out by hand as the DDS guide says, every part filled with its own number: mip chains, a
/// cube, an array in a DX10 header, a volume. Written and read back, every part stays where it was.
/// </summary>
[TestFixture]
public class DdsCodecTests
{
    private static string Asset(string name) => TestAssets.Path($"dds/{name}");

    [TestCase("rgba")]
    [TestCase("rgb")]
    [TestCase("gray")]
    public void MaskedPixels_ReadAsPillowReadsThem(string name) =>
        Assert.That(DdsImage.Read(File.ReadAllBytes(Asset(name + ".dds"))).GetRgba8Pixels(),
            Is.EqualTo(File.ReadAllBytes(Asset(name + ".rgba"))));

    [TestCase("dxt1", Format.BC1_RGBA_UNORM_BLOCK, 128)]
    [TestCase("dxt5", Format.BC3_UNORM_BLOCK, 128)]
    [TestCase("bc3-dx10", Format.BC3_UNORM_BLOCK, 148)]
    public void CompressedBlocks_AreKeptAsTheFileHasThem(string name, Format format, int header)
    {
        var file = File.ReadAllBytes(Asset(name + ".dds"));

        var dds = DdsImage.Read(file);

        Assert.That((Format)dds.PixelFormat, Is.EqualTo(format));
        Assert.That(dds.GetRawPixels(0), Is.EqualTo(file.AsSpan(header).ToArray()));
    }

    // Each part holds layer * 16 + level * 4 + slice; read into an image, each part is where it was.
    [TestCase("mips-dxt1", TextureDimension.Texture2D, 64u, 32u, 1u, 7u, 1u)]
    [TestCase("cube-rgba", TextureDimension.TextureCube, 8u, 8u, 1u, 2u, 6u)]
    [TestCase("array-bc7", TextureDimension.Texture2D, 16u, 8u, 1u, 2u, 3u)]
    [TestCase("volume-r8", TextureDimension.Texture3D, 4u, 4u, 4u, 3u, 1u)]
    public void EveryPart_IsReadWhereItIs(string name, TextureDimension dimension, uint width, uint height, uint depth,
        uint levels, uint layers)
    {
        using var image = Image.Load(Asset(name + ".dds"));

        Assert.That((image.Description.Dimension, image.Description.Width, image.Description.Height,
            image.Description.Depth, image.Description.MipLevels, image.Description.ArraySize),
            Is.EqualTo((dimension, width, height, depth, levels, layers)));
        AssertParts(image);
    }

    // An image of every kind written and read back keeps every part, its header the older one or DX10 as it needs.
    [TestCase("mips-dxt1")]
    [TestCase("cube-rgba")]
    [TestCase("array-bc7")]
    [TestCase("volume-r8")]
    public void EveryPart_IsWrittenAndReadBack(string name)
    {
        using var image = Image.Load(Asset(name + ".dds"));
        var stream = new MemoryStream();

        image.Save(stream, ImageFileType.Dds);
        using var read = Image.Load(stream.ToArray());

        Assert.That(read.Description, Is.EqualTo(image.Description));
        AssertParts(read);
    }

    [Test]
    public void ABrokenFile_IsRefused()
    {
        var whole = File.ReadAllBytes(Asset("mips-dxt1.dds"));
        var unknown = (byte[])whole.Clone();
        "ZZZZ"u8.CopyTo(unknown.AsSpan(84));

        Assert.Multiple(() =>
        {
            Assert.That(() => DdsImage.Read(whole.AsSpan(0, whole.Length - 1)), Throws.InstanceOf<InvalidDataException>());
            Assert.That(() => DdsImage.Read(whole.AsSpan(0, 100)), Throws.InstanceOf<InvalidDataException>());
            Assert.That(() => DdsImage.Read(unknown), Throws.InstanceOf<NotSupportedException>());
        });
    }

    private static void AssertParts(Image image)
    {
        var layers = image.Description.Dimension == TextureDimension.Texture3D ? 1 : (int)image.Description.ArraySize;
        for (var layer = 0; layer < layers; layer++)
        {
            for (var level = 0; level < image.Description.MipLevels; level++)
            {
                var slices = image.Description.Dimension == TextureDimension.Texture3D
                    ? Math.Max(1, (int)image.Description.Depth >> level)
                    : 1;
                for (var slice = 0; slice < slices; slice++)
                {
                    var bytes = image.GetPixelBuffer(layer, slice, level).GetPixels<byte>();
                    Assert.That(bytes.Distinct(), Is.EqualTo(new[] { (byte)(layer * 16 + level * 4 + slice) }),
                        $"layer {layer}, level {level}, slice {slice}");
                }
            }
        }
    }
}
