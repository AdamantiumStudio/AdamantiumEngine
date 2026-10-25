using System;
using System.IO;
using System.Linq;

namespace Adamantium.Imaging.Tiff;

/// <summary>How one TIFF image is laid out, from its directory: size, samples, color model, compression and the
/// strips or tiles its pixels are in.</summary>
internal sealed class TiffLayout
{
    /// <summary>The largest tile side taken beyond the image's own size: the most a JPEG tile can have (its frame size
    /// is 16-bit), more than any writer uses - a tile far past the image would only cost memory to decode.</summary>
    public const int MaxTileSide = 65536;

    private TiffLayout(TiffDirectory directory)
    {
        Directory = directory;
    }

    /// <summary>The directory the layout was read from.</summary>
    public TiffDirectory Directory { get; }

    /// <summary>Width in pixels.</summary>
    public int Width { get; private init; }

    /// <summary>Height in pixels.</summary>
    public int Height { get; private init; }

    /// <summary>Samples per pixel, color and extra together.</summary>
    public int SamplesPerPixel { get; private init; }

    /// <summary>Bits of each sample; every sample has the same.</summary>
    public int BitsPerSample { get; private init; }

    /// <summary>The kind of each sample.</summary>
    public TiffSampleFormat SampleFormat { get; private init; }

    /// <summary>The color model.</summary>
    public TiffPhotometric Photometric { get; private init; }

    /// <summary>How the strips or tiles are compressed.</summary>
    public TiffCompression Compression { get; private init; }

    /// <summary>1 - none, 2 - horizontal differences, 3 - floating-point differences.</summary>
    public int Predictor { get; private init; }

    /// <summary>Whether each sample is in planes of its own.</summary>
    public bool IsPlanar { get; private init; }

    /// <summary>Whether the bits of each byte run least significant first.</summary>
    public bool IsBitReversed { get; private init; }

    /// <summary>1 to 8, as the Orientation tag numbers them.</summary>
    public int Orientation { get; private init; }

    /// <summary>What each sample past the color model is: 0 - unspecified, 1 - associated alpha, 2 - unassociated
    /// alpha.</summary>
    public int[] ExtraSamples { get; private init; }

    /// <summary>The palette, 3 × 2^bits values of 16 bits; null for other color models.</summary>
    public ushort[] ColorMap { get; private init; }

    /// <summary>Chroma subsampling across and down for YCbCr.</summary>
    public (int X, int Y) Subsampling { get; private init; }

    /// <summary>Whether the pixels are in tiles rather than strips.</summary>
    public bool IsTiled { get; private init; }

    /// <summary>A strip's or tile's width; a strip is the image's width.</summary>
    public int ChunkWidth { get; private init; }

    /// <summary>A strip's or tile's height.</summary>
    public int ChunkHeight { get; private init; }

    /// <summary>Strips or tiles across the image.</summary>
    public int ChunksAcross { get; private init; }

    /// <summary>Strips or tiles down the image.</summary>
    public int ChunksDown { get; private init; }

    /// <summary>Where each strip or tile starts, plane after plane when planar.</summary>
    public ulong[] ChunkOffsets { get; private init; }

    /// <summary>How long each strip or tile is, compressed.</summary>
    public ulong[] ChunkByteCounts { get; private init; }

    /// <summary>The color model of the decoded samples: JPEG strips of YCbCr come out RGB.</summary>
    public TiffPhotometric DecodedPhotometric =>
        Compression == TiffCompression.Jpeg && Photometric == TiffPhotometric.YCbCr ? TiffPhotometric.Rgb : Photometric;

    /// <summary>The sample that is alpha, the first extra one marked so, and whether the colors are premultiplied by it;
    /// (-1, false) without one.</summary>
    public (int Index, bool Associated) Alpha
    {
        get
        {
            var extra = Array.FindIndex(ExtraSamples, kind => kind is 1 or 2);
            return extra < 0 ? (-1, false) : (ColorSamples + extra, ExtraSamples[extra] == 1);
        }
    }

    /// <summary>The samples of the color model, without the extra ones.</summary>
    public int ColorSamples => SamplesPerPixel - ExtraSamples.Length;

    /// <summary>Samples stored in one strip or tile.</summary>
    public int SamplesPerChunk => IsPlanar ? 1 : SamplesPerPixel;

    /// <summary>Bytes in one decompressed row of a strip or tile.</summary>
    public int ChunkRowBytes => (int)(((long)ChunkWidth * SamplesPerChunk * BitsPerSample + 7) / 8);

