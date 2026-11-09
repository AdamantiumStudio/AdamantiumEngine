using System;
using System.IO;

namespace Adamantium.Imaging.Jpeg2000;

/// <summary>The SIZ marker (ITU-T T.800, A.5.1): the reference grid, the tiles and the components.</summary>
internal sealed class J2kSize
{
    private const int MaxTiles = 65535;

    public int X0 { get; private init; }

    public int Y0 { get; private init; }

    public int X1 { get; private init; }

    public int Y1 { get; private init; }

    public int TileX0 { get; private init; }

    public int TileY0 { get; private init; }

    public int TileWidth { get; private init; }

    public int TileHeight { get; private init; }

    public int TilesAcross { get; private init; }

    public int TilesDown { get; private init; }

    public J2kComponentInfo[] Components { get; private init; }

    public int Width => X1 - X0;

    public int Height => Y1 - Y0;

    public static J2kSize Read(BigEndianReader reader, long maxPixels)
    {
        reader.Skip(2);
        var x1 = reader.ReadUInt32();
        var y1 = reader.ReadUInt32();
        var x0 = reader.ReadUInt32();
        var y0 = reader.ReadUInt32();
        var tileWidth = reader.ReadUInt32();
        var tileHeight = reader.ReadUInt32();
        var tileX0 = reader.ReadUInt32();
        var tileY0 = reader.ReadUInt32();
        var count = reader.ReadUInt16();
        if (x1 > int.MaxValue || y1 > int.MaxValue || x0 >= x1 || y0 >= y1 || tileWidth == 0 || tileHeight == 0 ||
            tileX0 > x0 || tileY0 > y0 || tileX0 + (long)tileWidth <= x0 || tileY0 + (long)tileHeight <= y0 ||
            count is 0 or > 16384)
        {
            throw new InvalidDataException("A JPEG 2000 image has a size it cannot have.");
        }

        if ((long)(x1 - x0) * (y1 - y0) > maxPixels)
        {
            throw new InvalidDataException("A JPEG 2000 image of " + (x1 - x0) + " by " + (y1 - y0) +
                                           " is too big.");
        }

        var across = (x1 - tileX0 + (long)tileWidth - 1) / tileWidth;
        var down = (y1 - tileY0 + (long)tileHeight - 1) / tileHeight;
        if (across * down > MaxTiles)
        {
            throw new InvalidDataException("A JPEG 2000 image has more tiles than a codestream can.");
        }

        var components = new J2kComponentInfo[count];
        for (var i = 0; i < count; i++)
        {
            var depth = reader.ReadByte();
            var dx = reader.ReadByte();
            var dy = reader.ReadByte();
            if ((depth & 0x7F) > 37 || dx == 0 || dy == 0)
            {
                throw new InvalidDataException("A JPEG 2000 component has a depth or subsampling it cannot have.");
            }

            components[i] = new J2kComponentInfo((depth & 0x7F) + 1, (depth & 0x80) != 0, dx, dy);
        }

        return new J2kSize
        {
            X0 = (int)x0,
            Y0 = (int)y0,
            X1 = (int)x1,
            Y1 = (int)y1,
            TileX0 = (int)tileX0,
            TileY0 = (int)tileY0,
            TileWidth = (int)Math.Min(tileWidth, int.MaxValue),
            TileHeight = (int)Math.Min(tileHeight, int.MaxValue),
            TilesAcross = (int)across,
            TilesDown = (int)down,
            Components = components,
        };
    }

    /// <summary>The bounds of tile <paramref name="index"/> on the reference grid (ITU-T T.800, B.3).</summary>
    public (int X0, int Y0, int X1, int Y1) Tile(int index)
    {
        var p = index % TilesAcross;
        var q = index / TilesAcross;
        return ((int)Math.Max(TileX0 + (long)p * TileWidth, X0), (int)Math.Max(TileY0 + (long)q * TileHeight, Y0),
            (int)Math.Min(TileX0 + (p + 1L) * TileWidth, X1), (int)Math.Min(TileY0 + (q + 1L) * TileHeight, Y1));
    }
}
