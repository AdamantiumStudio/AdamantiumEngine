using System;
using System.Buffers.Binary;
using System.Runtime.InteropServices;

namespace Adamantium.Imaging.Png;

/// <summary>The rows of a picture's frames as the PNG encoder reads them: RGBA of 8 bits a pixel packed in an int, red
/// lowest, or of 16 bits packed in a long - whatever the order and number of the picture's own channels.</summary>
internal sealed class PngSourceRows(IRawBitmap image, int width, int height, int channels, bool wide, bool bgr)
{
    public int Width => width;

    public int Height => height;

    /// <summary>Whether the samples are 16 bits.</summary>
    public bool Wide => wide;

    /// <summary>Frame <paramref name="index"/>'s pixels, as the picture holds them; asked for once a pass, since an
    /// animation's frames are composed when asked for.</summary>
    public byte[] Frame(int index) => image.GetRawPixels((uint)index);

    /// <summary>Row <paramref name="y"/> of 8-bit <paramref name="pixels"/>: the pixels themselves when they are RGBA
    /// already, else made in <paramref name="row"/>.</summary>
    public ReadOnlySpan<uint> ReadRow(byte[] pixels, int y, Span<uint> row)
    {
        var from = pixels.AsSpan((int)((long)y * width * channels), width * channels);
        if (channels == 4 && !bgr && BitConverter.IsLittleEndian)
        {
            return MemoryMarshal.Cast<byte, uint>(from);
        }

        switch (channels)
        {
            case 4:
            {
                var source = MemoryMarshal.Cast<byte, uint>(from);
                for (var x = 0; x < width; x++)
                {
                    var pixel = source[x];
                    row[x] = (pixel & 0xFF00FF00) | ((pixel >> 16) & 0xFF) | ((pixel & 0xFF) << 16);
                }

                break;
            }
            case 3:
            {
                var (red, blue) = bgr ? (2, 0) : (0, 2);
                for (var x = 0; x < width; x++)
                {
                    row[x] = from[3 * x + red] | ((uint)from[3 * x + 1] << 8) | ((uint)from[3 * x + blue] << 16)
                             | 0xFF000000;
                }

                break;
            }
            default:
                for (var x = 0; x < width; x++)
                {
                    row[x] = from[x] * 0x10101u | 0xFF000000;
                }

                break;
        }

        return row;
    }

    /// <summary>Row <paramref name="y"/> of 16-bit <paramref name="pixels"/>, little-endian samples, into
    /// <paramref name="row"/>.</summary>
    public void ReadRow(byte[] pixels, int y, Span<ulong> row)
    {
        var from = MemoryMarshal.Cast<byte, ushort>(pixels.AsSpan((int)((long)y * width * channels * 2),
            width * channels * 2));
        for (var x = 0; x < width; x++)
        {
            ulong r, g, b, a = ushort.MaxValue;
            if (channels == 1)
            {
                r = g = b = Sample(from[x]);
            }
            else
            {
                var at = x * channels;
                (r, g, b) = (Sample(from[at]), Sample(from[at + 1]), Sample(from[at + 2]));
                if (bgr)
                {
                    (r, b) = (b, r);
                }

                if (channels == 4)
                {
                    a = Sample(from[at + 3]);
                }
            }

            row[x] = r | (g << 16) | (b << 32) | (a << 48);
        }
    }

    private static ulong Sample(ushort value) =>
        BitConverter.IsLittleEndian ? value : BinaryPrimitives.ReverseEndianness(value);
}
