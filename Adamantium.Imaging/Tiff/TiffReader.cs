using System;
using System.Buffers.Binary;
using System.Collections.Concurrent;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Runtime.CompilerServices;
using System.Runtime.ExceptionServices;
using System.Runtime.InteropServices;
using System.Threading;
using System.Threading.Tasks;

namespace Adamantium.Imaging.Tiff;

/// <summary>Reads a TIFF raster too large to hold whole - a tile or a window at a time, at full resolution or from one
/// of its reduced-resolution copies - and, for GeoTIFF, where it lies. Opening reads only the file's structure. Samples
/// come out as the file holds them, not turned into colors: each in <see cref="TiffLevel.BytesPerSample"/> bytes,
/// little-endian, the bands of a pixel together. Tiles and windows can be read from any number of threads at once.
/// </summary>
public sealed class TiffReader : IDisposable
{
    private const int MaxLevels = 32;

    // What the decoders of one level keep in buffers, decoding at once and pooled - at least one decoder, whatever its
    // strips or tiles take.
    private const long DecoderMemory = 64L << 20;

    private readonly ITiffSource _source;
    private readonly bool _littleEndian;
    private readonly TiffLevel[] _levels;
    private readonly ConcurrentBag<TiffChunkDecoder>[] _decoders;
    private readonly int[] _pooled;
    private readonly int[] _decoderLimits;
    private readonly byte[][] _fills;
    private volatile bool _disposed;

    private TiffReader(ITiffSource source, bool littleEndian, TiffDirectory page, List<(TiffLevel, TiffChunkDecoder)> levels)
    {
        _source = source;
        _littleEndian = littleEndian;
        _levels = levels.Select(level => level.Item1).ToArray();
        _decoders = levels.Select(ConcurrentBag<TiffChunkDecoder> (level) => [level.Item2]).ToArray();
        _pooled = levels.Select(_ => 1).ToArray();
        _decoderLimits = levels.Select(level => (int)Math.Clamp(DecoderMemory / Math.Max(1,
                2 * level.Item2.NativeChunkBytes + TiffChunkDecoder.InputBytes(level.Item1.Layout)), 1,
            Environment.ProcessorCount)).ToArray();
        Levels = Array.AsReadOnly(_levels);
        GeoReference = TiffTry.Read(() => TiffGeoReference.Read(page), null);
        NoData = AsSample(TiffTry.Read(() => TiffGdalMetadata.NoData(page), null), _levels[0]);
        var exact = TiffTry.Read(() => TiffGdalMetadata.IntegerNoData(page), default);
        _fills = _levels.Select(level => Fill(NoData, exact, level)).ToArray();
        var (metadata, bands) = TiffTry.Read(() => TiffGdalMetadata.Read(page, _levels[0].Bands),
            TiffGdalMetadata.Read(null, _levels[0].Bands));
        Metadata = metadata;
        Bands = Array.AsReadOnly(bands);
    }

    /// <summary>The full-resolution image first, then its reduced-resolution copies from the largest down.</summary>
    public IReadOnlyList<TiffLevel> Levels { get; }

    /// <summary>Where the full-resolution image lies, for a GeoTIFF; null for a file without its tags.</summary>
    public TiffGeoReference GeoReference { get; }

    /// <summary>The value that means "no data" in every band (GDAL_NODATA), as the samples hold it - rounded to a
    /// float of 16, 24 or 32 bits for samples of that size; integers of 64 bits past 2^53 only as near as a double
    /// comes. Null when the file gives none.</summary>
    public double? NoData { get; }

    /// <summary>GDAL's metadata of the whole raster, by item name.</summary>
    public IReadOnlyDictionary<string, string> Metadata { get; }

    /// <summary>What GDAL's metadata says of each band: scale, offset, description.</summary>
    public IReadOnlyList<TiffBandInfo> Bands { get; }

    /// <summary>Opens the TIFF file at <paramref name="path"/>. A file that is not TIFF, or whose first image is broken
    /// or uses what is not read, throws <see cref="InvalidDataException"/> or <see cref="NotSupportedException"/>;
    /// reduced-resolution copies that cannot be read are left out of <see cref="Levels"/>.</summary>
    public static TiffReader Open(string path) => Open(new TiffFileSource(path));

