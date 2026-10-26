using System;
using System.Buffers.Binary;
using System.IO;
using Adamantium.Vulkan.Core;

namespace Adamantium.Imaging.Dds;

/// <summary>Writes pictures as DDS: every layer, mip level and depth slice an <see cref="Image"/> or a DDS has, the
/// frames of any other picture as layers, of any size in bytes; Direct3D's limits hold - at most 2048 layers and 2048
/// slices - as they do for <see cref="DdsImage"/>. Formats the older header has - 8-bit RGBA and BGRA, 24-bit RGB
/// and BGR, 5-6-5, BC1 to BC3 - are written in it, so readers of only it read them; any other in the DX10 header. DDS
/// has no code for BC1 without alpha, so it is written as BC1 and read back with alpha, as every reader of DDS takes it;
/// 24-bit RGB is read back widened, as <see cref="DdsImage"/> reads it.</summary>
public static class DdsEncoder
{
    private const int Chunk = 1 << 20;

    /// <summary>Writes <paramref name="bitmap"/> to <paramref name="output"/>; a format DXGI has no number for throws
    /// <see cref="NotSupportedException"/>.</summary>
    public static void Encode(IRawBitmap bitmap, Stream output)
    {
        using var made = bitmap is ImageBitmap ? null : Image.From(bitmap);
        var image = (bitmap as ImageBitmap)?.Image ?? made;
        var description = image.Description;
        var format = description.Format;
        var cube = description.Dimension == TextureDimension.TextureCube;
        var volume = description.Dimension == TextureDimension.Texture3D;
        var legacy = description.Dimension != TextureDimension.Texture1D && (cube ? description.ArraySize == 6 : description.ArraySize == 1)
            ? DdsFormats.Legacy(format)
            : null;
        var dxgi = DdsFormats.Dxgi(format);
        if (legacy == null && dxgi == 0)
        {
            throw new NotSupportedException($"DDS is not written of {format} pixels.");
        }

        if (description.ArraySize > DdsFormats.MostLayers || description.Depth > DdsFormats.MostLayers)
        {
            throw new NotSupportedException(
                $"DDS is not written of {description.ArraySize} layers and {description.Depth} slices: Direct3D's " +
                $"textures have at most {DdsFormats.MostLayers} of each.");
        }

        var header = new byte[legacy == null ? 148 : 128];
        void Put(int at, uint value) => BinaryPrimitives.WriteUInt32LittleEndian(header.AsSpan(at), value);
        var compressed = TexelBlocks.Of(format).Width > 1;
        var (row, rows) = TexelBlocks.Rows(format, description.Width, description.Height);
        var flags = 0x1u | 0x2 | 0x4 | 0x1000 | (compressed ? 0x80000u : 0x8u)
                    | (description.MipLevels > 1 ? 0x20000u : 0) | (volume ? 0x800000u : 0);
        Put(0, 0x20534444);
        Put(4, 124);
        Put(8, flags);
        Put(12, description.Height);
        Put(16, description.Width);
        Put(20, (uint)(compressed ? row * rows : row));
        Put(24, volume ? description.Depth : 0);
        Put(28, description.MipLevels);
        Put(76, 32);
        if (legacy is var (pixelFlags, fourCC, bits, red, green, blue, alpha))
        {
            (header[80], header[81], header[82], header[83]) =
                ((byte)pixelFlags, (byte)(pixelFlags >> 8), (byte)(pixelFlags >> 16), (byte)(pixelFlags >> 24));
            Put(84, fourCC);
            Put(88, bits);
            Put(92, red);
            Put(96, green);
            Put(100, blue);
            Put(104, alpha);
        }
        else
        {
            Put(80, DdsFormats.FourCCFlag);
            Put(84, DdsFormats.Dx10);
            Put(128, dxgi);
            Put(132, description.Dimension switch
            {
                TextureDimension.Texture1D => 2u,
                TextureDimension.Texture3D => 4u,
                _ => 3u
            });
            Put(136, cube ? 0x4u : 0);
            Put(140, cube ? description.ArraySize / 6 : volume ? 1 : description.ArraySize);
        }

        Put(108, 0x1000u | (description.MipLevels > 1 || cube || volume ? 0x8u : 0)
                 | (description.MipLevels > 1 ? 0x400000u : 0));
        Put(112, cube ? 0x200u | 0xFC00 : volume ? 0x200000u : 0);
        output.Write(header);
        WriteTexels(image, legacy == null ? DdsFormats.Swapped(dxgi) : null, output);
        output.Flush();
    }

    private static unsafe void WriteTexels(Image image, (int First, int Second)? swapped, Stream output)
    {
        var source = (byte*)image.DataPointer;
        var total = (long)image.TotalSizeInBytes;
        var buffer = swapped == null ? null : new byte[Math.Min(Chunk, total)];
        for (long done = 0; done < total;)
        {
            var length = (int)Math.Min(Chunk, total - done);
            var part = new ReadOnlySpan<byte>(source + done, length);
            if (swapped is var (first, second))
            {
                part.CopyTo(buffer);
                for (var i = 0; i + 3 < length; i += 4)
                {
                    (buffer[i + first], buffer[i + second]) = (buffer[i + second], buffer[i + first]);
                }

                part = buffer.AsSpan(0, length);
            }

            output.Write(part);
            done += length;
        }
    }
}
