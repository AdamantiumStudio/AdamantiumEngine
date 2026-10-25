namespace Adamantium.Imaging.Tiff;

/// <summary>The color model of TIFF pixel data.</summary>
internal enum TiffPhotometric : ushort
{
    /// <summary>Gray, zero is white.</summary>
    MinIsWhite = 0,

    /// <summary>Gray, zero is black.</summary>
    MinIsBlack = 1,

    /// <summary>Red, green, blue.</summary>
    Rgb = 2,

    /// <summary>Indices into the color map.</summary>
    Palette = 3,

    /// <summary>A 1-bit transparency mask of another image.</summary>
    Mask = 4,

    /// <summary>Inks, usually cyan, magenta, yellow, black.</summary>
    Separated = 5,

    /// <summary>Luma and two chroma differences.</summary>
    YCbCr = 6,

    /// <summary>CIE L*a*b*.</summary>
    CieLab = 8,

    /// <summary>CIE L*a*b* as ICC encodes it.</summary>
    IccLab = 9,

    /// <summary>CIE L*a*b* as ITU encodes it.</summary>
    ItuLab = 10,

    /// <summary>LogL - luminance of high dynamic range.</summary>
    LogL = 32844,

    /// <summary>LogLuv - color of high dynamic range.</summary>
    LogLuv = 32845
}