    /// <summary>Reads the layout from <paramref name="directory"/>; a broken one throws
    /// <see cref="InvalidDataException"/>, one the reader cannot take <see cref="NotSupportedException"/>.</summary>
    public static TiffLayout Read(TiffDirectory directory)
    {
        var width = Dimension(directory.GetRequired(TiffTag.ImageWidth), "width");
        var height = Dimension(directory.GetRequired(TiffTag.ImageLength), "height");
        var samples = (int)Math.Clamp(directory.GetUnsigned(TiffTag.SamplesPerPixel, 1), 1, 65535);
        var bits = Same(directory.GetUnsignedValues(TiffTag.BitsPerSample), 1, "bits per sample");
        var format = (TiffSampleFormat)Same(directory.GetUnsignedValues(TiffTag.SampleFormat), 1, "sample format");
        if (format == TiffSampleFormat.Undefined)
        {
            format = TiffSampleFormat.UnsignedInteger;
        }

        if (!Enum.IsDefined(format))
        {
            throw new NotSupportedException($"TIFF samples of format {(int)format} (complex numbers) are not read.");
        }

        if (bits is < 1 or > 64 || format == TiffSampleFormat.Float && bits is not (16 or 24 or 32 or 64))
        {
            throw new NotSupportedException($"TIFF samples of {bits} bits, {format}, are not read.");
        }

        var compression = (TiffCompression)directory.GetUnsigned(TiffTag.Compression, 1);
        var photometric = directory[TiffTag.PhotometricInterpretation] is { Count: > 0 } given
            ? (TiffPhotometric)given.GetUnsigned(0)
            : samples >= 3 ? TiffPhotometric.Rgb : TiffPhotometric.MinIsBlack;
        var extra = directory.GetUnsignedValues(TiffTag.ExtraSamples)?.Select(value => (int)value).ToArray() ?? [];
        var colorSamples = photometric switch
        {
            TiffPhotometric.Rgb or TiffPhotometric.YCbCr or TiffPhotometric.CieLab or TiffPhotometric.IccLab
                or TiffPhotometric.ItuLab => 3,
            TiffPhotometric.Separated => Math.Max(1, samples - extra.Length),
            _ => 1
        };
        if (extra.Length > samples - colorSamples)
        {
            extra = extra[..Math.Max(0, samples - colorSamples)];
        }

        if (samples < colorSamples)
        {
            throw new InvalidDataException($"A TIFF {photometric} image has {samples} samples per pixel.");
        }

        if (samples > colorSamples + extra.Length)
        {
            extra = [.. extra, .. new int[samples - colorSamples - extra.Length]];
        }

        var planar = directory.GetUnsigned(TiffTag.PlanarConfiguration, 1) == 2 && samples > 1;
        var tiled = directory[TiffTag.TileWidth] != null;
        int chunkWidth;
        int chunkHeight;
        ulong[] offsets;
        ulong[] counts;
        if (tiled)
        {
            chunkWidth = TileSide(directory.GetRequired(TiffTag.TileWidth), width, "width");
            chunkHeight = TileSide(directory.GetRequired(TiffTag.TileLength), height, "height");
            offsets = directory.GetUnsignedValues(TiffTag.TileOffsets);
            counts = directory.GetUnsignedValues(TiffTag.TileByteCounts);
        }
        else
        {
            chunkWidth = width;
            chunkHeight = (int)Math.Clamp(directory.GetUnsigned(TiffTag.RowsPerStrip, (ulong)height), 1UL, (ulong)height);
            offsets = directory.GetUnsignedValues(TiffTag.StripOffsets);
            counts = directory.GetUnsignedValues(TiffTag.StripByteCounts);
        }

        var chunkRowBytes = ((long)chunkWidth * (planar ? 1 : samples) * bits + 7) / 8;
        var imageRowBytes = (((long)width + 15) / 16 * 16 * (planar ? 1 : samples) * bits + 7) / 8;
        if (chunkRowBytes > Array.MaxLength || tiled && chunkRowBytes > Math.Max(imageRowBytes, MaxTileSide * 8L))
        {
            throw new InvalidDataException($"A TIFF strip or tile row of {chunkRowBytes} bytes in an image of "
                                           + $"{width} pixels across.");
        }

        var across = (int)(((long)width + chunkWidth - 1) / chunkWidth);
        var down = (int)(((long)height + chunkHeight - 1) / chunkHeight);
        var chunks = (long)across * down * (planar ? samples : 1);
        if (offsets == null || offsets.Length < chunks)
        {
            throw new InvalidDataException($"The TIFF image needs {chunks} strips or tiles, its offsets list "
                                           + $"{offsets?.Length ?? 0}.");
        }

        if (counts == null || counts.Length < chunks)
        {
            if (compression != TiffCompression.None)
            {
                throw new InvalidDataException("The TIFF image is compressed but does not say how long its strips are.");
            }

            var rowBytes = (ulong)chunkRowBytes;
            counts = new ulong[chunks];
            for (var i = 0; i < counts.Length; i++)
            {
                var row = i / across % down;
                var rows = tiled ? chunkHeight : Math.Min(chunkHeight, height - row * chunkHeight);
                counts[i] = rowBytes * (ulong)rows;
            }
        }

        var layout = new TiffLayout(directory)
        {
            Width = width,
            Height = height,
            SamplesPerPixel = samples,
            BitsPerSample = bits,
            SampleFormat = format,
            Photometric = photometric,
            Compression = compression,
            Predictor = (int)directory.GetUnsigned(TiffTag.Predictor, 1),
            IsPlanar = planar,
            IsBitReversed = directory.GetUnsigned(TiffTag.FillOrder, 1) == 2,
            Orientation = (int)Math.Clamp(directory.GetUnsigned(TiffTag.Orientation, 1), 1, 8),
            ExtraSamples = extra,
            ColorMap = directory.GetUnsignedValues(TiffTag.ColorMap)?.Select(value => (ushort)value).ToArray(),
            Subsampling = ReadSubsampling(directory, photometric, compression),
            IsTiled = tiled,
            ChunkWidth = chunkWidth,
            ChunkHeight = chunkHeight,
            ChunksAcross = across,
            ChunksDown = down,
            ChunkOffsets = offsets,
            ChunkByteCounts = counts
        };
        if (photometric == TiffPhotometric.Palette
            && (layout.ColorMap == null || layout.ColorMap.Length < 3 * (1L << Math.Min(bits, 16))))
        {
            throw new InvalidDataException($"The TIFF palette image has no color map of {3 << Math.Min(bits, 16)} "
                                           + "values.");
        }

        return layout;
    }

