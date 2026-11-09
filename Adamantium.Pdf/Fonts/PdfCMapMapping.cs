namespace Adamantium.Pdf.Fonts;

/// <summary>What a block of a CMap maps codes to (ISO 32000-2, 9.7.5.4, 9.7.6.3 and 9.10.3).</summary>
internal enum PdfCMapMapping
{
    /// <summary>CIDs counting up from the first one given.</summary>
    Cids,

    /// <summary>The one CID whose glyph stands in for a code that maps to none.</summary>
    NotDef,

    /// <summary>Text, as ToUnicode gives it.</summary>
    Texts,
}
