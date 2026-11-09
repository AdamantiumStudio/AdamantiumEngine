namespace Adamantium.Imaging.Icc;

/// <summary>How a table's outputs encode the profile connection space (ICC.1, 6.3.4 and annex A).</summary>
internal enum IccPcsEncoding
{
    /// <summary>lut16Type: L* 0..100 over 0..0xFF00 of 0..0xFFFF, a* and b* likewise from -128.</summary>
    Legacy16,

    /// <summary>lut8Type: L* 0..100 over 0..255, a* and b* -128..127.</summary>
    Byte,

    /// <summary>lutAToBType and the version 4 encoding: L* 0..100 over the whole range, a* and b* -128..127.</summary>
    Version4,
}
