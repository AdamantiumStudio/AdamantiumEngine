namespace Adamantium.Pdf.Fonts;

/// <summary>The font subtypes (ISO 32000-2, table 110) and the subtypes of embedded font programs (table 124).</summary>
internal static class PdfFontTypes
{
    public const string Type1 = "Type1";
    public const string MMType1 = "MMType1";
    public const string TrueType = "TrueType";
    public const string Type3 = "Type3";
    public const string Type0 = "Type0";
    public const string CIDFontType0 = "CIDFontType0";
    public const string CIDFontType2 = "CIDFontType2";

    public const string Type1C = "Type1C";
    public const string CIDFontType0C = "CIDFontType0C";
    public const string OpenType = "OpenType";
}
