using System;
using System.Buffers.Binary;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using Adamantium.Vulkan.Core;

namespace Adamantium.Imaging.Tiff;

/// <summary>Writes pictures as TIFF (TIFF 6.0): every frame a page, in strips or tiles, compressed as
/// <see cref="TiffSaveOptions"/> says; BigTIFF when the file would not fit in 4 GB.</summary>
public static class TiffEncoder
{
    private const int StripBytes = 64 * 1024;

    /// <summary>Writes <paramref name="bitmap"/> to <paramref name="stream"/> with the default options.</summary>
    public static void Save(IRawBitmap bitmap, Stream stream) => Save(bitmap, stream, new TiffSaveOptions());

    /// <summary>Writes <paramref name="bitmap"/> to <paramref name="stream"/>. A pixel format TIFF cannot hold throws
    /// <see cref="NotSupportedException"/>.</summary>
    public static void Save(IRawBitmap bitmap, Stream stream, TiffSaveOptions options)
    {
        if (options.Compression is not (TiffCompression.None or TiffCompression.PackBits or TiffCompression.Lzw
            or TiffCompression.Deflate))
        {
            throw new NotSupportedException($"Writing TIFF compressed with {options.Compression}.");
        }

        if (options.TileSize < 0 || options.TileSize % 16 != 0)
        {
            throw new ArgumentException("A TIFF tile's side is a multiple of 16.", nameof(options));
        }

        var pixel = Describe(bitmap.PixelFormat);
        if ((long)options.TileSize * options.TileSize * pixel.PixelBytes > Array.MaxLength)
        {
            throw new ArgumentException($"A TIFF tile of {options.TileSize}×{options.TileSize} is more than one array "
                                        + "holds.", nameof(options));
        }

        List<Page> pages = [];
        var pixels = new byte[bitmap.TotalSizeInBytes];
        for (uint frame = 0; frame < Math.Max(1, bitmap.FramesCount); frame++)
        {
            bitmap.CopyFrame(frame, pixels);
            pages.Add(Encode(pixels, (int)bitmap.Width, (int)bitmap.Height, pixel, options));
        }

        var dataBytes = pages.Sum(page => page.Chunks.Sum(chunk => (long)chunk.Length));
        var bigTiff = options.ForceBigTiff
                      || dataBytes + pages.Count * 4096L + pages.Sum(page => page.Chunks.Count * 16L) > uint.MaxValue;
        Write(stream, pages, pixel, options, bigTiff);
    }

    private static Page Encode(byte[] pixels, int width, int height, PixelLayout pixel, TiffSaveOptions options)
    {
        var size = pixel.Bits / 8;
        var tiled = options.TileSize > 0;
        var chunkWidth = tiled ? options.TileSize : width;
        var rowBytes = chunkWidth * pixel.Samples * size;
        var chunkHeight = tiled ? options.TileSize : Math.Clamp(StripBytes / Math.Max(1, rowBytes), 1, height);
        var predictor = Predictor(pixel, options);
        var across = (width + chunkWidth - 1) / chunkWidth;
        var down = (height + chunkHeight - 1) / chunkHeight;
        var chunk = new byte[rowBytes * chunkHeight];
        var plane = new byte[rowBytes];
        var chunks = new List<byte[]>(across * down);
        for (var cy = 0; cy < down; cy++)
        {
            for (var cx = 0; cx < across; cx++)
            {
                var rows = tiled ? chunkHeight : Math.Min(chunkHeight, height - cy * chunkHeight);
                var columns = Math.Min(chunkWidth, width - cx * chunkWidth);
                chunk.AsSpan().Clear();
                for (var r = 0; r < rows; r++)
                {
                    var y = cy * chunkHeight + r;
                    if (y >= height)
                    {
                        break;
                    }

                    var row = chunk.AsSpan(r * rowBytes, rowBytes);
                    var source = ((long)y * width + (long)cx * chunkWidth) * pixel.PixelBytes;
                    for (var x = 0; x < columns; x++)
                    {
                        pixels.AsSpan((int)(source + (long)x * pixel.PixelBytes), pixel.Samples * size)
                            .CopyTo(row[(x * pixel.Samples * size)..]);
                        if (pixel.Bgr)
                        {
                            var at = row.Slice(x * pixel.Samples * size, 3);
                            (at[0], at[2]) = (at[2], at[0]);
                        }
                    }

                    if (predictor == 2)
                    {
                        ApplyHorizontalPredictor(row, size, pixel.Samples);
                    }
                    else if (predictor == 3)
                    {
                        ApplyFloatPredictor(row, plane, size, pixel.Samples);
                    }
                }

                chunks.Add(Compress(chunk.AsSpan(0, rows * rowBytes), rowBytes, options.Compression));
            }
        }

        return new Page(width, height, chunkWidth, chunkHeight, tiled, predictor, chunks);
    }