    /// <summary>The number of the strip or tile holding chunk (<paramref name="across"/>, <paramref name="down"/>) of
    /// <paramref name="plane"/>.</summary>
    public int ChunkIndex(int across, int down, int plane) => (plane * ChunksDown + down) * ChunksAcross + across;

    /// <summary>The rows of chunk row <paramref name="down"/> inside the image - fewer than a strip's or tile's height
    /// at the bottom; a tile's rows past the image are never decoded.</summary>
    public int ChunkRows(int down) => (int)Math.Min(ChunkHeight, Height - (long)down * ChunkHeight);

    private static (int X, int Y) ReadSubsampling(TiffDirectory directory, TiffPhotometric photometric,
        TiffCompression compression)
    {
        if (photometric != TiffPhotometric.YCbCr || compression is TiffCompression.Jpeg or TiffCompression.OldJpeg)
        {
            return (1, 1);
        }

        var values = directory.GetUnsignedValues(TiffTag.YCbCrSubsampling);
        var x = values is { Length: > 0 } ? (int)values[0] : 2;
        var y = values is { Length: > 1 } ? (int)values[1] : 2;
        return x is 1 or 2 or 4 && y is 1 or 2 or 4 && y <= x
            ? (x, y)
            : throw new InvalidDataException($"The TIFF YCbCr subsampling {x}×{y} is not one TIFF allows.");
    }

    private static int TileSide(ulong value, int image, string what)
    {
        var side = Dimension(value, "tile " + what);
        var largest = Math.Max(((long)image + 15) / 16 * 16, MaxTileSide);
        return side <= largest
            ? side
            : throw new InvalidDataException($"A TIFF tile {what} of {side} for an image {what} of {image}.");
    }

    private static int Dimension(ulong value, string what) =>
        value is >= 1 and <= int.MaxValue
            ? (int)value
            : throw new InvalidDataException($"A TIFF image {what} of {value} pixels.");

    private static int Same(ulong[] values, int fallback, string what)
    {
        if (values == null || values.Length == 0)
        {
            return fallback;
        }

        return values.All(value => value == values[0])
            ? (int)Math.Min(values[0], int.MaxValue)
            : throw new NotSupportedException($"TIFF samples with different {what} ({string.Join(", ", values)}) are "
                                              + "not read.");
    }
}
