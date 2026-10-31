using System;
using System.Runtime.InteropServices;
using Adamantium.Graphics;
using Adamantium.Graphics.Core;
using Adamantium.Imaging;
using Adamantium.Vulkan.Core;
using NUnit.Framework;

namespace Adamantium.Engine.GraphicsTests;

[TestFixture]
public class TextureRegionUpdateTests
{
    private const uint Size = 16;

    [TearDown]
    public void ReleaseDevices() => GpuFixture.ReleaseRenderDevices();

    private static ITexture NewArray(IGraphicsDevice device)
    {
        var description = new TextureDescription
        {
            Width = Size,
            Height = Size,
            Depth = 1,
            ArrayLayers = 2,
            MipLevels = 2,
            Samples = MSAALevel.None,
            Format = Format.R8G8B8A8_UNORM,
            InitialLayout = ImageLayout.Undefined,
            DesiredImageLayout = ImageLayout.ShaderReadOnlyOptimal,
            ImageType = ImageType._2d,
            ImageAspect = ImageAspectFlagBits.ColorBit,
            Usage = ImageUsageFlagBits.SampledBit | ImageUsageFlagBits.TransferDstBit | ImageUsageFlagBits.TransferSrcBit,
            Dimension = TextureDimension.Texture2D,
        };
        var black = new byte[Size * Size * 4];
        return device.CreateTextureArray(description, [black, black]);
    }

    private static byte[] Solid(uint width, uint height, byte r, byte g, byte b)
    {
        var pixels = new byte[width * height * 4];
        for (var i = 0; i < pixels.Length; i += 4)
        {
            pixels[i] = r;
            pixels[i + 1] = g;
            pixels[i + 2] = b;
            pixels[i + 3] = 255;
        }

        return pixels;
    }

    private static byte[] PixelAt(ITexture texture, uint layer, uint level, int x, int y)
    {
        using var image = texture.ReadbackToImage(layer, level);
        var width = Math.Max(1u, Size >> (int)level);
        var pixel = new byte[4];
        Marshal.Copy(image.DataPointer + (int)((y * width + x) * 4), pixel, 0, 4);
        return pixel;
    }

    [Test]
    public void ARegionLandsInItsLayerAndLeavesTheRestAlone()
    {
        var device = GpuFixture.CreateRenderDevice();
        var array = NewArray(device);

        device.UpdateTextureRegions(array, [new TextureRegionUpdate(1, 0, 4, 6, 2, 3, Solid(2, 3, 255, 0, 0))]);

        Assert.Multiple(() =>
        {
            Assert.That(PixelAt(array, 1, 0, 4, 6), Is.EqualTo(new byte[] { 255, 0, 0, 255 }));
            Assert.That(PixelAt(array, 1, 0, 5, 8), Is.EqualTo(new byte[] { 255, 0, 0, 255 }));
            Assert.That(PixelAt(array, 1, 0, 6, 6), Is.EqualTo(new byte[] { 0, 0, 0, 0 }), "right of the region");
            Assert.That(PixelAt(array, 1, 0, 4, 9), Is.EqualTo(new byte[] { 0, 0, 0, 0 }), "below the region");
            Assert.That(PixelAt(array, 0, 0, 4, 6), Is.EqualTo(new byte[] { 0, 0, 0, 0 }), "the other layer");
            Assert.That(array.ImageLayout, Is.EqualTo(ImageLayout.ShaderReadOnlyOptimal), "ready to sample again");
        });
        array.Dispose();
    }

    [Test]
    public void SeveralRegionsAndLevelsGoInOneCall()
    {
        var device = GpuFixture.CreateRenderDevice();
        var array = NewArray(device);

        device.UpdateTextureRegions(array,
        [
            new TextureRegionUpdate(0, 0, 0, 0, 1, 1, Solid(1, 1, 0, 255, 0)),
            new TextureRegionUpdate(0, 1, 0, 0, Size / 2, Size / 2, Solid(Size / 2, Size / 2, 0, 0, 255)),
        ]);

        Assert.Multiple(() =>
        {
            Assert.That(PixelAt(array, 0, 0, 0, 0), Is.EqualTo(new byte[] { 0, 255, 0, 255 }));
            Assert.That(PixelAt(array, 0, 1, 7, 7), Is.EqualTo(new byte[] { 0, 0, 255, 255 }));
        });
        array.Dispose();
    }

    [Test]
    public void ARegionThatDoesNotFitIsRefused()
    {
        var device = GpuFixture.CreateRenderDevice();
        var array = NewArray(device);

        Assert.Multiple(() =>
        {
            Assert.Throws<ArgumentOutOfRangeException>(() => device.UpdateTextureRegions(array,
                [new TextureRegionUpdate(0, 1, 6, 0, 4, 1, Solid(4, 1, 1, 1, 1))]), "past the edge of level 1 (8 wide)");
            Assert.Throws<ArgumentOutOfRangeException>(() => device.UpdateTextureRegions(array,
                [new TextureRegionUpdate(2, 0, 0, 0, 1, 1, Solid(1, 1, 1, 1, 1))]), "a third layer of two");
            Assert.Throws<ArgumentException>(() => device.UpdateTextureRegions(array,
                [new TextureRegionUpdate(0, 0, 0, 0, 2, 2, Solid(1, 1, 1, 1, 1))]), "too few bytes");
            Assert.Throws<ArgumentException>(() => device.UpdateTextureRegions(((Texture)array).GetLevelTexture(1),
                [new TextureRegionUpdate(0, 0, 0, 0, 1, 1, Solid(1, 1, 1, 1, 1))]), "a view of one level");
        });
        array.Dispose();
    }
}