    private static int Predictor(PixelLayout pixel, TiffSaveOptions options)
    {
        if (!options.UsePredictor || options.Compression is not (TiffCompression.Lzw or TiffCompression.Deflate))
        {
            return 1;
        }

        return pixel.Kind == TiffSampleFormat.Float ? 3 : 2;
    }

    private static byte[] Compress(ReadOnlySpan<byte> data, int rowBytes, TiffCompression compression)
    {
        switch (compression)
        {
            case TiffCompression.Lzw:
                return TiffLzw.Encode(data);
            case TiffCompression.Deflate:
                return TiffDeflate.Encode(data);
            case TiffCompression.PackBits:
                var packed = new List<byte>(data.Length / 2);
                for (var at = 0; at < data.Length; at += rowBytes)
                {
                    TiffPackBits.Encode(data.Slice(at, rowBytes), packed);
                }

                return [.. packed];
            default:
                return data.ToArray();
        }
    }

    private static void ApplyHorizontalPredictor(Span<byte> row, int size, int stride)
    {
        for (var i = row.Length / size - 1; i >= stride; i--)
        {
            var at = row.Slice(i * size, size);
            var before = row.Slice((i - stride) * size, size);
            switch (size)
            {
                case 1:
                    at[0] -= before[0];
                    break;
                case 2:
                    BinaryPrimitives.WriteUInt16LittleEndian(at, (ushort)(BinaryPrimitives.ReadUInt16LittleEndian(at)
                        - BinaryPrimitives.ReadUInt16LittleEndian(before)));
                    break;
                case 4:
                    BinaryPrimitives.WriteUInt32LittleEndian(at, BinaryPrimitives.ReadUInt32LittleEndian(at)
                        - BinaryPrimitives.ReadUInt32LittleEndian(before));
                    break;
                default:
                    BinaryPrimitives.WriteUInt64LittleEndian(at, BinaryPrimitives.ReadUInt64LittleEndian(at)
                        - BinaryPrimitives.ReadUInt64LittleEndian(before));
                    break;
            }
        }
    }

    private static void ApplyFloatPredictor(Span<byte> row, byte[] plane, int size, int stride)
    {
        var count = row.Length / size;
        for (var i = 0; i < count; i++)
        {
            for (var b = 0; b < size; b++)
            {
                plane[b * count + i] = row[i * size + size - 1 - b];
            }
        }

        for (var i = row.Length - 1; i >= stride; i--)
        {
            plane[i] -= plane[i - stride];
        }

        plane.AsSpan(0, row.Length).CopyTo(row);
    }

