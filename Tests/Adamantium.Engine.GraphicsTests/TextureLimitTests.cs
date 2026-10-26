using System;
using Adamantium.Graphics.Core;
using Adamantium.Imaging;
using Adamantium.Vulkan.Core;
using NUnit.Framework;

namespace Adamantium.Engine.GraphicsTests;

/// <summary>A texture past the device's limits is refused by name, before Vulkan is asked for it.</summary>
[TestFixture]
public class TextureLimitTests
{
    private static TextureDescription Description(uint width, uint height, uint layers = 1) => new()
    {
        Width = width,
        Height = height,
        Depth = 1,
        Dimension = TextureDimension.Texture2D,
        ImageType = ImageType._2d,
        Format = Format.R8G8B8A8_UNORM,
        MipLevels = 1,
        ArrayLayers = layers,
        Usage = ImageUsageFlagBits.SampledBit | ImageUsageFlagBits.TransferDstBit,
        ImageTiling = ImageTiling.Optimal,
        SharingMode = SharingMode.Exclusive,
        InitialLayout = ImageLayout.Undefined,
        DesiredImageLayout = ImageLayout.ShaderReadOnlyOptimal,
        ImageAspect = ImageAspectFlagBits.ColorBit
    };

    [Test]
    public void ATexturePastTheDevicesSide_IsRefused()
    {
        var device = GpuFixture.Main.ResourceLoaderDevice;
        var side = device.Adapter.AdapterProperties.Limits.MaxImageDimension2D;

        Assert.That(() => device.CreateTexture(Description(side + 1, 1), new byte[(side + 1) * 4]),
            Throws.InstanceOf<NotSupportedException>().With.Message.Contains(side.ToString()));
    }

    [Test]
    public void ATexturePastTheDevicesLayers_IsRefused()
    {
        var device = GpuFixture.Main.ResourceLoaderDevice;
        var layers = device.Adapter.AdapterProperties.Limits.MaxImageArrayLayers;
        var pixels = new byte[layers + 1][];
        for (var i = 0; i < pixels.Length; i++)
        {
            pixels[i] = new byte[4];
        }

        Assert.That(() => device.CreateTextureArray(Description(1, 1, layers + 1), pixels),
            Throws.InstanceOf<NotSupportedException>());
    }
}
