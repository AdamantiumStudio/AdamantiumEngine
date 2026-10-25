using System.Collections.Generic;

namespace Adamantium.Imaging.Tiff;

/// <summary>What GDAL's metadata says of one band: a value v of it means v × <see cref="Scale"/> +
/// <see cref="Offset"/>, its description, and the rest of its items by name.</summary>
public sealed class TiffBandInfo
{
    internal TiffBandInfo(double scale, double offset, string description, IReadOnlyDictionary<string, string> items)
    {
        Scale = scale;
        Offset = offset;
        Description = description;
        Items = items;
    }

    /// <summary>What a value is multiplied by; 1 when the file does not say.</summary>
    public double Scale { get; }

    /// <summary>What is added after; 0 when the file does not say.</summary>
    public double Offset { get; }

    /// <summary>The band's description; empty when the file has none.</summary>
    public string Description { get; }

    /// <summary>Every item of the band, by name.</summary>
    public IReadOnlyDictionary<string, string> Items { get; }
}
