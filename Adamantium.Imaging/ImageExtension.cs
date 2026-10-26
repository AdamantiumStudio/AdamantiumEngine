using System;

namespace Adamantium.Imaging;

public static class ImageExtension
{
    /// <summary>The image as a picture to save: the animation it was loaded from, as it was, or the image itself -
    /// its layers the frames, its mip levels the levels. Each codec takes of it what its format holds.</summary>
    public static IRawBitmap ConvertToRawBitmap(this Image image, ImageFileType fileType = ImageFileType.Png) =>
        image.Frames ?? new ImageBitmap(image);
}

/// <summary>An <see cref="Image"/> seen as a picture: layer by layer the frames, the first layer's mip levels the
/// levels, every part of it to a codec that takes them all.</summary>
internal sealed class ImageBitmap(Image image) : IRawBitmap, ISubresources
{
    private uint Layers => image.Description.Dimension == TextureDimension.Texture3D ? 1 : image.Description.ArraySize;

    public uint Width => image.Description.Width;

    public uint Height => image.Description.Height;

    public SurfaceFormat PixelFormat => image.Description.Format;

    public bool IsMultiFrame => Layers > 1;

    public bool HasMipLevels => image.Description.MipLevels > 1;

    public uint MipLevelsCount => image.Description.MipLevels;

    public uint NumberOfReplays => 0;

    public uint FramesCount => Layers;

    /// <summary>The image this is.</summary>
    public Image Image => image;

    public byte[] GetRawPixels(uint frameIndex) => image.GetPixelBuffer((int)frameIndex, 0, 0).GetPixels<byte>();

    public FrameData GetMipLevelData(uint mipLevel)
    {
        var buffer = image.GetPixelBuffer(0, 0, (int)mipLevel);
        return new FrameData(buffer.GetPixels<byte>(), buffer.GetDescription(), mipLevel);
    }

    public ImageDescription GetImageDescription() => image.Description;

    public FrameData GetFrameData(uint frameIndex) =>
        new(GetRawPixels(frameIndex), ImageDescription.Default2D(Width, Height, PixelFormat));

    public void CopySubresource(int layer, int level, int slice, PixelBuffer target) =>
        image.GetPixelBuffer(layer, slice, level).CopyTo(target);
}
