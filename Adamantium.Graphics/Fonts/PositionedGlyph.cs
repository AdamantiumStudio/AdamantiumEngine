namespace Adamantium.Graphics.Fonts;

/// <summary>A glyph the caller has already placed: its index in the font and the pen position on the baseline it is drawn
/// from, y down. What a document that lays out its own text - a PDF page - hands over instead of a string.</summary>
public readonly record struct PositionedGlyph(uint Index, double X, double Y);
