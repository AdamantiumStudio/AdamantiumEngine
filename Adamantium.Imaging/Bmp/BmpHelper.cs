using System;
using System.IO;
using System.Runtime.InteropServices;
using Adamantium.Core;
using Adamantium.Mathematics;
using Adamantium.Vulkan.Core;

namespace Adamantium.Imaging.Bmp
{
    public class BmpHelper
    {
        private const UInt16 fileType = 0x4D42;

        public static IRawBitmap LoadFromMemory(IntPtr pSource, ulong size)
        {
            if (!DecodeBitmapHeader(pSource, size, out var description, out var dataOffset, out var compression))
            {
                throw new ArgumentException("Given file is not a BMP file");
            }

            // Rows are padded to 4 bytes: the file must hold every padded row.
            var row = (ulong)description.Width * (ulong)description.Format.SizeOfInBytes();
            if (row > int.MaxValue - 3 || (ulong)dataOffset + (row + 3) / 4 * 4 * description.Height > size)
            {
                throw new InvalidDataException("The BMP file ends before its pixels do.");
            }

            return CreateImageFromBitmap(pSource, dataOffset, description);
        }
        
        private static bool DecodeBitmapHeader(IntPtr headerPtr, ulong size, out ImageDescription description, out int dataOffset, out BitmapCompressionMode compressionMode)
        {
            dataOffset = 0;
            compressionMode = BitmapCompressionMode.RGB;
            description = new ImageDescription();
            if (headerPtr == IntPtr.Zero)
                throw new ArgumentException("Pointer to Bitmap header cannot be null", nameof(headerPtr));

            if (size < (ulong)Utilities.SizeOf<BitmapFileHeader>())
                return false;

            var fileHeader = Utilities.Read<BitmapFileHeader>(headerPtr);

            if (fileHeader.fileType != fileType)
            {
                return false;
            }

            var infoHeader = Utilities.Read<BitmapInfoHeader>(IntPtr.Add(headerPtr, Marshal.SizeOf<BitmapFileHeader>()));

            // The BMPColorHeader is used only for transparent images
            // The file's BGR comes out RGB (see CreateImageFromBitmap), and is said to be RGB.
            if (infoHeader.bitCount == 32)
            {
                description.Format = Format.R8G8B8A8_UNORM;
                // Check if the file has bit mask color information
                if (infoHeader.size >= (Marshal.SizeOf<BitmapInfoHeader>() + Marshal.SizeOf<BitmapColorHeader>()))
                {
                    var colorHeader = Utilities.Read<BitmapColorHeader>(IntPtr.Add(headerPtr, Marshal.SizeOf<BitmapInfoHeader>() + Marshal.SizeOf<BitmapFileHeader>()));
                    // Check if the pixel data is stored as BGRA and if the color space type is sRGB
                    CheckColorHeader(colorHeader);
                }
            }
            else if(infoHeader.bitCount == 24)
            {
                description.Format = Format.R8G8B8_UNORM;
            }
            else if (infoHeader.bitCount == 16)
            {
                // BI_RGB pixels of 16 bits are 5-5-5; BI_BITFIELDS puts the masks after the 40-byte header, 5-6-5 by
                // its green.
                var masks = Marshal.SizeOf<BitmapFileHeader>() + Marshal.SizeOf<BitmapInfoHeader>();
                var green = infoHeader.compression == BitmapCompressionMode.BITFIELDS && size >= (ulong)masks + 12
                    ? (uint)Marshal.ReadInt32(headerPtr, masks + 4)
                    : ColorMasks.RGB555.GreenMask;
                description.Format = green == ColorMasks.RGB565.GreenMask
                    ? Format.B5G6R5_UNORM_PACK16
                    : Format.B5G5R5A1_UNORM_PACK16;
            }
            else
            {
                throw new NotSupportedException($"BMP pixels of {infoHeader.bitCount} bits are not read.");
            }

            if (infoHeader.compression is not (BitmapCompressionMode.RGB or BitmapCompressionMode.BITFIELDS))
            {
                throw new NotSupportedException($"BMP compressed as {infoHeader.compression} is not read.");
            }

            if (infoHeader.height < 0)
            {
                throw new NotSupportedException("A BMP stored top-down is not read.");
            }

            if (infoHeader.width <= 0 || infoHeader.height == 0)
            {
                throw new InvalidDataException($"A BMP of {infoHeader.width}×{infoHeader.height} pixels.");
            }

            description.MipLevels = 1;
            description.Width = (uint)infoHeader.width;
            description.Height = (uint)infoHeader.height;
            description.Depth = 1;
            description.ArraySize = 1;
            description.Dimension = TextureDimension.Texture2D;

            dataOffset = (int)fileHeader.offsetData;
            compressionMode = infoHeader.compression;

            return true;
        }

