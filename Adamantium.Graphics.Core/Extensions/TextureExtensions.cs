using System;
using Adamantium.Vulkan.Core;

namespace Adamantium.Graphics.Core.Extensions;

public static class TextureExtensions
{
    public static void TransitionImageLayout(this ITexture texture, ImageLayout newLayout)
    {
        var commandBuffer = texture.GraphicsDevice.BeginSingleTimeCommand();
        texture.RecordLayoutTransition(commandBuffer, newLayout);
        texture.GraphicsDevice.EndSingleTimeCommand(commandBuffer);
    }

    /// <summary>Records into <paramref name="commandBuffer"/> the barrier that takes the texture to
    /// <paramref name="newLayout"/>, so it runs with the rest of that buffer's work rather than in a submit of its own.</summary>
    public static void RecordLayoutTransition(this ITexture texture, CommandBuffer commandBuffer, ImageLayout newLayout)
    {
        var imageMemoryBarrier = new ImageMemoryBarrier2
        {
            OldLayout = texture.ImageLayout,
            NewLayout = newLayout,
            SrcQueueFamilyIndex = Constants.VK_QUEUE_FAMILY_IGNORED,
            DstQueueFamilyIndex = Constants.VK_QUEUE_FAMILY_IGNORED,
            Image = texture.GetImage(),
            SubresourceRange = new ImageSubresourceRange()
        };

        imageMemoryBarrier.SubresourceRange.BaseMipLevel = 0;
        imageMemoryBarrier.SubresourceRange.LevelCount = Constants.VK_REMAINING_MIP_LEVELS;
        imageMemoryBarrier.SubresourceRange.BaseArrayLayer = 0;
        // All layers: with a count of 1 only layer 0 of an array texture changed layout.
        imageMemoryBarrier.SubresourceRange.LayerCount = Constants.VK_REMAINING_ARRAY_LAYERS;

        if (newLayout == ImageLayout.DepthStencilAttachmentOptimal)
        {
            imageMemoryBarrier.SubresourceRange.AspectMask = ImageAspectFlagBits.DepthBit;

            if (texture.SurfaceFormat.HasStencilFormat())
            {
                imageMemoryBarrier.SubresourceRange.AspectMask |= ImageAspectFlagBits.StencilBit;
            }
        }
        else
        {
            imageMemoryBarrier.SubresourceRange.AspectMask = ImageAspectFlagBits.ColorBit;
        }

        PipelineStageFlagBits2 sourceStage;
        PipelineStageFlagBits2 destinationStage;

        switch (texture.ImageLayout)
        {
            case ImageLayout.Undefined:
                // Image layout is undefined (or does not matter)
                // Only valid as initial layout
                // No flags required, listed only for completeness
                imageMemoryBarrier.SrcAccessMask = 0;
                sourceStage = PipelineStageFlagBits2.TopOfPipeBit;
                break;

            case ImageLayout.Preinitialized:
                // Image is preinitialized
                // Only valid as initial layout for linear images, preserves memory contents
                // Make sure host writes have been finished
                imageMemoryBarrier.SrcAccessMask = 0;
                sourceStage = PipelineStageFlagBits2.TopOfPipeBit;
                break;

            case ImageLayout.ColorAttachmentOptimal:
                // Image is a color attachment
                // Make sure any writes to the color buffer have been finished
                imageMemoryBarrier.SrcAccessMask = AccessFlagBits2.ColorAttachmentWriteBit;
                sourceStage = PipelineStageFlagBits2.ColorAttachmentOutputBit;
                break;

            case ImageLayout.DepthStencilAttachmentOptimal:
                // Image is a depth/stencil attachment
                // Make sure any writes to the depth/stencil buffer have been finished
                imageMemoryBarrier.SrcAccessMask = AccessFlagBits2.DepthStencilAttachmentWriteBit;
                sourceStage = PipelineStageFlagBits2.EarlyFragmentTestsBit;
                break;

            case ImageLayout.TransferSrcOptimal:
                // Image is a transfer source
                // Make sure any reads from the image have been finished
                imageMemoryBarrier.SrcAccessMask = AccessFlagBits2.TransferReadBit;
                sourceStage = PipelineStageFlagBits2.AllTransferBit;
                break;

            case ImageLayout.TransferDstOptimal:
                // Image is a transfer destination
                // Make sure any writes to the image have been finished
                imageMemoryBarrier.SrcAccessMask = AccessFlagBits2.TransferWriteBit;
                sourceStage = PipelineStageFlagBits2.AllTransferBit;
                break;

            case ImageLayout.ShaderReadOnlyOptimal:
                // Image is read by a shader
                // Make sure any shader reads from the image have been finished
                imageMemoryBarrier.SrcAccessMask = AccessFlagBits2.ShaderReadBit;
                sourceStage = PipelineStageFlagBits2.FragmentShaderBit;
                break;
            case ImageLayout.PresentSrcKhr:
                imageMemoryBarrier.SrcAccessMask = AccessFlagBits2.None;
                sourceStage = PipelineStageFlagBits2.BottomOfPipeBit;
                break;
            case ImageLayout.General:
                // Host image copy read the image (Texture.Save); the access happened on the host.
                imageMemoryBarrier.SrcAccessMask = AccessFlagBits2.HostReadBit;
                sourceStage = PipelineStageFlagBits2.HostBit;
                break;
            default:
                throw new ArgumentException(
                    $"Transferring from {texture.ImageLayout} image layout is not supported yet");
        }

        switch (newLayout)
        {
            case ImageLayout.TransferDstOptimal:
                // Image will be used as a transfer destination
                // Make sure any writes to the image have been finished
                imageMemoryBarrier.DstAccessMask = AccessFlagBits2.TransferWriteBit;
                destinationStage = PipelineStageFlagBits2.AllTransferBit;
                break;

            case ImageLayout.TransferSrcOptimal:
                // Image will be used as a transfer source
                // Make sure any reads from the image have been finished
                imageMemoryBarrier.DstAccessMask = AccessFlagBits2.TransferReadBit;
                destinationStage = PipelineStageFlagBits2.AllTransferBit;
                break;

            case ImageLayout.ColorAttachmentOptimal:
                // Image will be used as a color attachment
                // Make sure any writes to the color buffer have been finished
                imageMemoryBarrier.DstAccessMask = AccessFlagBits2.ColorAttachmentWriteBit;
                destinationStage = PipelineStageFlagBits2.ColorAttachmentOutputBit;
                break;

            case ImageLayout.DepthStencilAttachmentOptimal:
                // Image layout will be used as a depth/stencil attachment
                // Make sure any writes to depth/stencil buffer have been finished
                imageMemoryBarrier.DstAccessMask |= AccessFlagBits2.DepthStencilAttachmentReadBit |
                                                    AccessFlagBits2.DepthStencilAttachmentWriteBit;
                destinationStage = PipelineStageFlagBits2.EarlyFragmentTestsBit;
                break;

            case ImageLayout.ShaderReadOnlyOptimal:
                imageMemoryBarrier.DstAccessMask = AccessFlagBits2.ShaderReadBit;
                destinationStage = PipelineStageFlagBits2.FragmentShaderBit;
                break;
            case ImageLayout.PresentSrcKhr:
                imageMemoryBarrier.DstAccessMask = AccessFlagBits2.None;
                destinationStage = PipelineStageFlagBits2.BottomOfPipeBit;
                break;
            case ImageLayout.General:
                // Will be read by host image copy (Texture.Save) right after this transition.
                imageMemoryBarrier.DstAccessMask = AccessFlagBits2.HostReadBit;
                destinationStage = PipelineStageFlagBits2.HostBit;
                break;
            default:
                throw new ArgumentException($"Transferring to {newLayout} is not handled yet");
        }

        imageMemoryBarrier.SrcStageMask = sourceStage;
        imageMemoryBarrier.DstStageMask = destinationStage;

        commandBuffer.PipelineBarrier2(new DependencyInfo
        {
            PImageMemoryBarriers = new[] { imageMemoryBarrier },
            ImageMemoryBarrierCount = 1
        });

        texture.ImageLayout = newLayout;
    }