    private static void Write(Stream stream, List<Page> pages, PixelLayout pixel, TiffSaveOptions options, bool bigTiff)
    {
        var header = bigTiff ? 16L : 8L;
        var position = header;
        var offsets = new List<ulong[]>(pages.Count);
        foreach (var page in pages)
        {
            var pageOffsets = new ulong[page.Chunks.Count];
            for (var i = 0; i < pageOffsets.Length; i++)
            {
                pageOffsets[i] = (ulong)position;
                position += page.Chunks[i].Length;
            }

            offsets.Add(pageOffsets);
        }

        var directories = new List<byte[]>(pages.Count);
        var directoryOffsets = new long[pages.Count];
        for (var i = 0; i < pages.Count; i++)
        {
            position += position & 1;
            directoryOffsets[i] = position;
            var entries = Entries(pages[i], offsets[i], pixel, options, i, pages.Count, bigTiff);
            directories.Add(Directory(entries, position, bigTiff));
            position += directories[i].Length;
        }

        var start = new byte[header];
        start[0] = start[1] = 0x49;
        if (bigTiff)
        {
            start[2] = 43;
            start[4] = 8;
            BinaryPrimitives.WriteUInt64LittleEndian(start.AsSpan(8), (ulong)directoryOffsets[0]);
        }
        else
        {
            start[2] = 42;
            BinaryPrimitives.WriteUInt32LittleEndian(start.AsSpan(4), (uint)directoryOffsets[0]);
        }

        stream.Write(start);
        var written = header;
        foreach (var chunk in pages.SelectMany(page => page.Chunks))
        {
            stream.Write(chunk);
            written += chunk.Length;
        }

        for (var i = 0; i < directories.Count; i++)
        {
            if (written < directoryOffsets[i])
            {
                stream.WriteByte(0);
                written++;
            }

            var next = i + 1 < directories.Count ? (ulong)directoryOffsets[i + 1] : 0;
            var nextAt = DirectoryNextOffsetAt(directories[i], bigTiff);
            if (bigTiff)
            {
                BinaryPrimitives.WriteUInt64LittleEndian(directories[i].AsSpan(nextAt), next);
            }
            else
            {
                BinaryPrimitives.WriteUInt32LittleEndian(directories[i].AsSpan(nextAt), (uint)next);
            }

            stream.Write(directories[i]);
            written += directories[i].Length;
        }
    }

    private static List<Entry> Entries(Page page, ulong[] offsets, PixelLayout pixel, TiffSaveOptions options, int index,
        int pageCount, bool bigTiff)
    {
        var offsetType = bigTiff ? TiffFieldType.Long8 : TiffFieldType.Long;
        var counts = page.Chunks.Select(chunk => (ulong)chunk.Length).ToArray();
        var colorSamples = pixel.Photometric == TiffPhotometric.Rgb ? 3 : 1;
        List<Entry> entries =
        [
            new(TiffTag.ImageWidth, TiffFieldType.Long, [(ulong)page.Width]),
            new(TiffTag.ImageLength, TiffFieldType.Long, [(ulong)page.Height]),
            new(TiffTag.BitsPerSample, TiffFieldType.Short, Repeat((ulong)pixel.Bits, pixel.Samples)),
            new(TiffTag.Compression, TiffFieldType.Short, [(ulong)options.Compression]),
            new(TiffTag.PhotometricInterpretation, TiffFieldType.Short, [(ulong)pixel.Photometric]),
            new(TiffTag.SamplesPerPixel, TiffFieldType.Short, [(ulong)pixel.Samples]),
            new(TiffTag.XResolution, TiffFieldType.Rational, [72, 1]),
            new(TiffTag.YResolution, TiffFieldType.Rational, [72, 1]),
            new(TiffTag.PlanarConfiguration, TiffFieldType.Short, [1]),
            new(TiffTag.ResolutionUnit, TiffFieldType.Short, [2])
        ];
        if (page.Tiled)
        {
            entries.Add(new(TiffTag.TileWidth, TiffFieldType.Long, [(ulong)page.ChunkWidth]));
            entries.Add(new(TiffTag.TileLength, TiffFieldType.Long, [(ulong)page.ChunkHeight]));
            entries.Add(new(TiffTag.TileOffsets, offsetType, offsets));
            entries.Add(new(TiffTag.TileByteCounts, offsetType, counts));
        }
        else
        {
            entries.Add(new(TiffTag.StripOffsets, offsetType, offsets));
            entries.Add(new(TiffTag.RowsPerStrip, TiffFieldType.Long, [(ulong)page.ChunkHeight]));
            entries.Add(new(TiffTag.StripByteCounts, offsetType, counts));
        }

        if (pageCount > 1)
        {
            entries.Add(new(TiffTag.NewSubfileType, TiffFieldType.Long, [2]));
            entries.Add(new(TiffTag.PageNumber, TiffFieldType.Short, [(ulong)index, (ulong)pageCount]));
        }

        if (page.Predictor != 1)
        {
            entries.Add(new(TiffTag.Predictor, TiffFieldType.Short, [(ulong)page.Predictor]));
        }

        if (pixel.Samples > colorSamples)
        {
            var extra = Repeat(0, pixel.Samples - colorSamples);
            extra[0] = pixel.Alpha ? 2UL : 0UL;
            entries.Add(new(TiffTag.ExtraSamples, TiffFieldType.Short, extra));
        }

        if (pixel.Kind != TiffSampleFormat.UnsignedInteger)
        {
            entries.Add(new(TiffTag.SampleFormat, TiffFieldType.Short, Repeat((ulong)pixel.Kind, pixel.Samples)));
        }

        entries.Sort((a, b) => a.Tag.CompareTo(b.Tag));
        return entries;
    }

