namespace Adamantium.Imaging.Tiff;

/// <summary>The GeoTIFF keys (OGC GeoTIFF 1.1, section 7) <see cref="TiffGeoReference"/> reads by name; any other key
/// is in its <see cref="TiffGeoReference.Numbers"/> or <see cref="TiffGeoReference.Texts"/> by number.</summary>
public static class TiffGeoKey
{
    /// <summary>Projected, geographic or geocentric.</summary>
    public const ushort ModelType = 1024;

    /// <summary>Whether a pixel is an area or a point.</summary>
    public const ushort RasterType = 1025;

    /// <summary>A description of the whole coordinate system.</summary>
    public const ushort Citation = 1026;

    /// <summary>The geographic coordinate system's EPSG code; 32767 for one the file describes itself.</summary>
    public const ushort GeographicType = 2048;

    /// <summary>The geodetic datum's EPSG code.</summary>
    public const ushort GeodeticDatum = 2050;

    /// <summary>The unit of angles, an EPSG unit code (9102 is the degree).</summary>
    public const ushort AngularUnits = 2054;

    /// <summary>The ellipsoid's EPSG code.</summary>
    public const ushort Ellipsoid = 2056;

    /// <summary>The ellipsoid's semi-major axis.</summary>
    public const ushort SemiMajorAxis = 2057;

    /// <summary>The ellipsoid's inverse flattening.</summary>
    public const ushort InverseFlattening = 2059;

    /// <summary>The projected coordinate system's EPSG code; 32767 for one the file describes itself.</summary>
    public const ushort ProjectedType = 3072;

    /// <summary>The projection's EPSG code.</summary>
    public const ushort Projection = 3074;

    /// <summary>The coordinate transformation method, as GeoTIFF numbers them.</summary>
    public const ushort CoordinateTransformation = 3075;

    /// <summary>The unit of lengths, an EPSG unit code (9001 is the meter).</summary>
    public const ushort LinearUnits = 3076;

    /// <summary>The vertical coordinate system's EPSG code.</summary>
    public const ushort VerticalType = 4096;

    /// <summary>The unit of heights, an EPSG unit code.</summary>
    public const ushort VerticalUnits = 4099;
}
