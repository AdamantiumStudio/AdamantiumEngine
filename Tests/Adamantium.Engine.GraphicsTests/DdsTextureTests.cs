using System;
using System.IO;
using System.Linq;
using System.Runtime.InteropServices;
using Adamantium.Graphics;
using Adamantium.Imaging;
using Adamantium.Vulkan.Core;
using NUnit.Framework;
using Image = Adamantium.Imaging.Image;
using TextureDimension = Adamantium.Imaging.TextureDimension;

namespace Adamantium.Engine.GraphicsTests;

/// <summary>A DDS of several parts goes up whole: every face of a cube at every level where it belongs, cubes of real
/// files as the file has them, an array of cubes as one - or refused where the device makes none - every layer of an
/// array.</summary>
[TestFixture]
public class DdsTextureTests
{
    private const ImageUsageFlagBits ReadBack = ImageUsageFlagBits.SampledBit | ImageUsageFlagBits.TransferSrcBit;

    private static GraphicsDevice Device => (GraphicsDevice)GpuFixture.Main.ResourceLoaderDevice;

    private static string Asset(string name)
    {
        for (var dir = new DirectoryInfo(AppContext.BaseDirectory); dir != null; dir = dir.Parent)
        {
            var candidate = Path.Combine(dir.FullName, "Tests", "TestAssets", name);
            if (File.Exists(candidate)) return candidate;
        }

        throw new FileNotFoundException(name);
    }

    // Each face of cube-rgba.dds is filled with its layer times 16 plus its level times 4.
    [Test]
    public void EveryFaceOfACube_AtEveryLevel_GoesUpWhereItBelongs()
    {
        using var cube = Texture.Load(Device, Asset("dds/cube-rgba.dds"), ReadBack);

        Assert.That(cube.Description.MipLevels, Is.EqualTo(2));
        for (uint face = 0; face < 6; face++)
        {
            for (uint level = 0; level < cube.Description.MipLevels; level++)
            {
                Assert.That(Bytes(cube, face, level).Distinct(), Is.EqualTo(new[] { (byte)(face * 16 + level * 4) }),
                    $"face {face}, level {level}");
            }
        }
    }

    [TestCase("TextureCube.dds")]
    [TestCase("CubeTest.dds")]
    public void ACubeOfARealFile_GoesUpAsTheFileHasIt(string name)
    {
        using var file = Image.Load(Asset(name));
        using var cube = Texture.Load(Device, Asset(name), ReadBack);

        Assert.That(file.Description.Dimension, Is.EqualTo(TextureDimension.TextureCube));
        for (var face = 0; face < 6; face++)
        {
            Assert.That(Bytes(cube, (uint)face, 0), Is.EqualTo(file.GetPixelBuffer(face, 0, 0).GetPixels<byte>()),
                $"face {face}");
        }
    }

    [Test]
    public void AnArrayOfCubes_GoesUpAsOne_OrIsRefusedWhereTheDeviceMakesNone()
    {
        using var cubes = Image.New(new ImageDescription
        {
            Dimension = TextureDimension.TextureCube, Width = 4, Height = 4, Depth = 1, ArraySize = 12,
            MipLevels = 1, Format = Format.R8G8B8A8_UNORM
        });
        for (var layer = 0; layer < 12; layer++)
        {
            var buffer = cubes.GetPixelBuffer(layer, 0, 0);
            var bytes = Enumerable.Repeat((byte)(layer * 16), buffer.BufferStride).ToArray();
            Marshal.Copy(bytes, 0, buffer.DataPointer, bytes.Length);
        }

        var stream = new MemoryStream();
        cubes.Save(stream, ImageFileType.Dds);

        if (!Device.Adapter.SupportsImageCubeArray)
        {
            Assert.That(() => Texture.Load(Device, new MemoryStream(stream.ToArray()), ReadBack),
                Throws.InstanceOf<NotSupportedException>());
            return;
        }

        using var texture = Texture.Load(Device, new MemoryStream(stream.ToArray()), ReadBack);
        Assert.That(texture.Description.ArrayLayers, Is.EqualTo(12));
        for (uint layer = 0; layer < 12; layer++)
        {
            Assert.That(Bytes(texture, layer, 0).Distinct(), Is.EqualTo(new[] { (byte)(layer * 16) }), $"layer {layer}");
        }
    }

    [Test]
    public void ALayeredDds_MakesATexture()
    {
        Assert.That(() => Texture.Load(Device, Asset("dds/array-bc7.dds")).Dispose(), Throws.Nothing);
        Assert.That(() => Texture.Load(Device, Asset("dds/volume-r8.dds")).Dispose(), Throws.Nothing);
    }

    private static byte[] Bytes(Texture texture, uint layer, uint level)
    {
        using var read = texture.ReadbackToImage(layer, level);
        var bytes = new byte[read.TotalSizeInBytes];
        Marshal.Copy(read.DataPointer, bytes, 0, bytes.Length);
        return bytes;
    }
}
