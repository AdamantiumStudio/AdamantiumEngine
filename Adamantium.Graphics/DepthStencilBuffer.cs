using Adamantium.Graphics.Core;
using Adamantium.Imaging;
using Adamantium.Vulkan.Core;

namespace Adamantium.Graphics;

public class DepthStencilBuffer : Texture, IDepthStencilBuffer
{
    internal DepthStencilBuffer(IGraphicsDevice device, TextureDescription description, string name = "") : base(device,
        description, name)
    {
    }

    public static DepthStencilBuffer New(IGraphicsDevice graphicsDevice, 
        uint width, 
        uint height, 
        DepthFormat format, 
        MSAALevel msaa,
        ImageAspectFlagBits imageAspect = ImageAspectFlagBits.DepthBit,
        string name = "")
    {
        if (imageAspect.HasFlag(ImageAspectFlagBits.DepthBit))
        {
            imageAspect |= ImageAspectFlagBits.StencilBit;
        }

        TextureDescription description = new TextureDescription
        {
            Width = width,
            Height = height,
            Depth = 1,
            Dimension = TextureDimension.Texture2D,
            ArrayLayers = 1,
            Usage = ImageUsageFlagBits.DepthStencilAttachmentBit,
            Format = (Format)format,
            DesiredImageLayout = ImageLayout.DepthStencilAttachmentOptimal,
            ImageTiling = ImageTiling.Optimal,
            ImageType = ImageType._2d,
            MipLevels = 1,
            SharingMode = SharingMode.Exclusive,
            ImageAspect = imageAspect,
            Samples = msaa
        };

        return new DepthStencilBuffer(graphicsDevice, description, name);
    }

    /// <summary>A depth buffer a shader can read once it is drawn - a shadow map. Of depth alone, so
    /// <paramref name="format"/> is <see cref="DepthFormat.Depth16"/> or <see cref="DepthFormat.Depth32"/>.</summary>
    public static DepthStencilBuffer NewReadable(IGraphicsDevice graphicsDevice, uint width, uint height,
        DepthFormat format, string name = "")
    {
        if (format is not (DepthFormat.Depth16 or DepthFormat.Depth32))
        {
            throw new System.ArgumentException($"A readable depth buffer holds depth alone, not {format}.",
                nameof(format));
        }

        var description = new TextureDescription
        {
            Width = width,
            Height = height,
            Depth = 1,
            Dimension = TextureDimension.Texture2D,
            ArrayLayers = 1,
            Usage = ImageUsageFlagBits.DepthStencilAttachmentBit | ImageUsageFlagBits.SampledBit,
            Format = (Format)format,
            // It rests where a shader reads it, as its descriptor says; a pass that draws it turns it to an attachment
            // and back.
            DesiredImageLayout = ImageLayout.ShaderReadOnlyOptimal,
            ImageTiling = ImageTiling.Optimal,
            ImageType = ImageType._2d,
            MipLevels = 1,
            SharingMode = SharingMode.Exclusive,
            ImageAspect = ImageAspectFlagBits.DepthBit,
            Samples = MSAALevel.None
        };

        return new DepthStencilBuffer(graphicsDevice, description, name);
    }
}