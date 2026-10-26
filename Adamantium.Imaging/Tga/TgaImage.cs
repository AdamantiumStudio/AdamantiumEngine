using System;
using System.Buffers.Binary;
using System.IO;
using System.Text;

namespace Adamantium.Imaging.Tga;

/// <summary>A TGA picture (Truevision TGA 2.0, and the original format it extends) as RGBA of 8 bits a channel:
/// color-mapped, true-color and gray, plain or run-length encoded, of 8, 15, 16, 24 and 32 bits, in any of the four
/// orientations. Alpha is read as the image descriptor counts its bits and the extension area says what they are -
/// none, ignored, or premultiplied; a file shorter than its pixels is refused.</summary>
public sealed class TgaImage : IRawBitmap
{
    private readonly byte[] _pixels;
    private FrameData _frame;

    private TgaImage(uint width, uint height, byte[] pixels, string id)
    {
        (Width, Height, _pixels, Id) = (width, height, pixels, id);
    }

    public uint Width { get; }

    public uint Height { get; }

    public SurfaceFormat PixelFormat => SurfaceFormat.R8G8B8A8.UNorm;

    public bool IsMultiFrame => false;

    public bool HasMipLevels => false;

    public uint MipLevelsCount => 0;

    public uint NumberOfReplays => 0;

    public uint FramesCount => 1;

    /// <summary>The image ID field, empty when the file has none.</summary>
    public string Id { get; }

    public byte[] GetRawPixels(uint frameIndex) => _pixels;

    public FrameData GetMipLevelData(uint mipLevel) => GetFrameData(0);

    public ImageDescription GetImageDescription() => ImageDescription.Default2D(Width, Height, PixelFormat);

    public FrameData GetFrameData(uint frameIndex) => _frame ??= new FrameData(_pixels, GetImageDescription());

