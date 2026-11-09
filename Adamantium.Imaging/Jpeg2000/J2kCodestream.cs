using System;
using System.Collections.Generic;
using System.IO;

namespace Adamantium.Imaging.Jpeg2000;

/// <summary>A JPEG 2000 codestream (ITU-T T.800, annex A) read into its main header and the data of each tile. A
/// codestream that ends early keeps the tile-parts it has.</summary>
internal sealed class J2kCodestream
{
    private const int Capabilities = 0xFF50;
    private const int StartOfCodestream = 0xFF4F;
    private const int Size = 0xFF51;
    private const int StartOfTile = 0xFF90;
    private const int StartOfData = 0xFF93;
    private const int EndOfCodestream = 0xFFD9;
    private const int PackedMain = 0xFF60;
    private const int PackedTile = 0xFF61;

    private J2kCodestream(J2kSize size, J2kMarkerSet main, J2kTileData[] tiles)
    {
        SizeInfo = size;
        Main = main;
        Tiles = tiles;
    }

    public J2kSize SizeInfo { get; }

    public J2kMarkerSet Main { get; }

    public J2kTileData[] Tiles { get; }

    public static J2kCodestream Read(byte[] data, int start, int end, long maxPixels)
    {
        var reader = new BigEndianReader(data, start, end);
        if (reader.ReadUInt16() != StartOfCodestream || reader.ReadUInt16() != Size)
        {
            throw new InvalidDataException("A JPEG 2000 codestream does not start with SOC and SIZ.");
        }

        var size = J2kSize.Read(Segment(reader), maxPixels);
        var main = new J2kMarkerSet();
        var packedMain = new SortedDictionary<int, byte[]>();
        int marker;
        while ((marker = reader.ReadUInt16()) != StartOfTile)
        {
            if (marker == EndOfCodestream)
            {
                throw new InvalidDataException("A JPEG 2000 codestream has no tiles.");
            }

            if (marker == Capabilities)
            {
                throw new NotSupportedException("JPEG 2000 with extended capabilities (part 15 and on) is not read.");
            }

            var segment = Segment(reader);
            if (marker == PackedMain)
            {
                var index = segment.ReadByte();
                packedMain[index] = data[segment.Position..segment.End];
            }
            else
            {
                main.Read(marker, segment, size.Components.Length);
            }
        }

        var tiles = new J2kTileData[size.TilesAcross * size.TilesDown];
        for (var i = 0; i < tiles.Length; i++)
        {
            tiles[i] = new J2kTileData(i);
        }

        var packed = Concatenate(packedMain);
        var packedReader = packed == null ? null : new BigEndianReader(packed, 0, packed.Length);
        reader.Position -= 2;
        while (reader.Remaining >= 12 && reader.ReadUInt16() == StartOfTile)
        {
            if (!ReadTilePart(reader, data, tiles, size, packedReader))
            {
                break;
            }
        }

        return new J2kCodestream(size, main, tiles);
    }

    private static bool ReadTilePart(BigEndianReader reader, byte[] data, J2kTileData[] tiles, J2kSize size,
        BigEndianReader packedMain)
    {
        var sotStart = reader.Position - 2;
        var header = Segment(reader);
        var index = header.ReadUInt16();
        var length = header.ReadUInt32();
        header.ReadByte();
        var end = length == 0 ? reader.End : (int)Math.Min(reader.End, sotStart + (long)length);
        if (index >= tiles.Length || (length != 0 && length < 14))
        {
            reader.Position = Math.Max(end, sotStart + 12);
            return length != 0 && reader.Position < reader.End;
        }

        var tile = tiles[index];
        var first = !tile.Seen;
        tile.Seen = true;
        var part = tile.Parts++;
        int marker;
        while (reader.Position < end && (marker = reader.ReadUInt16()) != StartOfData)
        {
            var segment = Segment(reader);
            if (marker == PackedTile)
            {
                var order = segment.ReadByte();
                tile.AddPackedHeaders(part, order, data[segment.Position..segment.End]);
            }
            else if (first || marker == J2kMarkerSet.Poc)
            {
                tile.Markers.Read(marker, segment, size.Components.Length);
            }
        }

        if (packedMain != null && packedMain.Remaining >= 4)
        {
            var count = packedMain.ReadUInt32();
            var take = (int)Math.Min(count, (uint)packedMain.Remaining);
            tile.AddPackedHeadersFromMain(packedMain.Data[packedMain.Position..(packedMain.Position + take)]);
            packedMain.Position += take;
        }

        var bodyEnd = end;
        if (length == 0 && bodyEnd - 2 >= reader.Position && data[bodyEnd - 2] == 0xFF && data[bodyEnd - 1] == 0xD9)
        {
            bodyEnd -= 2;
        }

        if (reader.Position < bodyEnd)
        {
            tile.AddBody(data, reader.Position, bodyEnd);
        }

        reader.Position = end;
        return length != 0;
    }

    private static byte[] Concatenate(SortedDictionary<int, byte[]> parts)
    {
        if (parts.Count == 0)
        {
            return null;
        }

        var all = new MemoryStream();
        foreach (var part in parts.Values)
        {
            all.Write(part);
        }

        return all.ToArray();
    }

    private static BigEndianReader Segment(BigEndianReader reader)
    {
        var start = reader.Position;
        var length = reader.ReadUInt16();
        if (length < 2 || length > reader.End - start)
        {
            throw new InvalidDataException("A JPEG 2000 marker segment is longer than its codestream.");
        }

        reader.Position = start + length;
        return new BigEndianReader(reader.Data, start + 2, start + length);
    }
}
