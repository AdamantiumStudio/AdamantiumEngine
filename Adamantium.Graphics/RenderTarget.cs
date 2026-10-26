using System;
using Adamantium.Graphics.Core;
using Adamantium.Imaging;
using Adamantium.Vulkan.Core;

namespace Adamantium.Graphics
{
    public class RenderTarget : Texture, IRenderTarget
    {
        internal RenderTarget(IGraphicsDevice device, TextureDescription description, string name = "") : base(device, description, name)
        {
            if (description.Samples != MSAALevel.None)
            {
                var clonedDesc = description;
                clonedDesc.Samples = MSAALevel.None;
                try
                {
                    ResolveTexture = ToDispose(Texture.New(device, clonedDesc, $"{name}:Resolve"));
                }
                catch
                {
                    Dispose();
                    throw;
                }
            }
            else
            {
                ResolveTexture = this;
            }
        }

        public static RenderTarget New(IGraphicsDevice graphicsDevice, 
            UInt32 width, 
            UInt32 height, 
            MSAALevel msaa, 
            SurfaceFormat format, 
            ImageUsageFlagBits usage = ImageUsageFlagBits.TransferSrcBit,
            ImageLayout desiredLayout = ImageLayout.ColorAttachmentOptimal,
            string name = "")
        {
            usage |= ImageUsageFlagBits.ColorAttachmentBit | ImageUsageFlagBits.SampledBit;

            var description = new TextureDescription
            {
                Width = width,
                Height = height,
                Depth = 1,
                Dimension = TextureDimension.Texture2D,
                ArrayLayers = 1,
                Usage = usage,
                Format = format,
                DesiredImageLayout = desiredLayout,
                ImageTiling = ImageTiling.Optimal,
                ImageType = ImageType._2d,
                MipLevels = 1,
                SharingMode = SharingMode.Exclusive,
                ImageAspect = ImageAspectFlagBits.ColorBit,
                Samples = msaa
            };

            return new RenderTarget(graphicsDevice, description, name);
        }

        public ITexture ResolveTexture { get; }
    }
}