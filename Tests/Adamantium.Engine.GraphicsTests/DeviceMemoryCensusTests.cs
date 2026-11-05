using Adamantium.Graphics;
using Adamantium.Graphics.Core;
using Adamantium.Imaging;
using Adamantium.Vulkan.Core;
using NUnit.Framework;

namespace Adamantium.Engine.GraphicsTests;

[TestFixture]
public class DeviceMemoryCensusTests
{
    [TearDown]
    public void ReleaseDevices() => GpuFixture.ReleaseRenderDevices();

    private static TextureDescription Layers(uint layers) => new()
    {
        Width = 64,
        Height = 64,
        Depth = 1,
        ArrayLayers = layers,
        MipLevels = 1,
        Samples = MSAALevel.None,
        Format = Format.R8G8B8A8_UNORM,
        InitialLayout = ImageLayout.Undefined,
        DesiredImageLayout = ImageLayout.ShaderReadOnlyOptimal,
        ImageType = ImageType._2d,
        ImageAspect = ImageAspectFlagBits.ColorBit,
        Usage = ImageUsageFlagBits.SampledBit | ImageUsageFlagBits.TransferDstBit,
        Dimension = TextureDimension.Texture2D,
    };

    [Test]
    public void ATexture_IsCountedWithWhatItHolds_AndLeavesWithItsDisposal()
    {
        var device = GpuFixture.CreateRenderDevice();
        var before = DeviceMemoryCensus.Take(GpuFixture.Main);
        var imagesBefore = GpuFixture.Main.ImageBytes;

        var texture = Texture.New(device, Layers(4), "Census probe");
        var held = DeviceMemoryCensus.Take(GpuFixture.Main);

        Assert.That(texture.DeviceMemoryBytes, Is.GreaterThanOrEqualTo(64UL * 64 * 4 * 4), "four layers of 64x64 RGBA");
        Assert.That(held.TotalBytes - before.TotalBytes, Is.EqualTo(texture.DeviceMemoryBytes));
        Assert.That(GpuFixture.Main.ImageBytes - imagesBefore, Is.EqualTo((long)texture.DeviceMemoryBytes),
            "the allocation is counted where it is made");
        Assert.That(held.Report(), Does.Contain("'Census probe' 64x64 [4] R8G8B8A8_UNORM"));

        texture.Dispose();
        texture.Dispose();
        Assert.That(DeviceMemoryCensus.Take(GpuFixture.Main).TotalBytes, Is.EqualTo(before.TotalBytes));
        Assert.That(GpuFixture.Main.ImageBytes, Is.EqualTo(imagesBefore), "and uncounted once, however often disposed");
    }
}