    private static byte[] Directory(List<Entry> entries, long at, bool bigTiff)
    {
        var countBytes = bigTiff ? 8 : 2;
        var entryBytes = bigTiff ? 20 : 12;
        var inline = bigTiff ? 8 : 4;
        var tableBytes = countBytes + entries.Count * entryBytes + inline;
        List<byte> outOfLine = [];
        var table = new byte[tableBytes];
        if (bigTiff)
        {
            BinaryPrimitives.WriteUInt64LittleEndian(table, (ulong)entries.Count);
        }
        else
        {
            BinaryPrimitives.WriteUInt16LittleEndian(table, (ushort)entries.Count);
        }

        for (var i = 0; i < entries.Count; i++)
        {
            var entry = entries[i];
            var slot = table.AsSpan(countBytes + i * entryBytes, entryBytes);
            var values = Serialize(entry);
            var count = entry.Type is TiffFieldType.Rational ? entry.Values.Length / 2 : entry.Values.Length;
            BinaryPrimitives.WriteUInt16LittleEndian(slot, (ushort)entry.Tag);
            BinaryPrimitives.WriteUInt16LittleEndian(slot[2..], (ushort)entry.Type);
            var value = slot[(bigTiff ? 12 : 8)..];
            if (bigTiff)
            {
                BinaryPrimitives.WriteUInt64LittleEndian(slot[4..], (ulong)count);
            }
            else
            {
                BinaryPrimitives.WriteUInt32LittleEndian(slot[4..], (uint)count);
            }

            if (values.Length <= inline)
            {
                values.CopyTo(value);
                continue;
            }

            var offset = (ulong)(at + tableBytes + outOfLine.Count);
            if (bigTiff)
            {
                BinaryPrimitives.WriteUInt64LittleEndian(value, offset);
            }
            else
            {
                BinaryPrimitives.WriteUInt32LittleEndian(value, (uint)offset);
            }

            outOfLine.AddRange(values);
            if ((outOfLine.Count & 1) != 0)
            {
                outOfLine.Add(0);
            }
        }

        return [.. table, .. outOfLine];
    }

    private static int DirectoryNextOffsetAt(byte[] directory, bool bigTiff)
    {
        var count = bigTiff
            ? (int)BinaryPrimitives.ReadUInt64LittleEndian(directory)
            : BinaryPrimitives.ReadUInt16LittleEndian(directory);
        return (bigTiff ? 8 : 2) + count * (bigTiff ? 20 : 12);
    }

