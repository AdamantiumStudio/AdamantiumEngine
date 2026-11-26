using Adamantium.Graphics;
using Adamantium.Graphics.Core;
using Adamantium.Imaging;
using Adamantium.Vulkan.Core;
using TextureDimension = Adamantium.Imaging.TextureDimension;

namespace Adamantium.Engine.GraphicsTests;

/// <summary>A small red texture of one alpha all over, for a surface masked or blended by its texture.</summary>
internal static class RedTexture
{
    private const uint Side = 4;

    public static Texture Create(IGraphicsDevice device, byte alpha)
    {
        var texels = new byte[Side * Side * 4];
        for (var start = 0; start < texels.Length; start += 4)
        {
            texels[start] = 255;
            texels[start + 3] = alpha;
        }

        return Texture.CreateFrom(device, Description(), texels);
    }

    private static TextureDescription Description() => new()
    {
        Width = Side,
        Height = Side,
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
        Dimension = TextureDimension.Texture2D
    };
}
