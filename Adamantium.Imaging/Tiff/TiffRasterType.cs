namespace Adamantium.Imaging.Tiff;

/// <summary>What a GeoTIFF pixel stands for (GTRasterTypeGeoKey).</summary>
public enum TiffRasterType
{
    /// <summary>The key is missing; read as <see cref="PixelIsArea"/>, the GeoTIFF default.</summary>
    Unknown = 0,

    /// <summary>A pixel is an area: its coordinates are those of its corner.</summary>
    PixelIsArea = 1,

    /// <summary>A pixel is a point: its coordinates are those of its center, as with elevation samples.</summary>
    PixelIsPoint = 2
}