    /// <summary>Opens the TIFF file in <paramref name="stream"/>, which must be able to seek; the file starts at its
    /// position 0, and reads from it take turns. Without <paramref name="leaveOpen"/>, the stream is closed with the
    /// reader, or at once when it cannot be opened.</summary>
    public static TiffReader Open(Stream stream, bool leaveOpen = false)
    {
        ArgumentNullException.ThrowIfNull(stream);
        TiffStreamSource source;
        try
        {
            source = new TiffStreamSource(stream, leaveOpen);
        }
        catch
        {
            if (!leaveOpen)
            {
                stream.Dispose();
            }

            throw;
        }

        return Open(source);
    }

    /// <summary>The reference of <paramref name="level"/>: <see cref="GeoReference"/> with a pixel of that level's
    /// size; null without one.</summary>
    public TiffGeoReference GeoReferenceOf(int level)
    {
        var at = Level(level);
        return GeoReference?.Resampled(_levels[0].Width, _levels[0].Height, at.Width, at.Height);
    }

    /// <summary>Whether the file holds tile (<paramref name="column"/>, <paramref name="row"/>) of
    /// <paramref name="level"/>, any band of it. A tile it leaves out reads as <see cref="NoData"/> when the samples can
    /// hold that value, else as zeros.</summary>
    public bool HasTile(int level, int column, int row)
    {
        var at = Level(level);
        CheckTile(at, column, row);
        var planes = at.Layout.IsPlanar ? at.Bands : 1;
        return Enumerable.Range(0, planes).Any(plane =>
        {
            var index = at.Layout.ChunkIndex(column, row, plane);
            return at.Layout.ChunkByteCounts[index] != 0 && at.Layout.ChunkOffsets[index] != 0;
        });
    }

    /// <summary>Reads tile (<paramref name="column"/>, <paramref name="row"/>) of <paramref name="level"/>, all bands,
    /// into the first <see cref="TiffLevel.TileBytes"/> of <paramref name="destination"/>. Pixels of the tile outside
    /// the level (<see cref="TiffLevel.TileArea"/>) come out zero; a tile the file leaves out reads as
    /// <see cref="HasTile"/> says.</summary>
    public void ReadTile(int level, int column, int row, Span<byte> destination) =>
        ReadTile(level, column, row, -1, destination);

    /// <summary>Reads band <paramref name="band"/> of tile (<paramref name="column"/>, <paramref name="row"/>) of
    /// <paramref name="level"/> into <paramref name="destination"/>: <see cref="TiffLevel.TileWidth"/> ×
    /// <see cref="TiffLevel.TileHeight"/> samples. A file with each band in planes of its own reads only that band's.
    /// </summary>
    public void ReadTile(int level, int column, int row, int band, Span<byte> destination)
    {
        var at = Level(level);
        CheckTile(at, column, row);
        if (band < -1 || band >= at.Bands)
        {
            throw new ArgumentOutOfRangeException(nameof(band), $"The TIFF level has {at.Bands} bands.");
        }

        var bands = band < 0 ? at.Bands : 1;
        var length = (long)at.TileWidth * at.TileHeight * bands * at.BytesPerSample;
        if (length > Array.MaxLength)
        {
            throw new NotSupportedException($"A tile of {length} bytes is more than one array holds; read windows of "
                                            + "it instead.");
        }

        if (destination.Length < length)
        {
            throw new ArgumentException($"The tile takes {length} bytes.", nameof(destination));
        }

        var tile = destination[..(int)length];
        var (columns, rows) = at.TileArea(column, row);
        if (columns < at.TileWidth || rows < at.TileHeight)
        {
            tile.Clear();
        }

        Decode(level, column, row, band, 0, 0, columns, rows, tile, at.TileWidth);
    }

