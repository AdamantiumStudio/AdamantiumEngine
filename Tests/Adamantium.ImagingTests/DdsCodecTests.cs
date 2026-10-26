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

    // A header and a few bytes: the DX10 one for a resource of a dimension, else the older one of masks or a code.
    private static byte[] Header(uint width, uint height, uint depth = 0, uint flags = 0x41, uint bits = 32,
        uint red = 0xFF, uint green = 0xFF00, uint blue = 0xFF0000, uint alpha = 0xFF000000, uint caps2 = 0,
        (uint Dxgi, uint Resource, uint Misc, uint Array)? dx10 = null, int data = 32)
    {
        var file = new byte[(dx10 == null ? 128 : 148) + data];
        void Put(int at, uint value) => BitConverter.GetBytes(value).CopyTo(file, at);
        Put(0, 0x20534444);
        Put(4, 124);
        Put(12, height);
        Put(16, width);
        Put(24, depth);
        Put(80, dx10 == null ? flags : 0x4);
        Put(84, dx10 == null ? 0u : 0x30315844);
        Put(88, bits);
        Put(92, red);
        Put(96, green);
        Put(100, blue);
        Put(104, alpha);
        Put(112, caps2);
        if (dx10 is var (dxgi, resource, misc, array))
        {
            Put(128, dxgi);
            Put(132, resource);
            Put(136, misc);
            Put(140, array);
        }

        return file;
    }

    // Sizes that overflow, layers past what the file holds, a 1D picture of rows, a cube of unequal sides: refused as
    // broken, nothing their size made first.
    [Test]
    public void AHostileHeader_IsRefusedBeforeAnythingIsMade()
    {
        byte[][] files =
        [
            Header(1824726041, 631832658, dx10: (3, 3, 0, 1)),
            Header(1u << 30, 1u << 30, dx10: (2, 3, 0, 1)),
            Header(4, 4, dx10: (28, 3, 0, 100_000_000)),
            Header(4, 4, depth: 100_000_000, dx10: (28, 4, 0, 1)),
            Header(4, 4, dx10: (28, 3, 4, uint.MaxValue)),
            Header(4, 3, dx10: (28, 2, 0, 1)),
            Header(4, 2, dx10: (28, 3, 4, 1), data: 4096),
            Header(1, 1, dx10: (61, 3, 0, 4_000_000), data: 4_000_000),
            Header(1, 1, depth: 4096, caps2: 0x200000, flags: 0x20000, bits: 8, red: 0xFF, data: 4096)
        ];

        foreach (var file in files)
        {
            var before = GC.GetAllocatedBytesForCurrentThread();
            Assert.That(() => DdsImage.Read(file), Throws.InstanceOf<InvalidDataException>());
            Assert.That(GC.GetAllocatedBytesForCurrentThread() - before, Is.LessThan(1 << 20));
        }
    }

    // The older header's 24-bit color, luminance, 4-4-4-4, 3-3-2 and 4-4 luminance-alpha - formats few devices sample or
    // none has - are read as RGBA.
    [TestCase(24u, 0xFF0000u, 0xFF00u, 0xFFu, 0u, 0x41u, new byte[] { 30, 20, 10 }, new byte[] { 10, 20, 30, 255 })]
    [TestCase(8u, 0xFFu, 0u, 0u, 0u, 0x20000u, new byte[] { 77 }, new byte[] { 77, 77, 77, 255 })]
    [TestCase(16u, 0xFFu, 0u, 0u, 0xFF00u, 0x20001u, new byte[] { 77, 9 }, new byte[] { 77, 77, 77, 9 })]
    [TestCase(16u, 0xF00u, 0xF0u, 0xFu, 0xF000u, 0x41u, new byte[] { 0x21, 0x83 }, new byte[] { 51, 34, 17, 136 })]
    [TestCase(8u, 0xE0u, 0x1Cu, 0x3u, 0u, 0x40u, new byte[] { 0xE2 }, new byte[] { 255, 0, 170, 255 })]
    [TestCase(8u, 0xE0u, 0x1Cu, 0x3u, 0u, 0x40u, new byte[] { 0x89 }, new byte[] { 146, 73, 85, 255 })]
    [TestCase(16u, 0xE0u, 0x1Cu, 0x3u, 0xFF00u, 0x41u, new byte[] { 0x1C, 0x80 }, new byte[] { 0, 255, 0, 128 })]
    [TestCase(8u, 0xFu, 0u, 0u, 0xF0u, 0x20001u, new byte[] { 0x3A }, new byte[] { 170, 170, 170, 51 })]
    public void OlderFormats_AreReadAsRgba(uint bits, uint red, uint green, uint blue, uint alpha, uint flags,
        byte[] texel, byte[] expected)
    {
        var file = Header(1, 1, flags: flags, bits: bits, red: red, green: green, blue: blue, alpha: alpha,
            data: texel.Length);
        texel.CopyTo(file, 128);

        var dds = DdsImage.Read(file);

        Assert.That((Format)dds.PixelFormat, Is.EqualTo(Format.R8G8B8A8_UNORM));
        Assert.That(dds.GetRawPixels(0), Is.EqualTo(expected));
    }

    // An index looks its color up in the 256 RGBA entries after the header; with alpha, the alpha is the texel's own.
    [TestCase(0x20u, 8u, new byte[] { 2 }, new byte[] { 20, 21, 22, 23 })]
    [TestCase(0x21u, 16u, new byte[] { 2, 99 }, new byte[] { 20, 21, 22, 99 })]
    public void PalettedTexels_AreReadThroughThePalette(uint flags, uint bits, byte[] texel, byte[] expected)
    {
        var file = Header(1, 1, flags: flags, bits: bits, red: 0, green: 0, blue: 0, alpha: 0,
            data: 1024 + texel.Length);
        for (var entry = 0; entry < 256; entry++)
        {
            for (var channel = 0; channel < 4; channel++)
            {
                file[128 + entry * 4 + channel] = (byte)(entry * 10 + channel);
            }
        }

        texel.CopyTo(file, 128 + 1024);

        var dds = DdsImage.Read(file);

        Assert.That((Format)dds.PixelFormat, Is.EqualTo(Format.R8G8B8A8_UNORM));
        Assert.That(dds.GetRawPixels(0), Is.EqualTo(expected));
    }

    // DXGI's R8G8_B8G8 holds R, G, B, G and G8R8_G8B8 G, R, G, B; Direct3D 9 names channels from the high bits, so its
    // GRGB is the first and RGBG the second. Vulkan's 4:2:2 formats hold B, G, R, G and G, B, G, R. Read, a pair is in
    // Vulkan's order; written, back in the file's.
    [TestCase(68u, 0u, Format.B8G8R8G8_422_UNORM, new byte[] { 30, 20, 10, 40 })]
    [TestCase(69u, 0u, Format.G8B8G8R8_422_UNORM, new byte[] { 20, 30, 40, 10 })]
    [TestCase(0u, 0x42475247u, Format.B8G8R8G8_422_UNORM, new byte[] { 30, 20, 10, 40 })]
    [TestCase(0u, 0x47424752u, Format.G8B8G8R8_422_UNORM, new byte[] { 20, 30, 40, 10 })]
    public void A422Pair_IsReadInVulkansOrder_AndWrittenInTheFiles(uint dxgi, uint fourCC, Format format,
        byte[] expected)
    {
        byte[] pair = dxgi == 69 || fourCC == 0x47424752 ? [20, 10, 40, 30] : [10, 20, 30, 40];
        var file = dxgi != 0
            ? Header(2, 1, dx10: (dxgi, 3, 0, 1), data: 4)
            : Header(2, 1, flags: 0x4, data: 4);
        if (dxgi == 0)
        {
            BitConverter.GetBytes(fourCC).CopyTo(file, 84);
        }

        pair.CopyTo(file, file.Length - 4);

        using var image = Image.Load(file);
        var stream = new MemoryStream();
        image.Save(stream, ImageFileType.Dds);
        var written = stream.ToArray();

        Assert.That((Format)image.Description.Format, Is.EqualTo(format));
        Assert.That(image.GetPixelBuffer(0, 0, 0).GetPixels<byte>(), Is.EqualTo(expected));
        Assert.That(written[^4..], Is.EqualTo(pair));
    }

    // D3DX wrote 10-10-10-2's masks the other way round from the data, and Direct3D's loaders read them so: the masks of
    // red low are red high, and red high red low. Written, red high goes with the masks it is read by.
    [TestCase(0x3FFu, 0x3FF00000u, Format.A2R10G10B10_UNORM_PACK32)]
    [TestCase(0x3FF00000u, 0x3FFu, Format.A2B10G10R10_UNORM_PACK32)]
    public void TenBitMasks_AreReadAsD3DXWroteThem_AndWrittenSo(uint red, uint blue, Format format)
    {
        var file = Header(1, 1, flags: 0x41, bits: 32, red: red, green: 0xFFC00, blue: blue, alpha: 0xC0000000, data: 4);
        BitConverter.GetBytes(0xC00FFC01u).CopyTo(file, 128);

        using var image = Image.Load(file);
        var stream = new MemoryStream();
        image.Save(stream, ImageFileType.Dds);
        using var back = Image.Load(stream.ToArray());

        Assert.That((Format)image.Description.Format, Is.EqualTo(format));
        Assert.That((Format)back.Description.Format, Is.EqualTo(format));
        Assert.That(back.GetPixelBuffer(0, 0, 0).GetPixels<byte>(), Is.EqualTo(BitConverter.GetBytes(0xC00FFC01u)));
    }

    // Refused, the image leaves a file already there as it was.
    [Test]
    public void MoreLayersThanDirect3DHas_AreNotWritten_AndTheFileThereIsKept()
    {
        using var image = Image.New2D(1, 1, Format.R8G8B8A8_UNORM, 2049);
        var path = Path.Combine(TestContext.CurrentContext.WorkDirectory, "many-layers.dds");
        try
        {
            File.WriteAllBytes(path, [1, 2, 3]);

            Assert.That(() => image.Save(new MemoryStream(), ImageFileType.Dds), Throws.InstanceOf<NotSupportedException>());
            Assert.That(() => image.Save(path, ImageFileType.Dds), Throws.InstanceOf<NotSupportedException>());
            Assert.That(File.ReadAllBytes(path), Is.EqualTo(new byte[] { 1, 2, 3 }));
        }
        finally
        {
            File.Delete(path);
        }
    }

    [Test]
    public void ACubeOfUnequalSides_IsNotMade() =>
        Assert.That(() => Image.New(new ImageDescription
        {
            Dimension = TextureDimension.TextureCube, Width = 4, Height = 2, Depth = 1, ArraySize = 6, MipLevels = 1,
            Format = Format.R8G8B8A8_UNORM
        }), Throws.InvalidOperationException);

    [Test]
    public void ACompressedDescription_CountsWholeBlocks()
    {
        var description = ImageDescription.Default2D(5, 3, Format.BC1_RGBA_UNORM_BLOCK);

        Assert.That((description.RowStride, description.TotalSizeInBytes), Is.EqualTo((16L, 16L)));
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
