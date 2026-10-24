using System.Collections.Generic;
using System.Linq;
using Adamantium.Graphics;
using Adamantium.Graphics.Core;
using Adamantium.Vulkan.Core;
using NUnit.Framework;

namespace Adamantium.Engine.GraphicsTests;

[TestFixture]
public class DescriptorSetManagerTests
{
    [Test]
    public void TheTextureArray_StartsAtTheSettingsCapacity_AndGrowsWhenFull_KeepingEveryIndex()
    {
        var settings = GraphicsSettings.CreateDefault();
        settings.DescriptorModel = DescriptorModel.Sets;
        settings.TextureArrayCapacity = 2;
        var main = MainGraphicsDevice.Create(new GraphicsDeviceFactory(), settings, "TextureArrayGrows", false);
        var textures = new List<ITexture>();
        try
        {
            var manager = (DescriptorSetManager)main.DescriptorManager;
            Assume.That(manager.MaxTextureArrayCapacity, Is.GreaterThanOrEqualTo(8u),
                "the device's update-after-bind limits leave room for 8 textures");
            var array = manager.TextureArrayAt(0);
            var firstSet = array.Set;
            Assert.That(manager.TextureArrayCapacity, Is.EqualTo(2u));

            for (var i = 0; i < 5; i++)
            {
                textures.Add(main.ResourceLoaderDevice.CreateTexture(Description(), new byte[4]));
            }

            var indices = textures.Select(manager.TextureIndex).ToArray();

            Assert.Multiple(() =>
            {
                Assert.That(indices, Is.EqualTo(new uint[] { 0, 1, 2, 3, 4 }));
                Assert.That(manager.TextureArrayCapacity, Is.EqualTo(8u));
                Assert.That(array.Generation, Is.EqualTo(2ul));
                Assert.That(array.Set, Is.Not.SameAs(firstSet));
                Assert.That(textures.Select(manager.TextureIndex), Is.EqualTo(indices));
            });
        }
        finally
        {
            foreach (var texture in textures)
            {
                (texture as System.IDisposable)?.Dispose();
            }

            main.Dispose();
        }
    }

    [Test]
    public void TheTextureArrayCapacity_IsBoundedByTheDevice()
    {
        var settings = GraphicsSettings.CreateDefault();
        settings.DescriptorModel = DescriptorModel.Sets;
        settings.TextureArrayCapacity = uint.MaxValue;
        var main = MainGraphicsDevice.Create(new GraphicsDeviceFactory(), settings, "TextureArrayBound", false);
        try
        {
            var manager = (DescriptorSetManager)main.DescriptorManager;
            var limits = main.GraphicsAdapter.Vulkan12Properties;

            Assert.Multiple(() =>
            {
                Assert.That(manager.TextureArrayCapacity, Is.EqualTo(manager.MaxTextureArrayCapacity));
                Assert.That(manager.MaxTextureArrayCapacity,
                    Is.LessThanOrEqualTo(limits.MaxDescriptorSetUpdateAfterBindSampledImages));
                Assert.That(manager.MaxTextureArrayCapacity,
                    Is.LessThanOrEqualTo(limits.MaxPerStageDescriptorUpdateAfterBindSampledImages));
            });
        }
        finally
        {
            main.Dispose();
        }
    }

    private static TextureDescription Description() => new()
    {
        Width = 1,
        Height = 1,
        Depth = 1,
        ArrayLayers = 1,
        MipLevels = 1,
        Samples = MSAALevel.None,
        Format = Format.R8G8B8A8_UNORM,
        InitialLayout = ImageLayout.Undefined,
        DesiredImageLayout = ImageLayout.ShaderReadOnlyOptimal,
        ImageType = ImageType._2d,
        ImageAspect = ImageAspectFlagBits.ColorBit,
        ImageTiling = ImageTiling.Optimal,
        Usage = ImageUsageFlagBits.SampledBit | ImageUsageFlagBits.TransferDstBit,
        Dimension = Adamantium.Imaging.TextureDimension.Texture2D
    };
}