    /// <summary>Reads the window of <paramref name="width"/> × <paramref name="height"/> pixels at
    /// (<paramref name="x"/>, <paramref name="y"/>) of <paramref name="level"/>, all bands, into
    /// <paramref name="destination"/> - <paramref name="width"/> × <paramref name="height"/> ×
    /// <see cref="TiffLevel.Bands"/> × <see cref="TiffLevel.BytesPerSample"/> bytes, rows top to bottom - gathered from
    /// the tiles under it, decoding only their rows inside the level - the tiles on the thread pool, as many at once as
    /// 64 MB of decoding buffers allow.</summary>
    public void ReadWindow(int level, int x, int y, int width, int height, Span<byte> destination)
    {
        var at = Level(level);
        if (x < 0 || y < 0 || width < 0 || height < 0 || x > at.Width - width || y > at.Height - height)
        {
            throw new ArgumentOutOfRangeException(width < 0 || x > at.Width - width ? nameof(width) : nameof(height),
                $"The window {width}×{height} at ({x}, {y}) is not inside the level's {at.Width}×{at.Height}.");
        }

        var length = (long)width * height * at.Bands * at.BytesPerSample;
        if (destination.Length < length)
        {
            throw new ArgumentException($"The window takes {length} bytes.", nameof(destination));
        }

        if (width == 0 || height == 0)
        {
            return;
        }

        var firstColumn = x / at.TileWidth;
        var firstRow = y / at.TileHeight;
        var columns = (int)(((long)x + width - 1) / at.TileWidth) - firstColumn + 1;
        var tiles = (long)columns * ((int)(((long)y + height - 1) / at.TileHeight) - firstRow + 1);
        if (tiles == 1)
        {
            DecodeInto(level, firstColumn, firstRow, x, y, width, height, destination);
            return;
        }

        unsafe
        {
            fixed (byte* pointer = destination)
            {
                var address = (nint)pointer;
                var bytes = destination.Length;
                try
                {
                    var options = new ParallelOptions { MaxDegreeOfParallelism = _decoderLimits[level] };
                    Parallel.For(0L, tiles, options, tile => DecodeInto(level, firstColumn + (int)(tile % columns),
                        firstRow + (int)(tile / columns), x, y, width, height, new Span<byte>((byte*)address, bytes)));
                }
                catch (AggregateException failure) when (failure.InnerExceptions.Count > 0)
                {
                    ExceptionDispatchInfo.Capture(failure.InnerExceptions[0]).Throw();
                }
            }
        }
    }

    private void DecodeInto(int level, int column, int row, int x, int y, int width, int height,
        Span<byte> destination)
    {
        var at = _levels[level];
        var left = Math.Max(x, (long)column * at.TileWidth);
        var right = Math.Min((long)x + width, ((long)column + 1) * at.TileWidth);
        var top = Math.Max(y, (long)row * at.TileHeight);
        var bottom = Math.Min((long)y + height, ((long)row + 1) * at.TileHeight);
        var into = destination[(int)(((top - y) * width + left - x) * at.Bands * at.BytesPerSample)..];
        Decode(level, column, row, -1, (int)(left - (long)column * at.TileWidth),
            (int)(top - (long)row * at.TileHeight), (int)(right - left), (int)(bottom - top), into, width);
    }

    /// <summary>Closes the file, or the stream unless it was to be left open; reading after throws
    /// <see cref="ObjectDisposedException"/>.</summary>
    public void Dispose()
    {
        _disposed = true;
        (_source as IDisposable)?.Dispose();
    }

    private static TiffReader Open(ITiffSource source)
    {
        try
        {
            var file = TiffFile.Open(source);
            var page = file.Directories.FirstOrDefault(directory => directory.IsPage) ?? file.Directories[0];
            var first = new TiffLevel(TiffLayout.Read(page));
            List<(TiffLevel, TiffChunkDecoder)> levels =
                [(first, new TiffChunkDecoder(source, first.Layout, file.LittleEndian))];
            foreach (var directory in ReducedCopies(file, page))
            {
                if (levels.Count == MaxLevels)
                {
                    break;
                }

                var previous = levels[^1].Item1;
                var copy = TiffTry.Read(() => Readable(directory, file, source, previous), default);
                if (copy.Item1 != null)
                {
                    levels.Add(copy);
                }
            }

            return new TiffReader(source, file.LittleEndian, page, levels);
        }
        catch
        {
            (source as IDisposable)?.Dispose();
            throw;
        }
    }

    private static IEnumerable<TiffDirectory> ReducedCopies(TiffFile file, TiffDirectory page)
    {
        var chain = file.Directories.SkipWhile(directory => directory != page).Skip(1)
            .TakeWhile(directory => !directory.IsPage);
        var children = (page.GetUnsignedValues(TiffTag.SubIfds) ?? []).Distinct().Take(MaxLevels)
            .Select(offset => TiffTry.Read(() => file.ReadDirectory((long)offset), null))
            .Where(directory => directory != null);
        HashSet<long> seen = [page.Offset];
        return chain.Concat(children)
            .Where(directory => directory.IsReducedCopy && seen.Add(directory.Offset))
            .Select(directory => (directory, Width: TiffTry.Read(() => directory.GetUnsigned(TiffTag.ImageWidth, 0), 0UL)))
            .OrderByDescending(candidate => candidate.Width)
            .Select(candidate => candidate.directory);
    }

