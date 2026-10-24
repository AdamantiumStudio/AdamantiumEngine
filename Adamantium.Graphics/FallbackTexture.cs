using Adamantium.Graphics.Core;
using Adamantium.Vulkan.Core;

namespace Adamantium.Graphics;

/// <summary>What a shader samples for a texture parameter nobody bound: red 4x4 in Debug so the miss shows, transparent
/// 1x1 in Release.</summary>
internal static class FallbackTexture
{
    public static ITexture Create(IGraphicsDevice device)
    {
#if DEBUG
        const uint size = 4;
        var pixels = new byte[size * size * 4];
        for (var i = 0; i < pixels.Length; i += 4)
        {
            pixels[i] = 255;
            pixels[i + 3] = 255;
        }
#else
        const uint size = 1;
        var pixels = new byte[4];
#endif
        return device.CreateTexture(new TextureDescription
        {
            Width = size,
            Height = size,
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
            Dimension = Imaging.TextureDimension.Texture2D
        }, pixels);
    }
}
