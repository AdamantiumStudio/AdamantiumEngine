using System;
using System.Buffers.Binary;
using System.IO;
using System.Numerics;

namespace Adamantium.Imaging.Bmp;

/// <summary>A BMP picture (Microsoft, "Bitmap Storage": BITMAPCOREHEADER, BITMAPINFOHEADER and its V2 to V5) as RGBA of
/// 8 bits a channel: 1, 2, 4 and 8 bits through a color table, run-length encoded (RLE4, RLE8) or not; 16 and 32 bits
/// by their channel masks - 5-5-5 and 8-8-8 when they give none; 24 bits; rows bottom up or, for a negative height,
/// top down. Alpha only where a mask gives it; a file shorter than its pixels is refused.</summary>
public sealed class BmpImage : IRawBitmap
{
    private readonly byte[] _pixels;
    private FrameData _frame;

    private BmpImage(uint width, uint height, byte[] pixels)
    {
        (Width, Height, _pixels) = (width, height, pixels);
    }

    public uint Width { get; }

    public uint Height { get; }

    public SurfaceFormat PixelFormat => SurfaceFormat.R8G8B8A8.UNorm;

    public bool IsMultiFrame => false;

    public bool HasMipLevels => false;

    public uint MipLevelsCount => 0;

    public uint NumberOfReplays => 0;

    public uint FramesCount => 1;

    public byte[] GetRawPixels(uint frameIndex) => _pixels;

    public FrameData GetMipLevelData(uint mipLevel) => GetFrameData(0);

    public ImageDescription GetImageDescription() => ImageDescription.Default2D(Width, Height, PixelFormat);

    public FrameData GetFrameData(uint frameIndex) => _frame ??= new FrameData(_pixels, GetImageDescription());

    /// <summary>Reads the BMP in <paramref name="file"/>.</summary>
    public static BmpImage Read(ReadOnlySpan<byte> file)
    {
        if (file.Length < 26 || file[0] != 'B' || file[1] != 'M')
        {
            throw new InvalidDataException("Not a BMP file.");
        }

        var pixelsAt = BinaryPrimitives.ReadUInt32LittleEndian(file[10..]);
        var header = file[14..];
        var headerSize = (int)BinaryPrimitives.ReadUInt32LittleEndian(header);
        int width, height, bits;
        uint compression = 0, colorsUsed = 0;
        if (headerSize == 12)
        {
            width = BinaryPrimitives.ReadUInt16LittleEndian(header[4..]);
            height = BinaryPrimitives.ReadInt16LittleEndian(header[6..]);
            bits = BinaryPrimitives.ReadUInt16LittleEndian(header[10..]);
        }
        else if (headerSize >= 40 && header.Length >= headerSize)
        {
            width = BinaryPrimitives.ReadInt32LittleEndian(header[4..]);
            height = BinaryPrimitives.ReadInt32LittleEndian(header[8..]);
            bits = BinaryPrimitives.ReadUInt16LittleEndian(header[14..]);
            compression = BinaryPrimitives.ReadUInt32LittleEndian(header[16..]);
            colorsUsed = BinaryPrimitives.ReadUInt32LittleEndian(header[32..]);
        }
        else
        {
            throw new InvalidDataException($"A BMP header of {headerSize} bytes.");
        }

        var topDown = height < 0;
        height = Math.Abs(height);
        if (width <= 0 || height == 0)
        {
            throw new InvalidDataException($"A BMP of {width}×{height}.");
        }

        if (compression is 4 or 5)
        {
            throw new NotSupportedException("A BMP holding a JPEG or PNG is not read.");
        }

        uint red = 0, green = 0, blue = 0, alpha = 0;
        var tableAt = 14 + headerSize;
        if (compression is 3 or 6)
        {
            if (headerSize < 52 && tableAt + (compression == 6 ? 16 : 12) > file.Length)
            {
                throw new InvalidDataException("A BMP file ends in its channel masks.");
            }

            var masks = headerSize >= 52 ? header[40..] : file[tableAt..];
            (red, green, blue) = (BinaryPrimitives.ReadUInt32LittleEndian(masks),
                BinaryPrimitives.ReadUInt32LittleEndian(masks[4..]), BinaryPrimitives.ReadUInt32LittleEndian(masks[8..]));
            if (compression == 6 || headerSize >= 56)
            {
                alpha = BinaryPrimitives.ReadUInt32LittleEndian(masks[12..]);
            }

            if (headerSize < 52)
            {
                tableAt += compression == 6 ? 16 : 12;
            }
        }
        else if (bits == 16)
        {
            (red, green, blue) = (0x7C00, 0x3E0, 0x1F);
        }
        else if (bits == 32)
        {
            (red, green, blue) = (0xFF0000, 0xFF00, 0xFF);
        }

        if (bits is not (1 or 2 or 4 or 8 or 16 or 24 or 32)
            || compression is 1 && bits != 8 || compression is 2 && bits != 4
            || compression is 3 or 6 && bits is not (16 or 32))
        {
            throw new NotSupportedException($"BMP pixels of {bits} bits and compression {compression} are not read.");
        }

        if (compression > 6)
        {
            throw new NotSupportedException($"BMP compression {compression} is not read.");
        }

        var palette = Array.Empty<uint>();
        if (bits <= 8)
        {
            var entries = colorsUsed > 0 ? (int)Math.Min(colorsUsed, 256u) : 1 << bits;
            var entryBytes = headerSize == 12 ? 3 : 4;
            palette = new uint[256];
            Array.Fill(palette, 0xFF000000u);
            for (var i = 0; i < entries && tableAt + (i + 1) * entryBytes <= file.Length; i++)
            {
                var entry = file[(tableAt + i * entryBytes)..];
                palette[i] = entry[2] | (uint)entry[1] << 8 | (uint)entry[0] << 16 | 0xFF000000u;
            }
        }

        if ((long)width * height * 4 > Array.MaxLength)
        {
            throw new NotSupportedException($"A BMP of {width}×{height} is larger than an array holds.");
        }

        if (pixelsAt > file.Length)
        {
            throw new InvalidDataException("A BMP's pixels start past its end.");
        }

        var data = file[(int)pixelsAt..];
        var pixels = new byte[(long)width * height * 4];
        var target = System.Runtime.InteropServices.MemoryMarshal.Cast<byte, uint>(pixels.AsSpan());
        if (compression is 1 or 2)
        {
            Expand(data, target, width, height, topDown, bits, palette);
        }
        else
        {
            var stride = ((long)width * bits + 31) / 32 * 4;
            if (stride * height > data.Length)
            {
                throw new InvalidDataException($"A BMP file ends before its {width}×{height} pixels do.");
            }

            for (var row = 0; row < height; row++)
            {
                var line = data.Slice((int)(row * stride), (int)stride);
                var to = target.Slice((topDown ? row : height - 1 - row) * width, width);
                Row(line, to, bits, palette, red, green, blue, alpha);
            }
        }

        return new BmpImage((uint)width, (uint)height, pixels);
    }

