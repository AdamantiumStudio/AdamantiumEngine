namespace Adamantium.Imaging.Tiff;

/// <summary>The GeoTIFF keys (OGC GeoTIFF 1.1, section 7); their values are in
/// <see cref="TiffGeoReference.Numbers"/> or <see cref="TiffGeoReference.Texts"/> by number. Angles are in
/// <see cref="AngularUnits"/>, the azimuth in <see cref="AzimuthUnits"/>, the ellipsoid's axes in
/// <see cref="GeographicLinearUnits"/>, the projection's lengths in <see cref="LinearUnits"/>.</summary>
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

    /// <summary>A description of the geographic coordinate system.</summary>
    public const ushort GeographicCitation = 2049;

    /// <summary>The geodetic datum's EPSG code.</summary>
    public const ushort GeodeticDatum = 2050;

    /// <summary>The prime meridian's EPSG code.</summary>
    public const ushort PrimeMeridian = 2051;

    /// <summary>The unit of lengths of the geographic system, an EPSG unit code.</summary>
    public const ushort GeographicLinearUnits = 2052;

    /// <summary>The size of the geographic system's own linear unit, in meters.</summary>
    public const ushort GeographicLinearUnitSize = 2053;

    /// <summary>The unit of angles, an EPSG unit code (9102 is the degree).</summary>
    public const ushort AngularUnits = 2054;

    /// <summary>The size of the file's own angular unit, in radians.</summary>
    public const ushort AngularUnitSize = 2055;

    /// <summary>The ellipsoid's EPSG code.</summary>
    public const ushort Ellipsoid = 2056;

    /// <summary>The ellipsoid's semi-major axis.</summary>
    public const ushort SemiMajorAxis = 2057;

    /// <summary>The ellipsoid's semi-minor axis.</summary>
    public const ushort SemiMinorAxis = 2058;

    /// <summary>The ellipsoid's inverse flattening.</summary>
    public const ushort InverseFlattening = 2059;

    /// <summary>The unit of azimuths, an EPSG unit code.</summary>
    public const ushort AzimuthUnits = 2060;

    /// <summary>The prime meridian's longitude east of Greenwich.</summary>
    public const ushort PrimeMeridianLongitude = 2061;

    /// <summary>The projected coordinate system's EPSG code; 32767 for one the file describes itself.</summary>
    public const ushort ProjectedType = 3072;

    /// <summary>A description of the projected coordinate system.</summary>
    public const ushort ProjectedCitation = 3073;

    /// <summary>The projection's EPSG code.</summary>
    public const ushort Projection = 3074;

    /// <summary>The method of a projection the file describes itself - see <see cref="TiffProjectionMethod"/>.
    /// </summary>
    public const ushort ProjectionMethod = 3075;

    /// <summary>The unit of lengths, an EPSG unit code (9001 is the meter).</summary>
    public const ushort LinearUnits = 3076;

    /// <summary>The size of the file's own linear unit, in meters.</summary>
    public const ushort LinearUnitSize = 3077;

    /// <summary>The latitude of the first standard parallel.</summary>
    public const ushort StandardParallel1 = 3078;

    /// <summary>The latitude of the second standard parallel.</summary>
    public const ushort StandardParallel2 = 3079;

    /// <summary>The longitude of the natural origin.</summary>
    public const ushort NaturalOriginLongitude = 3080;

    /// <summary>The latitude of the natural origin.</summary>
    public const ushort NaturalOriginLatitude = 3081;

    /// <summary>The false easting.</summary>
    public const ushort FalseEasting = 3082;

    /// <summary>The false northing.</summary>
    public const ushort FalseNorthing = 3083;

    /// <summary>The longitude of the false origin.</summary>
    public const ushort FalseOriginLongitude = 3084;

    /// <summary>The latitude of the false origin.</summary>
    public const ushort FalseOriginLatitude = 3085;

    /// <summary>The easting at the false origin.</summary>
    public const ushort FalseOriginEasting = 3086;

    /// <summary>The northing at the false origin.</summary>
    public const ushort FalseOriginNorthing = 3087;

    /// <summary>The longitude of the projection center.</summary>
    public const ushort CenterLongitude = 3088;

    /// <summary>The latitude of the projection center.</summary>
    public const ushort CenterLatitude = 3089;

    /// <summary>The easting at the projection center.</summary>
    public const ushort CenterEasting = 3090;

    /// <summary>The northing at the projection center.</summary>
    public const ushort CenterNorthing = 3091;

    /// <summary>The scale factor at the natural origin.</summary>
    public const ushort ScaleAtNaturalOrigin = 3092;

    /// <summary>The scale factor at the projection center.</summary>
    public const ushort ScaleAtCenter = 3093;

    /// <summary>The azimuth of the initial line, in the azimuth unit.</summary>
    public const ushort Azimuth = 3094;

    /// <summary>The longitude that runs straight down from the pole on the map.</summary>
    public const ushort StraightVerticalPoleLongitude = 3095;

    /// <summary>The vertical coordinate system's EPSG code.</summary>
    public const ushort VerticalType = 4096;

    /// <summary>A description of the vertical coordinate system.</summary>
    public const ushort VerticalCitation = 4097;

    /// <summary>The vertical datum's EPSG code.</summary>
    public const ushort VerticalDatum = 4098;

    /// <summary>The unit of heights, an EPSG unit code.</summary>
    public const ushort VerticalUnits = 4099;
}