    public static void BlitImage(this IGraphicsDevice graphicsDevice, CommandBuffer commandBuffer, ITexture srcTexture, ITexture dstTexture)
    {
        var blit = new ImageBlit();
        blit.SrcSubresource = new ImageSubresourceLayers();
        blit.SrcSubresource.AspectMask     = ImageAspectFlagBits.ColorBit;
        blit.SrcSubresource.BaseArrayLayer = 0;
        blit.SrcSubresource.LayerCount     = 1;
        blit.SrcSubresource.MipLevel       = 0;
        var srcOffsets = new Offset3D[2];
        srcOffsets[0] = new Offset3D() {X = 0, Y = 0, Z = 0};
        srcOffsets[1] = new Offset3D() {X = (int)srcTexture.Width, Y = (int)srcTexture.Height, Z = 1};
        blit.SrcOffsets = srcOffsets; 

        // Copy color from source to destination of screen size
        blit.DstSubresource = new ImageSubresourceLayers();
        blit.DstSubresource.AspectMask     = ImageAspectFlagBits.ColorBit;
        blit.DstSubresource.BaseArrayLayer = 0;
        blit.DstSubresource.LayerCount     = 1;
        blit.DstSubresource.MipLevel       = 0;
        var dstOffsets = new Offset3D[2];
        dstOffsets[0] = new Offset3D() {X = 0, Y = 0, Z = 0};
        dstOffsets[1] = new Offset3D() {X = (int)dstTexture.Width, Y = (int)dstTexture.Height, Z = 1};
        blit.DstOffsets = dstOffsets;

        graphicsDevice.InsertImageMemoryBarrier(commandBuffer,
            srcTexture,
            AccessFlagBits2.ColorAttachmentWriteBit,
            AccessFlagBits2.TransferReadBit,
            //ImageLayout.ColorAttachmentOptimal,
            srcTexture.ImageLayout,
            ImageLayout.TransferSrcOptimal,
            PipelineStageFlagBits2.ColorAttachmentOutputBit,
            PipelineStageFlagBits2.AllTransferBit
        );
        
        // Destination (swapchain) texture: the source stages match the imageAvailable wait, or the transition could run
        // before the presentation engine releases the image.
        graphicsDevice.InsertImageMemoryBarrier(commandBuffer,
            dstTexture,
            0,
            AccessFlagBits2.TransferWriteBit,
            ImageLayout.Undefined,
            ImageLayout.TransferDstOptimal,
            PipelineStageFlagBits2.ColorAttachmentOutputBit | PipelineStageFlagBits2.AllTransferBit,
            PipelineStageFlagBits2.AllTransferBit
        );

        commandBuffer.BlitImage(
            srcTexture.GetImage(), 
            srcTexture.ImageLayout, 
            dstTexture.GetImage(),
            dstTexture.ImageLayout, 
            1, 
            blit, 
            Filter.Linear);

    }
}