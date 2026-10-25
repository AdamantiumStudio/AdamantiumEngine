using System;
using System.Buffers.Binary;
using System.IO;

namespace Adamantium.Imaging.Tiff;

/// <summary>Reads the strips or tiles of one TIFF image into its samples: decompression, bit order, byte order,
/// predictors, packed bit depths, planes and subsampled YCbCr.</summary>
internal static class TiffDecoder
{
    /// <summary>The bytes of each decoded sample's container for <paramref name="layout"/>.</summary>
    public static int ContainerBytes(TiffLayout layout) => layout.BitsPerSample switch
    {
        <= 8 => 1,
        <= 16 => 2,
        <= 32 => 4,
        _ => 8
    };

    /// <summary>Decodes the whole image of <paramref name="layout"/>.</summary>
    public static TiffSamples Decode(ITiffSource source, TiffLayout layout, bool littleEndian)
    {
        CheckSupported(layout);
        if (layout.Compression == TiffCompression.None)
        {
            CheckUncompressedChunks(source, layout);
        }

        var samples = new TiffSamples(layout.Width, layout.Height, layout.SamplesPerPixel, ContainerBytes(layout));
        var chunkBytes = ChunkBytes(layout, Math.Min(layout.ChunkHeight, layout.Height));
        var rowBytes = (long)layout.ChunkWidth * layout.SamplesPerChunk * samples.ContainerBytes;
        if (chunkBytes > Array.MaxLength || rowBytes > Array.MaxLength)
        {
            throw new NotSupportedException($"A TIFF strip or tile of {chunkBytes} bytes decoded is more than one array "
                                            + "holds.");
        }

        var chunk = new byte[chunkBytes];
        var row = new byte[rowBytes];
        byte[] compressed = null;
        var planes = layout.IsPlanar ? layout.SamplesPerPixel : 1;
        for (var plane = 0; plane < planes; plane++)
        {
            for (var down = 0; down < layout.ChunksDown; down++)
            {
                for (var across = 0; across < layout.ChunksAcross; across++)
                {
                    var index = layout.ChunkIndex(across, down, plane);
                    ReadChunk(source, layout, index, layout.ChunkRows(down), chunk, ref compressed);
                    if (layout.Subsampling != (1, 1))
                    {
                        PlaceSubsampled(layout, chunk, samples, across, down);
                    }
                    else
                    {
                        Place(layout, littleEndian, chunk, row, samples, across, down, plane);
                    }
                }
            }
        }

        return samples;
    }

    private static void CheckSupported(TiffLayout layout)
    {
        if (layout.Predictor is not (1 or 2 or 3))
        {
            throw new NotSupportedException($"The TIFF predictor {layout.Predictor} is not one TIFF defines.");
        }

        if (layout.Predictor == 2 && layout.BitsPerSample is not (8 or 16 or 32 or 64))
        {
            throw new NotSupportedException($"The TIFF horizontal predictor over {layout.BitsPerSample}-bit samples.");
        }

        if (layout.Predictor == 3 && layout.SampleFormat != TiffSampleFormat.Float)
        {
            throw new InvalidDataException("The TIFF floating-point predictor on samples that are not floats.");
        }

        if (layout.Subsampling != (1, 1) && (layout.BitsPerSample != 8 || layout.IsPlanar))
        {
            throw new NotSupportedException("TIFF subsampled YCbCr other than 8 bits, samples together.");
        }

        if (layout.Compression is TiffCompression.CcittRle or TiffCompression.CcittGroup3
                or TiffCompression.CcittGroup4 && (layout.BitsPerSample != 1 || layout.SamplesPerPixel != 1))
        {
            throw new InvalidDataException("TIFF CCITT compression of samples that are not single bits.");
        }

        if (layout.Compression == TiffCompression.Jpeg
            && (layout.BitsPerSample != 8 || layout.IsPlanar || layout.SamplesPerPixel is not (1 or 3)))
        {
            throw new NotSupportedException($"TIFF JPEG of {layout.SamplesPerPixel} samples of "
                                            + $"{layout.BitsPerSample} bits, {(layout.IsPlanar ? "planar" : "together")}.");
        }
    }

