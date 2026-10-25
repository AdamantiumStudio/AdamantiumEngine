using System;
using System.Collections.Concurrent;
using System.Collections.Generic;
using System.IO;
using System.Linq;

namespace Adamantium.Imaging.Tiff;

/// <summary>Reads a TIFF raster too large to hold whole - a tile or a window at a time, at full resolution or from one
/// of its reduced-resolution copies - and, for GeoTIFF, where it lies. Opening reads only the file's structure. Samples
/// come out as the file holds them, not turned into colors: each in <see cref="TiffLevel.BytesPerSample"/> bytes,
/// little-endian, the bands of a pixel together. Tiles and windows can be read from any number of threads at once.
/// </summary>
public sealed class TiffReader : IDisposable
{
    private const int MaxLevels = 32;

    private readonly ITiffSource _source;
    private readonly bool _littleEndian;
    private readonly TiffLevel[] _levels;
    private readonly ConcurrentBag<TiffChunkDecoder>[] _decoders;
    private volatile bool _disposed;

    private TiffReader(ITiffSource source, bool littleEndian, TiffDirectory page, List<(TiffLevel, TiffChunkDecoder)> levels)
    {
        _source = source;
        _littleEndian = littleEndian;
        _levels = levels.Select(level => level.Item1).ToArray();
        _decoders = levels.Select(ConcurrentBag<TiffChunkDecoder> (level) => [level.Item2]).ToArray();
        Levels = Array.AsReadOnly(_levels);
        GeoReference = TiffTry.Read(() => TiffGeoReference.Read(page), null);
        NoData = TiffTry.Read(() => TiffGdalMetadata.NoData(page), null);
        var (metadata, bands) = TiffTry.Read(() => TiffGdalMetadata.Read(page, _levels[0].Bands),
            TiffGdalMetadata.Read(null, _levels[0].Bands));
        Metadata = metadata;
        Bands = Array.AsReadOnly(bands);
    }

    /// <summary>The full-resolution image first, then its reduced-resolution copies from the largest down.</summary>
    public IReadOnlyList<TiffLevel> Levels { get; }

    /// <summary>Where the full-resolution image lies, for a GeoTIFF; null for a file without its tags.</summary>
    public TiffGeoReference GeoReference { get; }

    /// <summary>The value that means "no data" in every band (GDAL_NODATA); null when the file gives none.</summary>
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
    /// <paramref name="level"/>, any band of it; a tile it leaves out reads as zeros, which may mean no data rather than
    /// a value.</summary>
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
    /// the level (<see cref="TiffLevel.TileArea"/>), and a tile the file leaves out, come out zero.</summary>
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
        tile.Clear();
        var (columns, rows) = at.TileArea(column, row);
        Decode(level, column, row, band, 0, 0, columns, rows, tile, at.TileWidth);
    }

    /// <summary>Reads the window of <paramref name="width"/> × <paramref name="height"/> pixels at
    /// (<paramref name="x"/>, <paramref name="y"/>) of <paramref name="level"/>, all bands, into
    /// <paramref name="destination"/> - <paramref name="width"/> × <paramref name="height"/> ×
    /// <see cref="TiffLevel.Bands"/> × <see cref="TiffLevel.BytesPerSample"/> bytes, rows top to bottom - gathered from
    /// the tiles under it, decoding only their rows inside the level.</summary>
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

        for (var row = y / at.TileHeight; row <= (int)(((long)y + height - 1) / at.TileHeight); row++)
        {
            for (var column = x / at.TileWidth; column <= (int)(((long)x + width - 1) / at.TileWidth); column++)
            {
                var left = Math.Max(x, (long)column * at.TileWidth);
                var right = Math.Min((long)x + width, ((long)column + 1) * at.TileWidth);
                var top = Math.Max(y, (long)row * at.TileHeight);
                var bottom = Math.Min((long)y + height, ((long)row + 1) * at.TileHeight);
                var into = destination[(int)(((top - y) * width + left - x) * at.Bands * at.BytesPerSample)..];
                Decode(level, column, row, -1, (int)(left - (long)column * at.TileWidth),
                    (int)(top - (long)row * at.TileHeight), (int)(right - left), (int)(bottom - top), into, width);
            }
        }
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
        var decoder = _decoders[level].TryTake(out var pooled)
            ? pooled
            : new TiffChunkDecoder(_source, at.Layout, _littleEndian);
        try
        {
            for (var line = 0; line < height; line++)
            {
                destination.Slice((int)((long)line * destinationWidth * outBands * size), width * outBands * size)
                    .Clear();
            }

            IEnumerable<int> planes = at.Layout.IsPlanar ? band < 0 ? Enumerable.Range(0, at.Bands) : [band] : [0];
            foreach (var plane in planes.Where(plane => decoder.Holds(column, row, plane)))
            {
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

                    for (var x = 0; x < width; x++)
                    {
                        decoder.Native.AsSpan((int)(from + ((long)x * inBands + firstBand) * size), bandsCopied * size)
                            .CopyTo(destination[(int)(to + ((long)x * outBands + outBand) * size)..]);
                    }
                }
            }
        }
        finally
        {
            if (_decoders[level].Count < Environment.ProcessorCount)
            {
                _decoders[level].Add(decoder);
            }
        }
    }
}