        private static unsafe IRawBitmap CreateImageFromBitmap(IntPtr pSource, int offset, ImageDescription description)
        {
            var originalFormat = description.Format;
            var realFormatSize = description.Format.SizeOfInBytes();
            if (realFormatSize == 2)
            {
                // Pixels of 16 bits come out as RGB of 8.
                description.Format = Format.R8G8B8_UNORM;
            }

            var sizeInBytes = description.Format.SizeOfInBytes();
            var bufferSize = description.Width * description.Height * sizeInBytes;
            byte[] buffer = new byte[bufferSize];
            var rowStride = (int)(description.Width * realFormatSize);
            var alignedRowStride = AlignStride(rowStride);
            int rowStrideDiff = alignedRowStride - rowStride;
            var conversionFlags = ConversionFlags.None;
            if (realFormatSize == 2)
            {
                conversionFlags = originalFormat == Format.B5G5R5A1_UNORM_PACK16 ? ConversionFlags.RGB555 : ConversionFlags.RGB565;
            }

            using (var stream = new UnmanagedMemoryStream((byte*)pSource, alignedRowStride * description.Height + offset))
            {
                int bufferOffset = 0;
                var streamOffset = 0;
                stream.Seek(offset, SeekOrigin.Begin);
                for (int i = 0; i < bufferSize; i += sizeInBytes)
                {
                    stream.Read(buffer, bufferOffset, realFormatSize);
                    bufferOffset += realFormatSize;
                    streamOffset += realFormatSize;
                    var origin = bufferOffset - realFormatSize;

                    if (realFormatSize == 2)
                    {
                        ushort rg16 = (ushort)((buffer[origin]) | (buffer[origin + 1] << 8));
                        byte r = 0;
                        byte g = 0;
                        byte b = 0;
                        if (conversionFlags == ConversionFlags.RGB565)
                        {
                            b = (byte)(rg16 & ColorMasks.RGB565.BlueMask);
                            g = (byte)((rg16 & ColorMasks.RGB565.GreenMask) >> 5);
                            r = (byte)((rg16 & ColorMasks.RGB565.RedMask) >> 11);
                        }
                        else if (conversionFlags == ConversionFlags.RGB555)
                        {
                            b = (byte)(rg16 & ColorMasks.RGB555.BlueMask);
                            g = (byte)((rg16 & ColorMasks.RGB555.GreenMask) >> 5);
                            r = (byte)((rg16 & ColorMasks.RGB555.RedMask) >> 10);
                        }

                        r = (byte)Math.Round(255.0d / 31 * r);
                        g = (byte)Math.Round(255.0d / (conversionFlags == ConversionFlags.RGB565 ? 63 : 31) * g);
                        b = (byte)Math.Round(255.0d / 31 * b);

                        buffer[origin] = r;
                        buffer[origin + 1] = g;
                        buffer[origin + 2] = b;
                    }
                    else if (realFormatSize > 2)
                    {
                        Utilities.Swap(ref buffer[origin], ref buffer[origin + 2]);
                    }

                    if (realFormatSize < sizeInBytes)
                    {
                        var bytesDiff = sizeInBytes - realFormatSize;
                        bufferOffset += bytesDiff;
                    }

                    if (rowStride % 4 != 0 && streamOffset % rowStride == 0)
                    {
                        var tmpBuffer = new byte[rowStrideDiff];
                        stream.Read(tmpBuffer, 0, rowStrideDiff);
                    }
                }
            }

            var bmpImage = new BmpImage(description);
            buffer = ImageHelper.FlipBuffer(
                buffer, 
                description.Width, 
                description.Height, 
                description.Format.SizeOfInBytes(),
                FlipBufferOptions.FlipVertically);

            if (description.Format.SizeOfInBytes() == 4)
            {
                ImageHelper.SetAlpha(buffer, 255);
            }

            bmpImage.PixelData = buffer;
            return bmpImage;
        }

        private static int AlignStride(int stride, int align = 4)
        {
            var newStride = stride;
            while(newStride % align != 0)
            {
                newStride += 1;
            }

            return newStride;
        }

        private static void CheckColorHeader(BitmapColorHeader colorHeader)
        {
            if (ColorMasks.R8G8B8A8.RedMask != colorHeader.redMask ||
                ColorMasks.R8G8B8A8.BlueMask != colorHeader.blueMask ||
                ColorMasks.R8G8B8A8.GreenMask != colorHeader.greenMask ||
                ColorMasks.R8G8B8A8.AlphaMask != colorHeader.alphaMask)
            {
                throw new ArgumentOutOfRangeException("Unexpected color mask format! The program expects the pixel data to be in the BGRA format");
            }
            if (ColorMasks.R8G8B8A8.ColorSpaceType != colorHeader.colorSpaceType)
            {
                throw new ArgumentOutOfRangeException("Unexpected color space type! The program expects sRGB values");
            }
        }

