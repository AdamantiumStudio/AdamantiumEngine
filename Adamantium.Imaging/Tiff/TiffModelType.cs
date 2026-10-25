namespace Adamantium.Imaging.Tiff;

/// <summary>The kind of coordinate system a GeoTIFF's model space is (GTModelTypeGeoKey).</summary>
public enum TiffModelType
{
    /// <summary>The key is missing or holds a value GeoTIFF does not define.</summary>
    Unknown = 0,

    /// <summary>A projected system: easting and northing, usually in meters.</summary>
    Projected = 1,

    /// <summary>A geographic system: longitude and latitude, usually in degrees.</summary>
    Geographic = 2,

    /// <summary>A geocentric system: X, Y, Z from the Earth's center.</summary>
    Geocentric = 3
}