    private static void CheckUncompressedChunks(ITiffSource source, TiffLayout layout)
    {
        var needed = layout.ChunksAcross * layout.ChunksDown * (layout.IsPlanar ? layout.SamplesPerPixel : 1);
        for (var index = 0; index < needed; index++)
        {
            var count = layout.ChunkByteCounts[index];
            var offset = layout.ChunkOffsets[index];
            if (count == 0)
            {
                continue;
            }

            var expected = ChunkBytes(layout, layout.ChunkRows(index / layout.ChunksAcross % layout.ChunksDown));
            var available = offset < (ulong)source.Length ? Math.Min(count, (ulong)source.Length - offset) : 0;
            if (available < (ulong)expected)
            {
                throw new InvalidDataException($"TIFF strip or tile {index} holds {available} of its {expected} bytes.");
            }
        }
    }

    private static long ChunkBytes(TiffLayout layout, int rows)
    {
        if (layout.Subsampling == (1, 1))
        {
            return (long)layout.ChunkRowBytes * rows;
        }

        var (x, y) = layout.Subsampling;
        var units = (long)((layout.ChunkWidth + x - 1) / x) * ((rows + y - 1) / y);
        return units * (x * y + 2);
    }

    private static void ReadChunk(ITiffSource source, TiffLayout layout, int index, int rows, byte[] chunk,
        ref byte[] compressed)
    {
        var expected = (int)ChunkBytes(layout, rows);
        var output = chunk.AsSpan(0, expected);
        output.Clear();
        var offset = layout.ChunkOffsets[index];
        if (layout.ChunkByteCounts[index] == 0)
        {
            return;
        }

        if (offset >= (ulong)source.Length)
        {
            throw new InvalidDataException($"TIFF strip or tile {index} starts at {offset}, past the file's end.");
        }

        var count = (int)Math.Min(layout.ChunkByteCounts[index], (ulong)source.Length - offset);
        var reversed = layout.IsBitReversed && layout.Compression != TiffCompression.Jpeg;
        if (layout.Compression == TiffCompression.None)
        {
            source.Read((long)offset, output);
            if (reversed)
            {
                ReverseBits(output);
            }

            return;
        }

        if (compressed == null || compressed.Length < count)
        {
            compressed = new byte[count];
        }

        var input = compressed.AsSpan(0, count);
        source.Read((long)offset, input);
        if (reversed)
        {
            ReverseBits(input);
        }

        Decompress(layout, input, output);
    }

    private static void Decompress(TiffLayout layout, ReadOnlySpan<byte> input, Span<byte> output)
    {
        switch (layout.Compression)
        {
            case TiffCompression.PackBits:
                TiffPackBits.Decode(input, output);
                break;
            case TiffCompression.Lzw:
                TiffLzw.Decode(input, output);
                break;
            case TiffCompression.Deflate:
            case TiffCompression.OldDeflate:
                TiffDeflate.Decode(input, output);
                break;
            case TiffCompression.CcittRle:
            case TiffCompression.CcittGroup3:
            case TiffCompression.CcittGroup4:
                TiffCcitt.Decode(input, output, layout.ChunkWidth, layout.Compression,
                    layout.Directory.GetUnsigned(TiffTag.T4Options, 0));
                break;
            case TiffCompression.Jpeg:
                TiffJpeg.Decode(input, layout.Directory[TiffTag.JpegTables]?.Bytes.ToArray(),
                    layout.Photometric == TiffPhotometric.Rgb, layout.ChunkWidth, layout.ChunkHeight,
                    layout.SamplesPerChunk, output);
                break;
            case TiffCompression.Lerc:
                throw new NotSupportedException("TIFF compressed with LERC is not read: the format has no published "
                                                + "specification.");
            default:
                throw new NotSupportedException($"TIFF compression {layout.Compression} is not read.");
        }
    }