    private static (TiffLevel, TiffChunkDecoder) Readable(TiffDirectory directory, TiffFile file, ITiffSource source,
        TiffLevel previous)
    {
        if (directory.GetUnsigned(TiffTag.ImageWidth, 0) >= (ulong)previous.Width
            || directory.GetUnsigned(TiffTag.ImageLength, 0) > (ulong)previous.Height)
        {
            return default;
        }

        var level = new TiffLevel(TiffLayout.Read(directory));
        if (level.Bands != previous.Bands || level.BitsPerSample != previous.BitsPerSample
                                          || level.SampleFormat != previous.SampleFormat)
        {
            return default;
        }

        return (level, new TiffChunkDecoder(source, level.Layout, file.LittleEndian));
    }

    private TiffLevel Level(int level)
    {
        ObjectDisposedException.ThrowIf(_disposed, this);
        return (uint)level < (uint)_levels.Length
            ? _levels[level]
            : throw new ArgumentOutOfRangeException(nameof(level), $"The TIFF raster has {_levels.Length} levels.");
    }

    private static void Gather(ReadOnlySpan<byte> from, int fromStep, Span<byte> to, int toStep, int count, int bytes)
    {
        if (count == 0)
        {
            return;
        }

        if ((long)(count - 1) * fromStep + bytes > from.Length || (long)(count - 1) * toStep + bytes > to.Length)
        {
            throw new ArgumentOutOfRangeException(nameof(count));
        }

        ref var source = ref MemoryMarshal.GetReference(from);
        ref var target = ref MemoryMarshal.GetReference(to);
        switch (bytes)
        {
            case 1:
                for (var i = 0; i < count; i++)
                {
                    Unsafe.Add(ref target, i * toStep) = Unsafe.Add(ref source, i * fromStep);
                }

                break;
            case 2:
                Copy<ushort>(ref source, fromStep, ref target, toStep, count);
                break;
            case 4:
                Copy<uint>(ref source, fromStep, ref target, toStep, count);
                break;
            case 8:
                Copy<ulong>(ref source, fromStep, ref target, toStep, count);
                break;
            default:
                for (var i = 0; i < count; i++)
                {
                    from.Slice(i * fromStep, bytes).CopyTo(to[(i * toStep)..]);
                }

                break;
        }
    }

    private static void Copy<T>(ref byte source, int fromStep, ref byte target, int toStep, int count)
        where T : unmanaged
    {
        for (var i = 0; i < count; i++)
        {
            Unsafe.WriteUnaligned(ref Unsafe.Add(ref target, i * toStep),
                Unsafe.ReadUnaligned<T>(ref Unsafe.Add(ref source, i * fromStep)));
        }
    }

    private static double? AsSample(double? value, TiffLevel level) => value is { } number
                                                                       && level.SampleFormat == TiffSampleFormat.Float
        ? level.BitsPerSample switch
        {
            16 => (double)(Half)number,
            24 => Float24(number),
            32 => (float)number,
            _ => number
        }
        : value;

    // A float of 24 bits: 16 bits of mantissa, exponents from -62 (below them, steps of 2^-78) to 63. The value
    // rounded to it once, to nearest, ties to even; past the largest it is infinite.
    private static float Float24(double value)
    {
        if (!double.IsFinite(value) || value == 0)
        {
            return (float)value;
        }

        var magnitude = Math.Abs(value);
        var step = Math.ScaleB(1.0, Math.Max(Math.ILogB(magnitude), -62) - 16);
        var rounded = Math.Round(magnitude / step, MidpointRounding.ToEven) * step;
        return (float)Math.CopySign(rounded > Math.ScaleB(2 - Math.ScaleB(1.0, -16), 63) ? double.PositiveInfinity
            : rounded, value);
    }

