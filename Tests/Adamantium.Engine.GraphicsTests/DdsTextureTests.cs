using System;
using System.IO;
using System.Linq;
using System.Runtime.InteropServices;
using Adamantium.Graphics;
using NUnit.Framework;

namespace Adamantium.Engine.GraphicsTests;

/// <summary>A DDS of several parts goes up whole: every face of a cube where it belongs, every layer of an array.
/// </summary>
[TestFixture]
public class DdsTextureTests
{
    private static string Asset(string name)
    {
        for (var dir = new DirectoryInfo(AppContext.BaseDirectory); dir != null; dir = dir.Parent)
        {
            var candidate = Path.Combine(dir.FullName, "Tests", "TestAssets", "dds", name);
            if (File.Exists(candidate)) return candidate;
        }

        throw new FileNotFoundException(name);
    }

    // Each face of cube-rgba.dds is filled with its layer times 16.
    [Test]
    public void EveryFaceOfACube_GoesUpWhereItBelongs()
    {
        var device = (GraphicsDevice)GpuFixture.Main.ResourceLoaderDevice;
        using var cube = Texture.Load(device, Asset("cube-rgba.dds"));

        for (uint face = 0; face < 6; face++)
        {
            using var read = cube.ReadbackToImage(face);
            var bytes = new byte[read.TotalSizeInBytes];
            Marshal.Copy(read.DataPointer, bytes, 0, bytes.Length);
            Assert.That(bytes.Distinct(), Is.EqualTo(new[] { (byte)(face * 16) }), $"face {face}");
        }
    }

    [Test]
    public void ALayeredDds_MakesATexture()
    {
        var device = (GraphicsDevice)GpuFixture.Main.ResourceLoaderDevice;

        Assert.That(() => Texture.Load(device, Asset("array-bc7.dds")).Dispose(), Throws.Nothing);
        Assert.That(() => Texture.Load(device, Asset("volume-r8.dds")).Dispose(), Throws.Nothing);
    }
}
