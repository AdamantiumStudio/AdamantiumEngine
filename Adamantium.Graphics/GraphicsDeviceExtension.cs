using System;
using System.Diagnostics;
using Adamantium.Graphics.Core;
using Adamantium.Graphics.Core.Extensions;
using Adamantium.Vulkan.Core;

namespace Adamantium.Graphics;

public static class GraphicsDeviceExtension
{
    /// <summary>Records into the current command buffer a copy of a resolved render target into a shared surface, leaving the
    /// destination in ShaderReadOnly for the consumer. Call after EndDraw, before Submit.</summary>
    public static void RecordSharedSurfaceCopy(this IGraphicsDevice graphicsDevice, ITexture source, ITexture destination)
    {
        if (source == null || destination == null) return;
        var commandBuffer = graphicsDevice.CurrentCommandBuffer;

        graphicsDevice.InsertImageMemoryBarrier(commandBuffer, source,
            AccessFlagBits2.ColorAttachmentWriteBit, AccessFlagBits2.TransferReadBit,
            ImageLayout.ColorAttachmentOptimal, ImageLayout.TransferSrcOptimal,
            PipelineStageFlagBits2.ColorAttachmentOutputBit, PipelineStageFlagBits2.AllTransferBit);

        graphicsDevice.InsertImageMemoryBarrier(commandBuffer, destination,
            AccessFlagBits2.ShaderReadBit, AccessFlagBits2.TransferWriteBit,
            ImageLayout.ShaderReadOnlyOptimal, ImageLayout.TransferDstOptimal,
            PipelineStageFlagBits2.FragmentShaderBit, PipelineStageFlagBits2.AllTransferBit);

        // Clamp the copy region to the SMALLER of the two images. During a resize the source (resolved RT) and the
        // destination (shared surface / swapchain image) can be different sizes for one frame; copying the full source
        // extent into a smaller destination writes out of bounds -> GPU fault -> VK_ERROR_DEVICE_LOST
        // (VUID-vkCmdCopyImage-dstOffset-00150). The few edge pixels dropped for that transient frame are invisible.
        var imageCopy = new ImageCopy
        {
            Extent = new Extent3D { Width = Math.Min(source.Width, destination.Width), Height = Math.Min(source.Height, destination.Height), Depth = 1 },
            SrcOffset = new Offset3D(),
            DstOffset = new Offset3D(),
            SrcSubresource = new ImageSubresourceLayers { AspectMask = ImageAspectFlagBits.ColorBit, LayerCount = 1 },
            DstSubresource = new ImageSubresourceLayers { AspectMask = ImageAspectFlagBits.ColorBit, LayerCount = 1 }
        };
        commandBuffer.CopyImage(source.GetImage(), ImageLayout.TransferSrcOptimal,
            destination.GetImage(), ImageLayout.TransferDstOptimal, 1, imageCopy);

        graphicsDevice.InsertImageMemoryBarrier(commandBuffer, source,
            AccessFlagBits2.TransferReadBit, AccessFlagBits2.ColorAttachmentWriteBit,
            ImageLayout.TransferSrcOptimal, ImageLayout.ColorAttachmentOptimal,
            PipelineStageFlagBits2.AllTransferBit, PipelineStageFlagBits2.ColorAttachmentOutputBit);

        graphicsDevice.InsertImageMemoryBarrier(commandBuffer, destination,
            AccessFlagBits2.TransferWriteBit, AccessFlagBits2.ShaderReadBit,
            ImageLayout.TransferDstOptimal, ImageLayout.ShaderReadOnlyOptimal,
            PipelineStageFlagBits2.AllTransferBit, PipelineStageFlagBits2.FragmentShaderBit);
    }

