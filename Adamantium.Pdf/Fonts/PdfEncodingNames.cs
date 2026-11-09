namespace Adamantium.Pdf.Fonts;

/// <summary>The encodings a font dictionary may name (ISO 32000-2, 9.6.5 and 9.7.5.2).</summary>
internal static class PdfEncodingNames
{
    public const string StandardEncoding = "StandardEncoding";
    public const string WinAnsiEncoding = "WinAnsiEncoding";
    public const string MacRomanEncoding = "MacRomanEncoding";
    public const string MacExpertEncoding = "MacExpertEncoding";
    public const string IdentityH = "Identity-H";
    public const string IdentityV = "Identity-V";

    /// <summary>The CIDToGIDMap that maps each CID to the glyph of that index.</summary>
    public const string Identity = "Identity";
}
