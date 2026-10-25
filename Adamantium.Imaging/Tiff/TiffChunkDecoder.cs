using System;
using System.Buffers.Binary;
using System.IO;

namespace Adamantium.Imaging.Tiff;

/// <summary>Decodes the strips or tiles of one TIFF image one at a time into their samples: decompression, bit order,
/// byte order, predictors, packed bit depths and subsampled YCbCr. Each sample comes out in a little-endian container
/// of 1, 2, 4 or 8 bytes; floats of 24 bits are widened to 32. One decoder holds buffers of its own: use one per thread.
/// </summary>
internal sealed class TiffChunkDecoder
{
    private readonly ITiffSource _source;
    private readonly TiffLayout _layout;
    private readonly bool _littleEndian;
    private readonly long _chunkBytes;
    private byte[] _chunk;
    private byte[] _compressed;
    private byte[] _native;

    /// <summary>A decoder of <paramref name="layout"/>'s strips or tiles; one the reader cannot take throws
    /// <see cref="NotSupportedException"/>, a broken one <see cref="InvalidDataException"/>.</summary>
    public TiffChunkDecoder(ITiffSource source, TiffLayout layout, bool littleEndian)
    {
        CheckSupported(layout);
        _source = source;
        _layout = layout;
        _littleEndian = littleEndian;
        Container = ContainerBytes(layout);
        var rows = Math.Min(layout.ChunkHeight, layout.Height);
        var chunkBytes = ChunkBytes(layout, rows);
        NativeChunkBytes = rows * NativeRowBytes;
        if (chunkBytes > Array.MaxLength || NativeChunkBytes > Array.MaxLength)
        {
            throw new NotSupportedException($"A TIFF strip or tile of {Math.Max(chunkBytes, NativeChunkBytes)} bytes "
                                            + "decoded is more than one array holds.");
        }

        _chunkBytes = chunkBytes;
    }

    /// <summary>Bytes of each sample's container.</summary>
    public int Container { get; }

    /// <summary>Samples of each decoded pixel of a strip or tile: one when planar, three for subsampled YCbCr.</summary>
    public int Samples => _layout.Subsampling != (1, 1) ? 3 : _layout.SamplesPerChunk;

    /// <summary>Bytes of one decoded row of a strip or tile, its full width.</summary>
    public long NativeRowBytes => (long)_layout.ChunkWidth * Samples * Container;

    /// <summary>Bytes of the largest decoded strip or tile: its rows inside the image.</summary>
    public long NativeChunkBytes { get; }

    /// <summary>A buffer of <see cref="NativeChunkBytes"/> to decode into, the decoder's own.</summary>
    public byte[] Native => _native ??= new byte[NativeChunkBytes];

    /// <summary>The bytes of each decoded sample's container for <paramref name="layout"/>.</summary>
    public static int ContainerBytes(TiffLayout layout) => layout.BitsPerSample switch
    {
        <= 8 => 1,
        <= 16 => 2,
        <= 32 => 4,
        _ => 8
    };

    /// <summary>Decodes strip or tile (<paramref name="across"/>, <paramref name="down"/>) of
    /// <paramref name="plane"/> into <paramref name="native"/>: its rows inside the image, each
    /// <see cref="NativeRowBytes"/> long. One the file leaves out comes out zero.</summary>
    public void Decode(int across, int down, int plane, Span<byte> native)
    {
        var rows = _layout.ChunkRows(down);
        _chunk ??= new byte[_chunkBytes];
        var output = _chunk.AsSpan(0, (int)ChunkBytes(_layout, rows));
        Read(_layout.ChunkIndex(across, down, plane), output);
        var rowBytes = (int)NativeRowBytes;
        if (_layout.Subsampling != (1, 1))
        {
            Expand(output, rows, native[..(rows * rowBytes)]);
            return;
        }

        var count = _layout.ChunkWidth * _layout.SamplesPerChunk;
        for (var r = 0; r < rows; r++)
        {
            ToNative(output.Slice(r * _layout.ChunkRowBytes, _layout.ChunkRowBytes),
                native.Slice(r * rowBytes, rowBytes), count);
        }
    }

    /// <summary>Whether the file holds strip or tile (<paramref name="across"/>, <paramref name="down"/>) of
    /// <paramref name="plane"/>; one it leaves out decodes to zeros.</summary>
    public bool Holds(int across, int down, int plane)
    {
        var index = _layout.ChunkIndex(across, down, plane);
        return _layout.ChunkByteCounts[index] != 0 && _layout.ChunkOffsets[index] != 0;
    }

    /// <summary>Throws <see cref="InvalidDataException"/> unless every uncompressed strip or tile the image needs is in
    /// the file whole - before anything is allocated for the image.</summary>
    public void CheckUncompressed()
    {
        var needed = _layout.ChunksAcross * _layout.ChunksDown * (_layout.IsPlanar ? _layout.SamplesPerPixel : 1);
        for (var index = 0; index < needed; index++)
        {
            Available(index, _layout.ChunkRows(index / _layout.ChunksAcross % _layout.ChunksDown));
        }
    }