    /// <summary>Reads the TGA in <paramref name="file"/>.</summary>
    public static TgaImage Read(ReadOnlySpan<byte> file)
    {
        if (file.Length < 18)
        {
            throw new InvalidDataException("A TGA file is 18 bytes at least.");
        }

        int idLength = file[0], mapType = file[1], type = file[2];
        int mapFirst = BinaryPrimitives.ReadUInt16LittleEndian(file[3..]);
        int mapLength = BinaryPrimitives.ReadUInt16LittleEndian(file[5..]);
        int mapBits = file[7];
        int width = BinaryPrimitives.ReadUInt16LittleEndian(file[12..]);
        int height = BinaryPrimitives.ReadUInt16LittleEndian(file[14..]);
        int depth = file[16], descriptor = file[17];
        var kind = type & 7;
        var compressed = type >= 8;
        if (type is not (1 or 2 or 3 or 9 or 10 or 11))
        {
            throw type == 0
                ? new InvalidDataException("The TGA file has no image.")
                : new NotSupportedException($"TGA image type {type} is not read.");
        }

        if (mapType > 1 || kind == 1 && mapType != 1)
        {
            throw new InvalidDataException($"A TGA image of type {type} with color map type {mapType}.");
        }

        if (width == 0 || height == 0)
        {
            throw new InvalidDataException($"A TGA image of {width}×{height}.");
        }

        var supported = kind switch
        {
            1 => depth is 8 or 16 && mapBits is 15 or 16 or 24 or 32,
            2 => depth is 15 or 16 or 24 or 32,
            _ => depth is 8 or 16
        };
        if (!supported)
        {
            throw new NotSupportedException(kind == 1
                ? $"TGA color-mapped pixels of {depth} bits in a map of {mapBits} are not read."
                : $"TGA pixels of {depth} bits in image type {type} are not read.");
        }

        var at = 18;
        var id = Encoding.Latin1.GetString(Slice(file, at, idLength)).TrimEnd('\0');
        at += idLength;
        // A pixel's alpha: the bits the descriptor counts - and the fourth byte of a 32-bit pixel, the second of a
        // 16-bit gray one, even where it counts none, as writers leave it and readers take it; a map entry's, its own
        // fourth byte. All as the extension area has them, when it says.
        var alphaBits = descriptor & 15;
        var alpha = Alpha(file, alphaBits > 0 || kind == 2 && depth == 32 || kind == 3 && depth == 16);
        var mapAlpha = Alpha(file, mapBits == 32);

        // The color map, as RGBA - a map is there when its type says so, used or not.
        uint[] map = null;
        if (mapType == 1)
        {
            var entryBytes = (mapBits + 7) / 8;
            var entries = Slice(file, at, mapLength * entryBytes);
            at += entries.Length;
            if (kind == 1)
            {
                // Indices the map has no entry for are opaque black, below its first entry as past its last.
                map = new uint[mapFirst + mapLength];
                Array.Fill(map, 0xFF000000u);
                for (var i = 0; i < mapLength; i++)
                {
                    map[mapFirst + i] = Color(entries.Slice(i * entryBytes, entryBytes), mapBits, mapAlpha);
                }
            }
        }

        var pixelBytes = (depth + 7) / 8;
        var count = (long)width * height;
        var data = file[at..];
        // Plain pixels must all be there; a packet of 128 pixels takes one byte and a pixel at least.
        if (compressed ? count > 128L * data.Length / (1 + pixelBytes) + 128 : count * pixelBytes > data.Length)
        {
            throw new InvalidDataException($"A TGA file ends before its {width}×{height} pixels do.");
        }

        if (count * 4 > Array.MaxLength)
        {
            throw new NotSupportedException($"A TGA image of {width}×{height} is larger than an array holds.");
        }

        // The pixels as the file has them, one after another.
        var stream = new byte[count * pixelBytes];
        if (compressed)
        {
            Expand(data, stream, pixelBytes);
        }
        else
        {
            data[..stream.Length].CopyTo(stream);
        }

        var pixels = new byte[count * 4];
        var target = System.Runtime.InteropServices.MemoryMarshal.Cast<byte, uint>(pixels.AsSpan());
        bool rightToLeft = (descriptor & 0x10) != 0, topToBottom = (descriptor & 0x20) != 0;
        for (var row = 0; row < height; row++)
        {
            var y = topToBottom ? row : height - 1 - row;
            if (kind == 2 && depth >= 24 && alpha != AlphaKind.Premultiplied && !rightToLeft)
            {
                // The common case, a byte at a time: BGR(A) to RGBA.
                var source = stream.AsSpan((int)((long)row * width * pixelBytes), width * pixelBytes);
                var to = target.Slice(y * width, width);
                var opaque = depth == 24 || alpha == AlphaKind.None;
                for (int x = 0, i = 0; x < width; x++, i += pixelBytes)
                {
                    to[x] = source[i + 2] | (uint)source[i + 1] << 8 | (uint)source[i] << 16
                            | (opaque ? 0xFF000000u : (uint)source[i + 3] << 24);
                }

                continue;
            }

            for (var column = 0; column < width; column++)
            {
                var x = rightToLeft ? width - 1 - column : column;
                var pixel = stream.AsSpan((int)(((long)row * width + column) * pixelBytes), pixelBytes);
                target[y * width + x] = kind switch
                {
                    1 => Index(pixel, depth) is var index && index < map.Length ? map[index] : 0xFF000000u,
                    2 => Color(pixel, depth, alpha),
                    _ => Gray(pixel, alpha)
                };
            }
        }

        return new TgaImage((uint)width, (uint)height, pixels, id);
    }

    // What alpha bits are: none when there are none or the extension area says they are not alpha; premultiplied when
    // it says so.
    private enum AlphaKind
    {
        None,
        Straight,
        Premultiplied
    }