    // The integer, when the text is one, is taken as it is: a double rounds those past 2^53.
    private static byte[] Fill(double? noData, (long? Signed, ulong? Unsigned) exact, TiffLevel level)
    {
        if (noData is not { } value || value == 0)
        {
            return null;
        }

        var size = level.BytesPerSample;
        var sample = new byte[size];
        if (level.SampleFormat == TiffSampleFormat.Float)
        {
            switch (size)
            {
                case 2:
                    BinaryPrimitives.WriteHalfLittleEndian(sample, (Half)value);
                    break;
                case 4:
                    BinaryPrimitives.WriteSingleLittleEndian(sample, (float)value);
                    break;
                default:
                    BinaryPrimitives.WriteDoubleLittleEndian(sample, value);
                    break;
            }

            return sample;
        }

        var bits = level.BitsPerSample;
        var signed = level.SampleFormat == TiffSampleFormat.SignedInteger;
        ulong raw;
        if (signed && exact.Signed is { } whole)
        {
            if (bits < 64 && (whole < -(1L << (bits - 1)) || whole >= 1L << (bits - 1)))
            {
                return null;
            }

            raw = (ulong)whole;
        }
        else if (!signed && exact.Unsigned is { } natural)
        {
            if (bits < 64 && natural >= 1UL << bits)
            {
                return null;
            }

            raw = natural;
        }
        else
        {
            var (low, high) = signed ? (-Math.Pow(2, bits - 1), Math.Pow(2, bits - 1)) : (0, Math.Pow(2, bits));
            if (value != Math.Floor(value) || value < low || value >= high)
            {
                return null;
            }

            raw = signed ? (ulong)(long)value : (ulong)value;
        }

        for (var b = 0; b < size; b++)
        {
            sample[b] = (byte)(raw >> (8 * b));
        }

        return sample;
    }

    private static void CheckTile(TiffLevel at, int column, int row)
    {
        if ((uint)column >= (uint)at.TilesAcross)
        {
            throw new ArgumentOutOfRangeException(nameof(column), $"Column {column} of {at.TilesAcross} tiles across.");
        }

        if ((uint)row >= (uint)at.TilesDown)
        {
            throw new ArgumentOutOfRangeException(nameof(row), $"Row {row} of {at.TilesDown} tiles down.");
        }
    }

    private void Decode(int level, int column, int row, int band, int left, int top, int width, int height,
        Span<byte> destination, int destinationWidth)
    {
        var at = _levels[level];
        var size = at.BytesPerSample;
        var outBands = band < 0 ? at.Bands : 1;
        TiffChunkDecoder decoder;
        if (_decoders[level].TryTake(out var pooled))
        {
            Interlocked.Decrement(ref _pooled[level]);
            decoder = pooled;
        }
        else
        {
            decoder = new TiffChunkDecoder(_source, at.Layout, _littleEndian);
        }

        try
        {
            var (firstPlane, lastPlane) = !at.Layout.IsPlanar ? (0, 0) : band < 0 ? (0, at.Bands - 1) : (band, band);
            var missing = false;
            for (var plane = firstPlane; plane <= lastPlane; plane++)
            {
                missing |= !decoder.Holds(column, row, plane);
            }

            for (var line = 0; missing && line < height; line++)
            {
                var into = destination.Slice((int)((long)line * destinationWidth * outBands * size),
                    width * outBands * size);
                if (_fills[level] == null)
                {
                    into.Clear();
                    continue;
                }

                _fills[level].CopyTo(into);
                for (var filled = size; filled < into.Length; filled *= 2)
                {
                    into[..Math.Min(filled, into.Length - filled)].CopyTo(into[filled..]);
                }
            }

            for (var plane = firstPlane; plane <= lastPlane; plane++)
            {
                if (!decoder.Holds(column, row, plane))
                {
                    continue;
                }

                decoder.Decode(column, row, plane, decoder.Native);
                var inBands = at.Layout.IsPlanar ? 1 : at.Bands;
                var firstBand = at.Layout.IsPlanar ? 0 : Math.Max(band, 0);
                var bandsCopied = at.Layout.IsPlanar || band >= 0 ? 1 : at.Bands;
                var outBand = at.Layout.IsPlanar && band < 0 ? plane : 0;
                var whole = inBands == bandsCopied && outBands == bandsCopied;
                for (var line = 0; line < height; line++)
                {
                    var from = ((long)(top + line) * at.TileWidth + left) * inBands * size;
                    var to = (long)line * destinationWidth * outBands * size;
                    if (whole)
                    {
                        decoder.Native.AsSpan((int)from, width * inBands * size).CopyTo(destination[(int)to..]);
                        continue;
                    }

                    Gather(decoder.Native.AsSpan((int)(from + firstBand * size)), inBands * size,
                        destination[(int)(to + outBand * size)..], outBands * size, width, bandsCopied * size);
                }
            }
        }
        finally
        {
            if (Interlocked.Increment(ref _pooled[level]) <= _decoderLimits[level])
            {
                _decoders[level].Add(decoder);
            }
            else
            {
                Interlocked.Decrement(ref _pooled[level]);
            }
        }
    }
}
