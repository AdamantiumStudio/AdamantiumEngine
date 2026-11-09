namespace Adamantium.Pdf.Fonts;

/// <summary>A glyph of the Latin character set and its codes, -1 where an encoding has none.</summary>
internal readonly record struct PdfLatinCharacter(string Name, short Standard, short MacRoman, short WinAnsi,
    short PdfDoc);