        public static void SaveToStream(IRawBitmap img, Stream imageStream)
        {
            // BMP is written from 8-bit RGB(A) or BGR(A): other pixels are brought to RGBA of 8 bits first.
            if ((Format)img.PixelFormat is not (Format.R8G8B8A8_UNORM or Format.R8G8B8A8_SRGB or Format.B8G8R8A8_UNORM
                    or Format.B8G8R8A8_SRGB or Format.R8G8B8_UNORM or Format.R8G8B8_SRGB or Format.B8G8R8_UNORM
                    or Format.B8G8R8_SRGB))
            {
                var rgba = img.GetRgba8Pixels()
                           ?? throw new NotSupportedException($"BMP is not written from {(Format)img.PixelFormat} pixels.");
                img = new FrameBitmap(img.Width, img.Height, SurfaceFormat.R8G8B8A8.UNorm, [rgba]);
            }

            SaveToBitmapStream(img, imageStream);
        }

        private static unsafe void SaveToBitmapStream(IRawBitmap rawBitmap, Stream imageStream)
        {
            imageStream.Seek(0, SeekOrigin.Begin);
            var frameData = rawBitmap.GetFrameData(0);

            uint offsetData = (uint)(Marshal.SizeOf<BitmapFileHeader>() + Marshal.SizeOf<BitmapInfoHeader>()); //+ Marshal.SizeOf<BitmapColorHeader>());
            uint fileSize = offsetData;

            if (frameData.Description.RowStride % 4 == 0)
            {
                fileSize += (uint)frameData.BufferSize;
            }
            else
            {
                // Each row is padded to 4 bytes.
                var size = AlignStride((int)(rawBitmap.Width * rawBitmap.PixelFormat.SizeInBytes)) * rawBitmap.Height;
                fileSize += (uint)size;
            }

            var fileHeader = new BitmapFileHeader();
            fileHeader.fileType = fileType;
            fileHeader.fileSize = fileSize;
            fileHeader.offsetData = offsetData;

            var infoHeader = new BitmapInfoHeader();
            infoHeader.bitCount = (ushort)rawBitmap.PixelFormat.SizeInBits;
            infoHeader.width = (int)rawBitmap.Width;
            infoHeader.height = (int)rawBitmap.Height;
            infoHeader.planes = 1;
            infoHeader.size = (uint)Marshal.SizeOf<BitmapInfoHeader>();

            var buffer = new byte[fileSize];
            IntPtr fileHeaderMemoryPtr = Utilities.AllocateMemory(Marshal.SizeOf<BitmapFileHeader>(), 2);
            Marshal.StructureToPtr(fileHeader, fileHeaderMemoryPtr, true);
            Utilities.Read(fileHeaderMemoryPtr, buffer, 0, Marshal.SizeOf<BitmapFileHeader>());
            Utilities.FreeMemory(fileHeaderMemoryPtr);

            IntPtr infoHeaderMemoryPtr = Utilities.AllocateMemory(Marshal.SizeOf<BitmapInfoHeader>());
            Marshal.StructureToPtr(infoHeader, infoHeaderMemoryPtr, true);
            Utilities.Read(infoHeaderMemoryPtr, buffer, Marshal.SizeOf<BitmapFileHeader>(), Marshal.SizeOf<BitmapInfoHeader>());
            Utilities.FreeMemory(infoHeaderMemoryPtr);

            var bufferOffset = Marshal.SizeOf<BitmapFileHeader>() + Marshal.SizeOf<BitmapInfoHeader>();

            var colors = ImageHelper.FlipBuffer(
                frameData.RawPixels, 
                frameData.Description.Width,
                frameData.Description.Height,
                frameData.Description.Format.SizeOfInBytes(),
                FlipBufferOptions.FlipVertically);
            // BMP keeps BGR: pixels said to be RGB are swapped into it, BGR ones go as they are.
            var pixelSize = rawBitmap.PixelFormat.SizeInBytes;
            var bgr = (Format)rawBitmap.PixelFormat is Format.B8G8R8A8_UNORM or Format.B8G8R8A8_SRGB
                or Format.B8G8R8_UNORM or Format.B8G8R8_SRGB;
            for (int i = 0; !bgr && i < colors.Length; i+=pixelSize)
            {
                Utilities.Swap(ref colors[i], ref colors[i+2]);
            }
            
            imageStream.Write(buffer, 0, (int)offsetData);
            var handle = GCHandle.Alloc(colors, GCHandleType.Pinned);
            Utilities.Read(handle.AddrOfPinnedObject(), buffer, bufferOffset, colors.Length);
            handle.Free();
            
            if (frameData.Description.RowStride % 4 == 0)
            {
                imageStream.Write(buffer, (int)offsetData, colors.Length);
            }
            else
            {
                int rowStride = (int)(frameData.Description.Width * frameData.Description.Format.SizeOfInBytes());
                var alignDiff = AlignStride(rowStride) - rowStride;
                for (int i = 0; i < frameData.Description.Height; ++i)
                {
                    imageStream.Write(buffer, (int)offsetData, rowStride);
                    for (int k = 0; k < alignDiff; ++k)
                    {
                        imageStream.WriteByte(0);
                    }
                    offsetData += (uint)rowStride;
                }
            }
        }
    };
}
