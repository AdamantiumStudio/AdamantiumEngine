namespace Adamantium.Imaging.Tiff;

/// <summary>The tags the TIFF reader understands (TIFF 6.0, TIFF Technical Notes, GeoTIFF, GDAL).</summary>
internal enum TiffTag : ushort
{
    /// <summary>What the image is: bit 0 - a reduced-resolution copy, bit 1 - a page, bit 2 - a transparency mask.</summary>
    NewSubfileType = 254,

    /// <summary>The image's width in pixels.</summary>
    ImageWidth = 256,

    /// <summary>The image's height in pixels.</summary>
    ImageLength = 257,

    /// <summary>Bits per sample, one value per sample of a pixel.</summary>
    BitsPerSample = 258,

    /// <summary>How the pixel data is compressed.</summary>
    Compression = 259,

    /// <summary>The color model of the pixel data.</summary>
    PhotometricInterpretation = 262,

    /// <summary>The order of bits within a byte: 1 - most significant first, 2 - least significant first.</summary>
    FillOrder = 266,

    /// <summary>Offsets of the strips.</summary>
    StripOffsets = 273,

    /// <summary>Which way the rows and columns run.</summary>
    Orientation = 274,

    /// <summary>Samples per pixel.</summary>
    SamplesPerPixel = 277,

    /// <summary>Rows in each strip but perhaps the last.</summary>
    RowsPerStrip = 278,

    /// <summary>Bytes in each strip, compressed.</summary>
    StripByteCounts = 279,

    /// <summary>Pixels per resolution unit across.</summary>
    XResolution = 282,

    /// <summary>Pixels per resolution unit down.</summary>
    YResolution = 283,

    /// <summary>1 - samples of a pixel together, 2 - each sample in planes of its own.</summary>
    PlanarConfiguration = 284,

    /// <summary>Options of CCITT Group 3 compression.</summary>
    T4Options = 292,

    /// <summary>Options of CCITT Group 4 compression.</summary>
    T6Options = 293,

    /// <summary>The unit of the resolution: 1 - none, 2 - inch, 3 - centimeter.</summary>
    ResolutionUnit = 296,

    /// <summary>The page's number and the number of pages.</summary>
    PageNumber = 297,

    /// <summary>The difference predictor applied before compression.</summary>
    Predictor = 317,

    /// <summary>The palette: all red values, then all green, then all blue, 16 bits each.</summary>
    ColorMap = 320,

    /// <summary>A tile's width in pixels.</summary>
    TileWidth = 322,

    /// <summary>A tile's height in pixels.</summary>
    TileLength = 323,

    /// <summary>Offsets of the tiles.</summary>
    TileOffsets = 324,

    /// <summary>Bytes in each tile, compressed.</summary>
    TileByteCounts = 325,

    /// <summary>Offsets of child directories, such as reduced-resolution copies.</summary>
    SubIfds = 330,

    /// <summary>The ink set of separated (CMYK) data.</summary>
    InkSet = 332,

    /// <summary>What the samples past the color model are, such as alpha.</summary>
    ExtraSamples = 338,

    /// <summary>The kind of each sample: unsigned, signed or floating point.</summary>
    SampleFormat = 339,

    /// <summary>JPEG tables shared by every strip or tile (TIFF Technical Note 2).</summary>
    JpegTables = 347,

    /// <summary>The old-style JPEG process (TIFF 6.0, section 22).</summary>
    JpegProc = 512,

    /// <summary>The offset of the old-style JPEG interchange stream.</summary>
    JpegInterchangeFormat = 513,

    /// <summary>The length of the old-style JPEG interchange stream.</summary>
    JpegInterchangeFormatLength = 514,

    /// <summary>The weights of red, green and blue in Y of YCbCr data.</summary>
    YCbCrCoefficients = 529,

    /// <summary>Chroma subsampling across and down.</summary>
    YCbCrSubsampling = 530,

    /// <summary>Where chroma samples sit relative to luma.</summary>
    YCbCrPositioning = 531,

    /// <summary>Black and white reference values of each sample.</summary>
    ReferenceBlackWhite = 532,

    /// <summary>The model's pixel size (GeoTIFF).</summary>
    ModelPixelScale = 33550,

    /// <summary>Raster points tied to model points (GeoTIFF).</summary>
    ModelTiepoint = 33922,

    /// <summary>The ICC color profile.</summary>
    IccProfile = 34675,

    /// <summary>The raster-to-model transformation matrix (GeoTIFF).</summary>
    ModelTransformation = 34264,

    /// <summary>The GeoTIFF key directory.</summary>
    GeoKeyDirectory = 34735,

    /// <summary>The GeoTIFF keys' double values.</summary>
    GeoDoubleParams = 34736,

    /// <summary>The GeoTIFF keys' text values.</summary>
    GeoAsciiParams = 34737,

    /// <summary>GDAL's metadata, XML.</summary>
    GdalMetadata = 42112,

    /// <summary>GDAL's no-data value, as text.</summary>
    GdalNoData = 42113
}
