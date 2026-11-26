namespace Adamantium.Fonts.Subsetting;

/// <summary>A run of characters, <paramref name="First"/> to <paramref name="Last"/>, standing for a run of glyphs one
/// after the other from <paramref name="Glyph"/> - a segment of a 'cmap' subtable.</summary>
internal readonly record struct CharacterRun(int First, int Last, int Glyph);
