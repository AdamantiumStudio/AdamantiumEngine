namespace Adamantium.Imaging.Tiff;

/// <summary>The method of a projection a GeoTIFF describes itself (ProjMethodGeoKey; OGC GeoTIFF 1.1, annex C).
/// </summary>
public enum TiffProjectionMethod
{
    /// <summary>The key is missing or holds a value GeoTIFF does not define.</summary>
    Unknown = 0,

    /// <summary>Transverse Mercator, also Gauss-Krüger and Gauss-Boaga.</summary>
    TransverseMercator = 1,

    /// <summary>Modified Transverse Mercator for Alaska, also Alaska Conformal.</summary>
    TransverseMercatorModifiedAlaska = 2,

    /// <summary>Hotine Oblique Mercator.</summary>
    ObliqueMercator = 3,

    /// <summary>Laborde Oblique Mercator.</summary>
    ObliqueMercatorLaborde = 4,

    /// <summary>Rosenmund Oblique Mercator - Swiss Oblique Cylindrical.</summary>
    ObliqueMercatorRosenmund = 5,

    /// <summary>Oblique Mercator on the sphere.</summary>
    ObliqueMercatorSpherical = 6,

    /// <summary>Mercator.</summary>
    Mercator = 7,

    /// <summary>Lambert Conic Conformal with two standard parallels.</summary>
    LambertConformalConic2SP = 8,

    /// <summary>Lambert Conic Conformal, Helmert's variant - the code files carry for Lambert Conic Conformal with one
    /// standard parallel (1SP).</summary>
    LambertConformalConicHelmert = 9,

    /// <summary>Lambert Azimuthal Equal Area.</summary>
    LambertAzimuthalEqualArea = 10,

    /// <summary>Albers Equal Area.</summary>
    AlbersEqualArea = 11,

    /// <summary>Azimuthal Equidistant.</summary>
    AzimuthalEquidistant = 12,

    /// <summary>Equidistant Conic.</summary>
    EquidistantConic = 13,

    /// <summary>Stereographic.</summary>
    Stereographic = 14,

    /// <summary>Polar Stereographic.</summary>
    PolarStereographic = 15,

    /// <summary>Oblique Stereographic.</summary>
    ObliqueStereographic = 16,

    /// <summary>Equirectangular.</summary>
    Equirectangular = 17,

    /// <summary>Cassini-Soldner, also Transverse Equidistant Cylindrical.</summary>
    CassiniSoldner = 18,

    /// <summary>Gnomonic.</summary>
    Gnomonic = 19,

    /// <summary>Miller Cylindrical.</summary>
    MillerCylindrical = 20,

    /// <summary>Orthographic.</summary>
    Orthographic = 21,

    /// <summary>Polyconic.</summary>
    Polyconic = 22,

    /// <summary>Robinson.</summary>
    Robinson = 23,

    /// <summary>Sinusoidal.</summary>
    Sinusoidal = 24,

    /// <summary>Van der Grinten.</summary>
    VanDerGrinten = 25,

    /// <summary>New Zealand Map Grid.</summary>
    NewZealandMapGrid = 26,

    /// <summary>Transverse Mercator, south oriented.</summary>
    TransverseMercatorSouthOriented = 27
}