    private static AlphaKind Alpha(ReadOnlySpan<byte> file, bool present)
    {
        if (!present)
        {
            return AlphaKind.None;
        }

        // TGA 2.0: a footer names the extension area, whose attributes type says what the alpha bits hold.
        if (file.Length >= 26 + 18 && file[^18..].SequenceEqual("TRUEVISION-XFILE.\0"u8))
        {
            var extension = BinaryPrimitives.ReadUInt32LittleEndian(file[^26..]);
            if (extension >= 18 && extension + 495L <= file.Length
                                && BinaryPrimitives.ReadUInt16LittleEndian(file[(int)extension..]) >= 495)
            {
                return file[(int)extension + 494] switch
                {
                    0 or 1 or 2 => AlphaKind.None,
                    4 => AlphaKind.Premultiplied,
                    _ => AlphaKind.Straight
                };
            }
        }

        return AlphaKind.Straight;
    }

    private static ReadOnlySpan<byte> Slice(ReadOnlySpan<byte> file, int at, int length) =>
        at + length <= file.Length
            ? file.Slice(at, length)
            : throw new InvalidDataException("A TGA file ends in its header fields.");

    // Run-length packets (TGA 2.0, image types 9 to 11): a run of one pixel repeated, or that many pixels as they are;
    // packets may run on from one row to the next.
    private static void Expand(ReadOnlySpan<byte> data, Span<byte> output, int pixelBytes)
    {
        var at = 0;
        var written = 0;
        while (written < output.Length)
        {
            if (at >= data.Length)
            {
                throw new InvalidDataException("TGA run-length data ends before the image does.");
            }

            var header = data[at++];
            var pixels = Math.Min((header & 0x7F) + 1, (output.Length - written) / pixelBytes);
            var length = (header & 0x80) != 0 ? pixelBytes : pixels * pixelBytes;
            if (at + length > data.Length)
            {
                throw new InvalidDataException("TGA run-length data ends before the image does.");
            }

            if ((header & 0x80) != 0)
            {
                var pixel = data.Slice(at, pixelBytes);
                for (var i = 0; i < pixels; i++)
                {
                    pixel.CopyTo(output[(written + i * pixelBytes)..]);
                }
            }
            else
            {
                data.Slice(at, length).CopyTo(output[written..]);
            }

            at += length;
            written += pixels * pixelBytes;
        }
    }

    private static int Index(ReadOnlySpan<byte> pixel, int depth) =>
        depth == 8 ? pixel[0] : BinaryPrimitives.ReadUInt16LittleEndian(pixel);

    // A true-color pixel or map entry - BGR(A) of 8 bits, or 5-5-5 with an attribute bit - as RGBA.
    private static uint Color(ReadOnlySpan<byte> pixel, int bits, AlphaKind alpha)
    {
        uint r, g, b, a;
        if (bits <= 16)
        {
            var value = BinaryPrimitives.ReadUInt16LittleEndian(pixel);
            (r, g, b) = (Scale5((value >> 10) & 31), Scale5((value >> 5) & 31), Scale5(value & 31));
            a = bits == 16 && alpha != AlphaKind.None ? (value & 0x8000) != 0 ? 255u : 0u : 255u;
        }
        else
        {
            (b, g, r) = (pixel[0], pixel[1], pixel[2]);
            a = bits == 32 && alpha != AlphaKind.None ? pixel[3] : 255u;
        }

        if (alpha == AlphaKind.Premultiplied && a is > 0 and < 255)
        {
            (r, g, b) = (Unpremultiply(r, a), Unpremultiply(g, a), Unpremultiply(b, a));
        }

        return r | g << 8 | b << 16 | a << 24;
    }

    private static uint Gray(ReadOnlySpan<byte> pixel, AlphaKind alpha)
    {
        uint v = pixel[0];
        var a = pixel.Length == 2 && alpha != AlphaKind.None ? pixel[1] : 255u;
        if (alpha == AlphaKind.Premultiplied && a is > 0 and < 255)
        {
            v = Unpremultiply(v, a);
        }

        return v | v << 8 | v << 16 | a << 24;
    }

    private static uint Scale5(int value) => (uint)((value * 255 + 15) / 31);

    private static uint Unpremultiply(uint value, uint alpha) => Math.Min(255u, (value * 255 + alpha / 2) / alpha);
}
