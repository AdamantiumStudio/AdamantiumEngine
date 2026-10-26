using System.Collections.Generic;
using Adamantium.Imaging.Bmp;
using Adamantium.Imaging.Dds;
using Adamantium.Imaging.Ico;
using Adamantium.Imaging.Jpeg;
using Adamantium.Vulkan.Core;

namespace Adamantium.Imaging;

public static class ImageExtension
{
    public static IRawBitmap ConvertToRawBitmap(this Image image, ImageFileType fileType = ImageFileType.Png)
    {
        switch (fileType)
        {
            case ImageFileType.Bmp:
                {
                    var img = new BmpImage(image.Description);
                    img.PixelData = image.PixelBuffer[0].GetPixels<byte>();
                    return img;
                }
            case ImageFileType.Jpg:
                {
                    var pxBuffer = image.PixelBuffer[0];
                    return new JpegImage((uint)pxBuffer.Width, (uint)pxBuffer.Height, image.Description.Format,
                        pxBuffer.GetPixels<byte>());
                }
            case ImageFileType.Png:
            case ImageFileType.Gif:
            case ImageFileType.Tga:
                {
                    // Every buffer of the picture's own size is a frame - an animation of several.
                    var frames = new List<byte[]>();
                    for (var index = 0; index < image.PixelBuffer.Count; index++)
                    {
                        var pixelBuffer = image.PixelBuffer[index];
                        if (pixelBuffer.Width == image.Description.Width && pixelBuffer.Height == image.Description.Height)
                        {
                            frames.Add(pixelBuffer.GetPixels<byte>());
                        }
                    }

                    return new FrameBitmap(image.Description.Width, image.Description.Height, image.Description.Format,
                        frames);
                }
            case ImageFileType.Dds:
                {
                    var img = new DdsImage(image.Description);
                    var frameDataList = new List<FrameData>();
                    for (int i = 0; i < image.PixelBuffer.Count; i++)
                    {
                        var pxBuffer = image.PixelBuffer[i];
                        var description = new ImageDescription()
                        {
                            Width = pxBuffer.Width,
                            Height = pxBuffer.Height,
                            Depth = 1,
                            ArraySize = 1,
                            Format = pxBuffer.Format,
                            Dimension = image.Description.Dimension,
                            MipLevels = pxBuffer.MipLevel
                        };

                        var frameData = new FrameData(pxBuffer.GetPixels<byte>(), description, pxBuffer.MipLevel);
                        frameDataList.Add(frameData);
                    }

                    return img;
                }
            case ImageFileType.Ico:
                {
                    var img = new IcoImage(image.Description);
                    for (int i = 0; i < image.PixelBuffer.Count; i++)
                    {
                        var pxBuffer = image.PixelBuffer[i];
                        var frame = new FrameData(pxBuffer.GetPixels<byte>(), pxBuffer.GetDescription(), pxBuffer.MipLevel);
                        img.AddMipLevel(frame);
                    }

                    return img;
                }
            case ImageFileType.Tiff:
                {
                    if (image.Frames != null)
                    {
                        return image.Frames;
                    }

                    var frames = new List<byte[]>((int)image.Description.ArraySize);
                    for (var slice = 0; slice < image.Description.ArraySize; slice++)
                    {
                        frames.Add(image.GetPixelBuffer(slice, 0).GetPixels<byte>());
                    }

                    return new FrameBitmap(image.Description.Width, image.Description.Height,
                        image.Description.Format, frames);
                }
        }

        return null;
    }
}