using System;
using System.Collections.Generic;

namespace Adamantium.Imaging.Tiff;

/// <summary>One level of a TIFF raster for <see cref="TiffReader"/>: the full-resolution image or one of its
/// reduced-resolution copies, and how its pixels are laid out in tiles. A file in strips reads as tiles as wide as the
/// image and as tall as a strip.</summary>
public sealed class TiffLevel
{
    internal TiffLevel(TiffLayout layout)
    {
        Layout = layout;
        BytesPerSample = TiffChunkDecoder.ContainerBytes(layout);
        Bands = layout.Subsampling != (1, 1) ? 3 : layout.SamplesPerPixel;
        ColorMap = Array.AsReadOnly(layout.ColorMap ?? []);
        ExtraSamples = Array.AsReadOnly(layout.ExtraSamples);
    }

    /// <summary>Width in pixels.</summary>
    public int Width => Layout.Width;

    /// <summary>Height in pixels.</summary>
    public int Height => Layout.Height;

    /// <summary>A tile's width in pixels.</summary>
    public int TileWidth => Layout.ChunkWidth;

    /// <summary>A tile's height in pixels.</summary>
    public int TileHeight => Layout.ChunkHeight;

    /// <summary>Tiles across the level.</summary>
    public int TilesAcross => Layout.ChunksAcross;

    /// <summary>Tiles down the level.</summary>
    public int TilesDown => Layout.ChunksDown;

    /// <summary>Whether the file holds this level in tiles rather than strips.</summary>
    public bool IsTiled => Layout.IsTiled;

    /// <summary>Samples of each pixel - bands - as they come out: color and extra samples together.</summary>
    public int Bands { get; }

    /// <summary>Bits of each sample in the file.</summary>
    public int BitsPerSample => Layout.BitsPerSample;

    /// <summary>The kind of each sample.</summary>
    public TiffSampleFormat SampleFormat => Layout.SampleFormat;

    /// <summary>Bytes each sample comes out in: 1, 2, 4 or 8, little-endian; floats of 24 bits as 4.</summary>
    public int BytesPerSample { get; }

    /// <summary>What the samples mean as they come out - JPEG of YCbCr comes out RGB.</summary>
    public TiffPhotometric Photometric => Layout.DecodedPhotometric;

    /// <summary>The palette for <see cref="TiffPhotometric.Palette"/>: all red values, then green, then blue, 16 bits
    /// each; empty for other color models.</summary>
    public IReadOnlyList<ushort> ColorMap { get; }

    /// <summary>What each band past the color model is: 0 - unspecified, 1 - alpha the colors are premultiplied by,
    /// 2 - alpha.</summary>
    public IReadOnlyList<int> ExtraSamples { get; }

    /// <summary>The band that is alpha; -1 without one.</summary>
    public int AlphaBand => Layout.Alpha.Index;

    /// <summary>Whether the colors are premultiplied by <see cref="AlphaBand"/>.</summary>
    public bool IsAlphaPremultiplied => Layout.Alpha.Associated;

    /// <summary>How the rows and columns run, 1 to 8 as TIFF numbers them; tiles come out as stored.</summary>
    public int Orientation => Layout.Orientation;

    /// <summary>Bytes of one tile, all bands: <see cref="TileWidth"/> × <see cref="TileHeight"/> × <see cref="Bands"/> ×
    /// <see cref="BytesPerSample"/>.</summary>
    public long TileBytes => (long)TileWidth * TileHeight * Bands * BytesPerSample;

    internal TiffLayout Layout { get; }

    /// <summary>The part of tile (<paramref name="column"/>, <paramref name="row"/>) inside the level: a tile at the
    /// right or bottom edge holds fewer pixels.</summary>
    public (int Width, int Height) TileArea(int column, int row) =>
        ((int)Math.Min(TileWidth, Width - (long)column * TileWidth),
            (int)Math.Min(TileHeight, Height - (long)row * TileHeight));
}