    private static void Row(ReadOnlySpan<byte> line, Span<uint> to, int bits, uint[] palette, uint red, uint green,
        uint blue, uint alpha)
    {
        for (var x = 0; x < to.Length; x++)
        {
            to[x] = bits switch
            {
                <= 8 => palette[line[x * bits / 8] >> (8 - bits - x * bits % 8) & ((1 << bits) - 1)],
                24 => line[x * 3 + 2] | (uint)line[x * 3 + 1] << 8 | (uint)line[x * 3] << 16 | 0xFF000000u,
                16 => Masked(BinaryPrimitives.ReadUInt16LittleEndian(line[(x * 2)..]), red, green, blue, alpha),
                _ => Masked(BinaryPrimitives.ReadUInt32LittleEndian(line[(x * 4)..]), red, green, blue, alpha)
            };
        }
    }

    private static uint Masked(uint value, uint red, uint green, uint blue, uint alpha) =>
        Channel(value, red) | Channel(value, green) << 8 | Channel(value, blue) << 16
        | (alpha == 0 ? 0xFF000000u : Channel(value, alpha) << 24);

    private static uint Channel(uint value, uint mask)
    {
        if (mask == 0)
        {
            return 0;
        }

        var shift = BitOperations.TrailingZeroCount(mask);
        var width = BitOperations.PopCount(mask);
        var raw = (value & mask) >> shift;
        var top = (1ul << width) - 1;
        return (uint)((raw * 255 + top / 2) / top);
    }

    private static void Expand(ReadOnlySpan<byte> data, Span<uint> target, int width, int height, bool topDown, int bits,
        uint[] palette)
    {
        int x = 0, row = 0, at = 0;
        while (at + 1 < data.Length && row < height)
        {
            int count = data[at], value = data[at + 1];
            at += 2;
            if (count > 0)
            {
                for (var i = 0; i < count; i++)
                {
                    Put(target, ref x, row, width, height, topDown,
                        palette[bits == 8 ? value : i % 2 == 0 ? value >> 4 : value & 15]);
                }

                continue;
            }

            switch (value)
            {
                case 0:
                    (x, row) = (0, row + 1);
                    break;
                case 1:
                    return;
                case 2:
                    if (at + 1 >= data.Length)
                    {
                        return;
                    }

                    (x, row) = (x + data[at], row + data[at + 1]);
                    at += 2;
                    break;
                default:
                {
                    var bytes = bits == 8 ? value : (value + 1) / 2;
                    if (at + bytes > data.Length)
                    {
                        return;
                    }

                    for (var i = 0; i < value; i++)
                    {
                        Put(target, ref x, row, width, height, topDown,
                            palette[bits == 8 ? data[at + i] : i % 2 == 0 ? data[at + i / 2] >> 4 : data[at + i / 2] & 15]);
                    }

                    at += bytes + (bytes & 1);
                    break;
                }
            }
        }
    }

    private static void Put(Span<uint> target, ref int x, int row, int width, int height, bool topDown, uint color)
    {
        if (x < width && row < height)
        {
            target[(topDown ? row : height - 1 - row) * width + x] = color;
        }

        x++;
    }
}