    private static void Place(TiffLayout layout, bool littleEndian, byte[] chunk, byte[] row, TiffSamples samples,
        int across, int down, int plane)
    {
        var x0 = across * layout.ChunkWidth;
        var columns = Math.Min(layout.ChunkWidth, layout.Width - x0);
        var count = layout.ChunkWidth * layout.SamplesPerChunk;
        var pixelBytes = layout.SamplesPerChunk * samples.ContainerBytes;
        for (var r = 0; r < layout.ChunkHeight; r++)
        {
            var y = down * layout.ChunkHeight + r;
            if (y >= layout.Height)
            {
                break;
            }

            var packed = chunk.AsSpan(r * layout.ChunkRowBytes, layout.ChunkRowBytes);
            var native = row.AsSpan(0, count * samples.ContainerBytes);
            ToNative(layout, littleEndian, packed, native, count);
            if (!layout.IsPlanar)
            {
                native[..(columns * pixelBytes)].CopyTo(samples.Data.AsSpan((int)samples.Offset(x0, y, 0)));
                continue;
            }

            for (var x = 0; x < columns; x++)
            {
                native.Slice(x * samples.ContainerBytes, samples.ContainerBytes)
                    .CopyTo(samples.Data.AsSpan((int)samples.Offset(x0 + x, y, plane)));
            }
        }
    }

    private static void ToNative(TiffLayout layout, bool littleEndian, Span<byte> packed, Span<byte> native, int count)
    {
        var bits = layout.BitsPerSample;
        var container = native.Length / count;
        if (layout.Predictor == 3)
        {
            UndoFloatPredictor(packed, native, count, bits / 8, layout.SamplesPerChunk);
        }
        else if (bits is 8 or 16 or 32 or 64)
        {
            packed[..native.Length].CopyTo(native);
            if (!littleEndian && bits > 8)
            {
                SwapBytes(native, bits / 8);
            }

            if (layout.Predictor == 2)
            {
                UndoHorizontalPredictor(native, bits / 8, layout.SamplesPerChunk);
            }
        }
        else if (bits % 8 == 0)
        {
            var size = bits / 8;
            for (var i = 0; i < count; i++)
            {
                var value = packed.Slice(i * size, size);
                var target = native.Slice(i * container, container);
                target.Clear();
                for (var b = 0; b < size; b++)
                {
                    target[b] = littleEndian ? value[b] : value[size - 1 - b];
                }
            }
        }
        else
        {
            UnpackBits(packed, native, count, bits, container);
        }

        if (layout.SampleFormat == TiffSampleFormat.Float && bits == 24)
        {
            WidenFloat24(native, count);
        }
        else if (layout.SampleFormat == TiffSampleFormat.SignedInteger && bits < container * 8)
        {
            ExtendSign(native, count, bits, container);
        }
    }

    private static void UndoHorizontalPredictor(Span<byte> row, int size, int stride)
    {
        var count = row.Length / size;
        for (var i = stride; i < count; i++)
        {
            var at = row.Slice(i * size, size);
            var before = row.Slice((i - stride) * size, size);
            switch (size)
            {
                case 1:
                    at[0] += before[0];
                    break;
                case 2:
                    BinaryPrimitives.WriteUInt16LittleEndian(at, (ushort)(BinaryPrimitives.ReadUInt16LittleEndian(at)
                        + BinaryPrimitives.ReadUInt16LittleEndian(before)));
                    break;
                case 4:
                    BinaryPrimitives.WriteUInt32LittleEndian(at, BinaryPrimitives.ReadUInt32LittleEndian(at)
                        + BinaryPrimitives.ReadUInt32LittleEndian(before));
                    break;
                default:
                    BinaryPrimitives.WriteUInt64LittleEndian(at, BinaryPrimitives.ReadUInt64LittleEndian(at)
                        + BinaryPrimitives.ReadUInt64LittleEndian(before));
                    break;
            }
        }
    }

    private static void UndoFloatPredictor(Span<byte> packed, Span<byte> native, int count, int size, int stride)
    {
        var bytes = packed[..(count * size)];
        for (var i = stride; i < bytes.Length; i++)
        {
            bytes[i] += bytes[i - stride];
        }

        var container = native.Length / count;
        native.Clear();
        for (var i = 0; i < count; i++)
        {
            for (var b = 0; b < size; b++)
            {
                native[i * container + size - 1 - b] = bytes[b * count + i];
            }
        }
    }