    private static void CheckSupported(TiffLayout layout)
    {
        if (layout.Compression == TiffCompression.Lerc)
        {
            throw new NotSupportedException("TIFF compressed with LERC is not read: the format has no published "
                                            + "specification.");
        }

        if (layout.Compression is not (TiffCompression.None or TiffCompression.PackBits or TiffCompression.Lzw
            or TiffCompression.Deflate or TiffCompression.OldDeflate or TiffCompression.CcittRle
            or TiffCompression.CcittGroup3 or TiffCompression.CcittGroup4 or TiffCompression.Jpeg))
        {
            throw new NotSupportedException($"TIFF compression {layout.Compression} is not read.");
        }

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

        if (layout.Subsampling != (1, 1)
            && (layout.BitsPerSample != 8 || layout.IsPlanar || layout.SamplesPerPixel != 3))
        {
            throw new NotSupportedException("TIFF subsampled YCbCr other than 3 samples of 8 bits together.");
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

    private int Available(int index, int rows)
    {
        var count = _layout.ChunkByteCounts[index];
        var offset = _layout.ChunkOffsets[index];
        if (count == 0 || offset == 0)
        {
            return 0;
        }

        if (offset >= (ulong)_source.Length)
        {
            throw new InvalidDataException($"TIFF strip or tile {index} starts at {offset}, past the file's end.");
        }

        var remaining = Math.Min(count, (ulong)_source.Length - offset);
        if (remaining > (ulong)Array.MaxLength)
        {
            throw new NotSupportedException($"TIFF strip or tile {index} of {remaining} bytes is more than one array "
                                            + "holds.");
        }

        var available = (int)remaining;
        var expected = ChunkBytes(_layout, rows);
        if (_layout.Compression == TiffCompression.None && available < expected)
        {
            throw new InvalidDataException($"TIFF strip or tile {index} holds {available} of its {expected} bytes.");
        }

        return available;
    }

    private void Read(int index, Span<byte> output)
    {
        output.Clear();
        var count = Available(index, _layout.ChunkRows(index / _layout.ChunksAcross % _layout.ChunksDown));
        if (count == 0)
        {
            return;
        }

        var offset = (long)_layout.ChunkOffsets[index];
        var reversed = _layout.IsBitReversed && _layout.Compression != TiffCompression.Jpeg;
        if (_layout.Compression == TiffCompression.None)
        {
            _source.Read(offset, output);
            if (reversed)
            {
                ReverseBits(output);
            }

            return;
        }

        if (_compressed == null || _compressed.Length < count)
        {
            _compressed = new byte[count];
        }

        var input = _compressed.AsSpan(0, count);
        _source.Read(offset, input);
        if (reversed)
        {
            ReverseBits(input);
        }

        Decompress(input, output);
    }

    private void Decompress(ReadOnlySpan<byte> input, Span<byte> output)
    {
        switch (_layout.Compression)
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
                TiffCcitt.Decode(input, output, _layout.ChunkWidth, _layout.Compression,
                    _layout.Directory.GetUnsigned(TiffTag.T4Options, 0));
                break;
            case TiffCompression.Jpeg:
                TiffJpeg.Decode(input, _layout.Directory[TiffTag.JpegTables]?.Bytes.ToArray(),
                    _layout.Photometric == TiffPhotometric.Rgb, _layout.ChunkWidth, _layout.ChunkHeight,
                    _layout.SamplesPerChunk, output);
                break;
            default:
                throw new NotSupportedException($"TIFF compression {_layout.Compression} is not read.");
        }
    }

    private void ToNative(Span<byte> packed, Span<byte> native, int count)
    {
        var bits = _layout.BitsPerSample;
        if (_layout.Predictor == 3)
        {
            UndoFloatPredictor(packed, native, count, bits / 8, _layout.SamplesPerChunk);
        }
        else if (bits is 8 or 16 or 32 or 64)
        {
            packed[..native.Length].CopyTo(native);
            if (!_littleEndian && bits > 8)
            {
                SwapBytes(native, bits / 8);
            }

            if (_layout.Predictor == 2)
            {
                UndoHorizontalPredictor(native, bits / 8, _layout.SamplesPerChunk);
            }
        }
        else if (bits % 8 == 0)
        {
            var size = bits / 8;
            for (var i = 0; i < count; i++)
            {
                var value = packed.Slice(i * size, size);
                var target = native.Slice(i * Container, Container);
                target.Clear();
                for (var b = 0; b < size; b++)
                {
                    target[b] = _littleEndian ? value[b] : value[size - 1 - b];
                }
            }
        }
        else
        {
            UnpackBits(packed, native, count, bits, Container);
        }

        if (_layout.SampleFormat == TiffSampleFormat.Float && bits == 24)
        {
            WidenFloat24(native, count);
        }
        else if (_layout.SampleFormat == TiffSampleFormat.SignedInteger && bits < Container * 8)
        {
            ExtendSign(native, count, bits, Container);
        }
    }

    private void Expand(ReadOnlySpan<byte> units, int rows, Span<byte> native)
    {
        var (sx, sy) = _layout.Subsampling;
        var width = _layout.ChunkWidth;
        var unitsAcross = (width + sx - 1) / sx;
        var unitsDown = (rows + sy - 1) / sy;
        var unitBytes = sx * sy + 2;
        for (var u = 0; u < unitsAcross * unitsDown; u++)
        {
            var unit = units.Slice(u * unitBytes, unitBytes);
            var ux = u % unitsAcross * sx;
            var uy = u / unitsAcross * sy;
            for (var j = 0; j < sy && uy + j < rows; j++)
            {
                for (var i = 0; i < sx && ux + i < width; i++)
                {
                    var at = ((uy + j) * width + ux + i) * 3;
                    native[at] = unit[j * sx + i];
                    native[at + 1] = unit[sx * sy];
                    native[at + 2] = unit[sx * sy + 1];
                }
            }
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
