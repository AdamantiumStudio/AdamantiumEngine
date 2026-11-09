using System;

namespace Adamantium.Pdf.Fonts;

/// <summary>The flags of a font descriptor (ISO 32000-2, table 121).</summary>
[Flags]
public enum PdfFontFlags
{
    None = 0,
    FixedPitch = 1 << 0,
    Serif = 1 << 1,

    /// <summary>The font holds glyphs outside the standard Latin set, so its encoding is its own.</summary>
    Symbolic = 1 << 2,
    Script = 1 << 3,
    Nonsymbolic = 1 << 5,
    Italic = 1 << 6,
    AllCap = 1 << 16,
    SmallCap = 1 << 17,
    ForceBold = 1 << 18,
}
