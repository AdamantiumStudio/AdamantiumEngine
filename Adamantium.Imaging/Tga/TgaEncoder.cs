using System;
using System.IO;
using System.Runtime.InteropServices;
using Adamantium.Core;
using Adamantium.Vulkan.Core;

namespace Adamantium.Imaging.Tga;

public class TgaEncoder
{
    internal static bool EncodeTgaHeader(ImageDescription description, out TgaHeader header, ref TgaConversionFlags flags)
    {
        header = new TgaHeader();
        if (description.Width > 0xFFFF || description.Height > 0xFFFF)
        {
            return false;
        }

        header.Width = (ushort)description.Width;
        header.Height = (ushort)description.Height;

        switch (description.Format)
        {
            case Format.R8G8B8A8_UNORM:
            case Format.R8G8B8A8_SRGB:
                header.ImageType = (byte)TgaImageType.TrueColor;
                header.BitsPerPixel = 32;
                header.Descriptor = (byte)TgaDescriptorFlags.InvertY | 8;
                flags |= TgaConversionFlags.Swizzle;
                break;
            case Format.B8G8R8A8_UNORM:
            case Format.B8G8R8A8_SRGB:
                // TGA keeps BGRA: these go as they are.
                header.ImageType = (byte)TgaImageType.TrueColor;
                header.BitsPerPixel = 32;
                header.Descriptor = (byte)TgaDescriptorFlags.InvertY | 8;
                break;
            case Format.B8G8R8_UNORM:
            case Format.B8G8R8_SRGB:
                header.ImageType = (byte)TgaImageType.TrueColor;
                header.BitsPerPixel = 24;
                header.Descriptor = (byte)TgaDescriptorFlags.InvertY;
                flags |= TgaConversionFlags.Format888;
                break;
            case Format.R8_UNORM:
                header.ImageType = (byte)TgaImageType.BlackAndWhite;
                header.BitsPerPixel = 8;
                header.Descriptor = (byte)TgaDescriptorFlags.InvertY;
                break;
            case Format.B5G5R5A1_UNORM_PACK16:
                header.ImageType = (byte)TgaImageType.TrueColor;
                header.BitsPerPixel = 16;
                header.Descriptor = (byte)TgaDescriptorFlags.InvertY | 1;
                break;

            default:
                return false;
        }

        return true;
    }
    
    public static unsafe void SaveToTgaStream(IRawBitmap rawBitmap, Stream imageStream)
        {
            // Pixels TGA has no header for - RGB, 16 bits, gray of 16 - are brought to RGBA of 8 bits first.
            var probe = TgaConversionFlags.None;
            if (!EncodeTgaHeader(rawBitmap.GetImageDescription(), out _, ref probe)
                && rawBitmap.GetRgba8Pixels() is { } rgba)
            {
                rawBitmap = new FrameBitmap(rawBitmap.Width, rawBitmap.Height, SurfaceFormat.R8G8B8A8.UNorm, [rgba]);
            }

            TgaHeader header;
            ImageDescription description = rawBitmap.GetImageDescription();
            TgaConversionFlags flags = TgaConversionFlags.None;
            if (!EncodeTgaHeader(description, out header, ref flags))
            {
                throw new NotSupportedException(
                    $"TGA is not written from {(Format)description.Format} pixels of {description.Width}×{description.Height}.");
            }

            int rowPitch, slicePitch;
            if (flags.HasFlag(TgaConversionFlags.Format888))
            {
                rowPitch = (int)description.Width * 3;
                slicePitch = (int)description.Height * rowPitch;
            }
            else
            {
                int widthCount, heightCount;
                Image.ComputePitch(description.Format, (int)description.Width, (int)description.Height, out rowPitch, out slicePitch, out widthCount, out heightCount);
            }

            var pixelBuffer = rawBitmap.GetRawPixels(0);
            var handle = GCHandle.Alloc(pixelBuffer, GCHandleType.Pinned);
            IntPtr pSource = handle.AddrOfPinnedObject();
            var sPitch = description.Width * description.Format.SizeOfInBytes();

            int headerSize = Utilities.SizeOf<TgaHeader>();
            var buffer = new byte[Math.Max(slicePitch, headerSize)];
            IntPtr memory = Utilities.AllocateMemory(headerSize, 1);
            Marshal.StructureToPtr(header, memory, true);
            Utilities.Read(memory, buffer, 0, headerSize);
            imageStream.Write(buffer, 0, headerSize);
            Utilities.FreeMemory(memory);

            var size = description.Width * description.Height * description.Format.SizeOfInBytes();
            var dPtr = Utilities.AllocateMemory((int)size);
            var finalPixelBufferPtr = dPtr;

            for (int y = 0; y < description.Height; ++y)
            {
                if (flags.HasFlag(TgaConversionFlags.Format888))
                {
                    ImageHelper.CopyScanline(dPtr, pSource, rowPitch);
                }
                else if (flags.HasFlag(TgaConversionFlags.Swizzle))
                {
                    ImageHelper.SwizzleScanline(dPtr, rowPitch, pSource, (int)sPitch,
                       description.Format, ImageHelper.ScanlineFlags.None);
                }
                else
                {
                    ImageHelper.CopyScanline(dPtr, rowPitch, pSource, (int)sPitch,
                       description.Format, ImageHelper.ScanlineFlags.None);
                }

                dPtr = (IntPtr)((byte*)dPtr + rowPitch);
                pSource = (IntPtr)((byte*)pSource + sPitch);
            }

            //renew pointer address as far as it was moved by code above
            dPtr = finalPixelBufferPtr;
            
            handle.Free();

            Utilities.Read(dPtr, buffer, 0, slicePitch);
            imageStream.Write(buffer, 0, slicePitch);
            Utilities.FreeMemory(dPtr);
        }
}