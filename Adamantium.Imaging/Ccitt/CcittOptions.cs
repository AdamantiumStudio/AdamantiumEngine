namespace Adamantium.Imaging.Ccitt;

/// <summary>How CCITT fax data is coded - the parameters of the PDF <c>CCITTFaxDecode</c> filter (ISO 32000-2,
/// table 12), which TIFF's fax compressions are cases of.</summary>
public sealed class CcittOptions
{
    /// <summary>Negative for Group 4 (two-dimensional only), 0 for one-dimensional Group 3, positive for Group 3 with
    /// a tag bit before each row choosing one or two dimensions.</summary>
    public int K { get; init; }

    /// <summary>The width of a row in pixels.</summary>
    public int Columns { get; init; } = 1728;

    /// <summary>The number of rows, or 0 to decode until the data or its end-of-block code ends.</summary>
    public int Rows { get; init; }

    /// <summary>Whether each row starts on a byte boundary.</summary>
    public bool EncodedByteAlign { get; init; }

    /// <summary>Whether 1 bits are black in the output; otherwise 0 bits are, as in PDF by default.</summary>
    public bool BlackIs1 { get; init; }
}