    public static void CopyImage(this IGraphicsDevice graphicsDevice, ITexture sourceTexture,
        ITexture destinationTexture)
    {
        if (sourceTexture == null)
        {
            Debug.WriteLine("Resolve Texture is null");
            return;
        }

        if (destinationTexture == null)
        {
            Debug.WriteLine("Destination Texture is null");
            return;
        }

        var commandBuffer = graphicsDevice.BeginSingleTimeCommand();
        try
        {
            RecordCopy(graphicsDevice, commandBuffer, sourceTexture, destinationTexture);
        }
        catch
        {
            // The layout changes it submits on the way may fail: the scope is given back, not left holding the lock.
            graphicsDevice.AbandonSingleTimeCommand(commandBuffer);
            throw;
        }

        graphicsDevice.EndSingleTimeCommand(commandBuffer);
    }

    private static void RecordCopy(IGraphicsDevice graphicsDevice, CommandBuffer commandBuffer, ITexture sourceTexture,
        ITexture destinationTexture)
    {
        var imageCopy = new ImageCopy();
        imageCopy.Extent = new Extent3D();
        imageCopy.SrcOffset = new Offset3D();
        imageCopy.DstOffset = new Offset3D();
        imageCopy.Extent.Depth = 1;
        // Clamp to the smaller image so a size mismatch (e.g. mid-resize) can't copy past the destination bounds
        // -> GPU fault -> device lost (VUID-vkCmdCopyImage-dstOffset-00150).
        imageCopy.Extent.Width = Math.Min(sourceTexture.Width, destinationTexture.Width);
        imageCopy.Extent.Height = Math.Min(sourceTexture.Height, destinationTexture.Height);
        imageCopy.SrcSubresource = new ImageSubresourceLayers
        {
            AspectMask = ImageAspectFlagBits.ColorBit,
            LayerCount = 1
        };
        imageCopy.DstSubresource = new ImageSubresourceLayers
        {
            AspectMask = ImageAspectFlagBits.ColorBit,
            LayerCount = 1
        };

        sourceTexture.TransitionImageLayout(ImageLayout.TransferSrcOptimal);
        destinationTexture.TransitionImageLayout(ImageLayout.TransferDstOptimal);

        graphicsDevice.InsertImageMemoryBarrier(commandBuffer,
            sourceTexture,
            AccessFlagBits2.ColorAttachmentWriteBit,
            AccessFlagBits2.TransferReadBit,
            ImageLayout.ColorAttachmentOptimal,
            ImageLayout.TransferSrcOptimal,
            PipelineStageFlagBits2.ColorAttachmentOutputBit,
            PipelineStageFlagBits2.AllTransferBit);

        graphicsDevice.InsertImageMemoryBarrier(commandBuffer,
            destinationTexture,
            AccessFlagBits2.ShaderReadBit,
            AccessFlagBits2.TransferWriteBit,
            ImageLayout.ShaderReadOnlyOptimal,
            ImageLayout.TransferDstOptimal,
            PipelineStageFlagBits2.FragmentShaderBit,
            PipelineStageFlagBits2.AllTransferBit);

        commandBuffer.CopyImage(sourceTexture.GetImage(),
            ImageLayout.TransferSrcOptimal,
            destinationTexture.GetImage(),
            ImageLayout.TransferDstOptimal,
            1,
            imageCopy);

        graphicsDevice.InsertImageMemoryBarrier(commandBuffer,
            sourceTexture,
            AccessFlagBits2.TransferReadBit,
            AccessFlagBits2.ColorAttachmentWriteBit,
            ImageLayout.TransferSrcOptimal,
            ImageLayout.ColorAttachmentOptimal,
            PipelineStageFlagBits2.AllTransferBit,
            PipelineStageFlagBits2.ColorAttachmentOutputBit);

        graphicsDevice.InsertImageMemoryBarrier(commandBuffer,
            destinationTexture,
            AccessFlagBits2.TransferWriteBit,
            AccessFlagBits2.ShaderReadBit,
            ImageLayout.TransferDstOptimal,
            ImageLayout.ShaderReadOnlyOptimal,
            PipelineStageFlagBits2.AllTransferBit,
            PipelineStageFlagBits2.FragmentShaderBit);

        sourceTexture.TransitionImageLayout(ImageLayout.ColorAttachmentOptimal);
        destinationTexture.TransitionImageLayout(ImageLayout.ShaderReadOnlyOptimal);
    }
}