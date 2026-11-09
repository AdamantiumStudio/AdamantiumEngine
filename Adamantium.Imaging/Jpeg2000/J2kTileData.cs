using System.Collections.Generic;
using System.IO;

namespace Adamantium.Imaging.Jpeg2000;

/// <summary>What a codestream holds for one tile: the markers of its tile-part headers, the bytes of its tile-parts
/// one after another, and its packet headers when PPT or PPM moved them out of the packets.</summary>
internal sealed class J2kTileData
{
    private readonly MemoryStream _body = new();
    private readonly SortedDictionary<(int Part, int Index), byte[]> _packedByIndex = [];
    private MemoryStream _packedFromMain;

    public J2kTileData(int index)
    {
        Index = index;
    }

    public int Index { get; }

    public J2kMarkerSet Markers { get; } = new();

    /// <summary>Whether a tile-part of it came.</summary>
    public bool Seen { get; set; }

    public void AddBody(byte[] data, int start, int end) => _body.Write(data, start, end - start);

    /// <summary>The tile-parts read of it so far.</summary>
    public int Parts { get; set; }

    /// <summary>A PPT marker's headers: <paramref name="index"/> orders them within tile-part
    /// <paramref name="part"/>.</summary>
    public void AddPackedHeaders(int part, int index, byte[] data) => _packedByIndex[(part, index)] = data;

    public void AddPackedHeadersFromMain(byte[] data)
    {
        _packedFromMain ??= new MemoryStream();
        _packedFromMain.Write(data);
    }

    public byte[] Body() => _body.ToArray();

    /// <summary>The packet headers the tile's PPT or the main header's PPM hold, or null when they are in the
    /// packets.</summary>
    public byte[] PackedHeaders()
    {
        if (_packedFromMain != null)
        {
            return _packedFromMain.ToArray();
        }

        if (_packedByIndex.Count == 0)
        {
            return null;
        }

        var all = new MemoryStream();
        foreach (var part in _packedByIndex.Values)
        {
            all.Write(part);
        }

        return all.ToArray();
    }
}