    private static void UnpackBits(ReadOnlySpan<byte> packed, Span<byte> native, int count, int bits, int container)
    {
        long bit = 0;
        for (var i = 0; i < count; i++)
        {
            ulong value = 0;
            for (var b = 0; b < bits; b++, bit++)
            {
                value = (value << 1) | (uint)((packed[(int)(bit >> 3)] >> (7 - (int)(bit & 7))) & 1);
            }

            var target = native.Slice(i * container, container);
            for (var b = 0; b < container; b++)
            {
                target[b] = (byte)(value >> (8 * b));
            }
        }
    }

    private static void WidenFloat24(Span<byte> native, int count)
    {
        for (var i = 0; i < count; i++)
        {
            var at = native.Slice(i * 4, 4);
            var value = at[0] | (at[1] << 8) | (at[2] << 16);
            var sign = (value >> 23) & 1;
            var exponent = (value >> 16) & 0x7F;
            var mantissa = value & 0xFFFF;
            uint bits;
            if (exponent == 0x7F)
            {
                bits = ((uint)sign << 31) | 0x7F800000u | ((uint)mantissa << 7);
            }
            else if (exponent == 0)
            {
                var single = mantissa / 65536f * MathF.Pow(2, -62);
                bits = (uint)BitConverter.SingleToInt32Bits(sign == 1 ? -single : single);
            }
            else
            {
                bits = ((uint)sign << 31) | ((uint)(exponent - 63 + 127) << 23) | ((uint)mantissa << 7);
            }

            BinaryPrimitives.WriteUInt32LittleEndian(at, bits);
        }
    }

    private static void ExtendSign(Span<byte> native, int count, int bits, int container)
    {
        for (var i = 0; i < count; i++)
        {
            var at = native.Slice(i * container, container);
            if (((at[(bits - 1) / 8] >> ((bits - 1) % 8)) & 1) == 0)
            {
                continue;
            }

            for (var b = bits; b < container * 8; b++)
            {
                at[b / 8] |= (byte)(1 << (b % 8));
            }
        }
    }

    private static void PlaceSubsampled(TiffLayout layout, byte[] chunk, TiffSamples samples, int across, int down)
    {
        var (sx, sy) = layout.Subsampling;
        var unitsAcross = (layout.ChunkWidth + sx - 1) / sx;
        var unitsDown = (layout.ChunkRows(down) + sy - 1) / sy;
        var unitBytes = sx * sy + 2;
        for (var u = 0; u < unitsAcross * unitsDown; u++)
        {
            var unit = chunk.AsSpan(u * unitBytes, unitBytes);
            var ux = u % unitsAcross * sx;
            var uy = u / unitsAcross * sy;
            for (var j = 0; j < sy; j++)
            {
                for (var i = 0; i < sx; i++)
                {
                    var x = across * layout.ChunkWidth + ux + i;
                    var y = down * layout.ChunkHeight + uy + j;
                    if (x >= layout.Width || y >= layout.Height || ux + i >= layout.ChunkWidth
                        || uy + j >= layout.ChunkHeight)
                    {
                        continue;
                    }

                    var at = (int)samples.Offset(x, y, 0);
                    samples.Data[at] = unit[j * sx + i];
                    samples.Data[at + 1] = unit[sx * sy];
                    samples.Data[at + 2] = unit[sx * sy + 1];
                }
            }
        }
    }

    private static void SwapBytes(Span<byte> data, int size)
    {
        for (var i = 0; i + size <= data.Length; i += size)
        {
            data.Slice(i, size).Reverse();
        }
    }

    private static void ReverseBits(Span<byte> data)
    {
        for (var i = 0; i < data.Length; i++)
        {
            var value = data[i];
            value = (byte)(((value & 0xF0) >> 4) | ((value & 0x0F) << 4));
            value = (byte)(((value & 0xCC) >> 2) | ((value & 0x33) << 2));
            data[i] = (byte)(((value & 0xAA) >> 1) | ((value & 0x55) << 1));
        }
    }
}