    private static byte[] Serialize(Entry entry)
    {
        var size = entry.Type == TiffFieldType.Rational ? 4 : TiffField.SizeOf(entry.Type);
        var bytes = new byte[entry.Values.Length * size];
        for (var i = 0; i < entry.Values.Length; i++)
        {
            var at = bytes.AsSpan(i * size);
            switch (size)
            {
                case 2:
                    BinaryPrimitives.WriteUInt16LittleEndian(at, (ushort)entry.Values[i]);
                    break;
                case 4:
                    BinaryPrimitives.WriteUInt32LittleEndian(at, (uint)entry.Values[i]);
                    break;
                default:
                    BinaryPrimitives.WriteUInt64LittleEndian(at, entry.Values[i]);
                    break;
            }
        }

        return bytes;
    }

    private static ulong[] Repeat(ulong value, int count) => Enumerable.Repeat(value, count).ToArray();

    private static PixelLayout Describe(SurfaceFormat format)
    {
        var unsigned = TiffSampleFormat.UnsignedInteger;
        var signed = TiffSampleFormat.SignedInteger;
        var real = TiffSampleFormat.Float;
        var gray = TiffPhotometric.MinIsBlack;
        var rgb = TiffPhotometric.Rgb;
        return (Format)format switch
        {
            Format.R8_UNORM or Format.R8_UINT => new(1, 8, unsigned, gray),
            Format.R8_SINT => new(1, 8, signed, gray),
            Format.R8G8B8_UNORM or Format.R8G8B8_SRGB => new(3, 8, unsigned, rgb),
            Format.B8G8R8_UNORM or Format.B8G8R8_SRGB => new(3, 8, unsigned, rgb) { Bgr = true },
            Format.R8G8B8A8_UNORM or Format.R8G8B8A8_SRGB => new(4, 8, unsigned, rgb) { Alpha = true },
            Format.B8G8R8A8_UNORM or Format.B8G8R8A8_SRGB => new(4, 8, unsigned, rgb) { Alpha = true, Bgr = true },
            Format.R16_UNORM or Format.R16_UINT => new(1, 16, unsigned, gray),
            Format.R16_SINT => new(1, 16, signed, gray),
            Format.R16_SFLOAT => new(1, 16, real, gray),
            Format.R16G16B16_UNORM => new(3, 16, unsigned, rgb),
            Format.R16G16B16A16_UNORM => new(4, 16, unsigned, rgb) { Alpha = true },
            Format.R16G16B16A16_SFLOAT => new(4, 16, real, rgb) { Alpha = true },
            Format.R32_UINT => new(1, 32, unsigned, gray),
            Format.R32_SINT => new(1, 32, signed, gray),
            Format.R32_SFLOAT => new(1, 32, real, gray),
            Format.R32G32B32_SFLOAT => new(3, 32, real, rgb),
            Format.R32G32B32A32_SFLOAT => new(4, 32, real, rgb) { Alpha = true },
            Format.R64_UINT => new(1, 64, unsigned, gray),
            Format.R64_SINT => new(1, 64, signed, gray),
            Format.R64_SFLOAT => new(1, 64, real, gray),
            _ => throw new NotSupportedException($"Writing {format} as TIFF.")
        };
    }

    private sealed record PixelLayout(int Samples, int Bits, TiffSampleFormat Kind, TiffPhotometric Photometric)
    {
        public bool Alpha { get; init; }

        public bool Bgr { get; init; }

        public int PixelBytes => Samples * Bits / 8;
    }

    private sealed record Page(int Width, int Height, int ChunkWidth, int ChunkHeight, bool Tiled, int Predictor,
        List<byte[]> Chunks);

    private sealed record Entry(TiffTag Tag, TiffFieldType Type, ulong[] Values);
}
