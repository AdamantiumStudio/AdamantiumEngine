using System.IO;
using Adamantium.Fonts.Common;

namespace Adamantium.Fonts.Parsers;

internal class TTFGlyphOutlineSource : IGlyphOutlineSource, IVariableGlyphOutlineSource
{
    private const int MaxComponentDepth = 32;

    private readonly SfntParser parser;
    private readonly byte[] fontData;
    private readonly long glyfTableOffset;
    private readonly uint[] glyphOffsets;
    private readonly Glyph[] glyphs;
    private readonly FontWorkBudget budget;
    private int componentDepth;

    public TTFGlyphOutlineSource(SfntParser parser, byte[] fontData, long glyfTableOffset, uint[] glyphOffsets, Glyph[] glyphs)
    {
        budget = FontWorkBudget.ForCompositePoints(fontData.Length);
        this.parser = parser;
        this.fontData = fontData;
        this.glyfTableOffset = glyfTableOffset;
        this.glyphOffsets = glyphOffsets;
        this.glyphs = glyphs;
    }

    public void LoadOutlines(Glyph glyph)
    {
        if (componentDepth >= MaxComponentDepth)
        {
            throw new InvalidDataException("Composite glyphs nest too deep.");
        }

        componentDepth++;
        try
        {
            using var reader = new FontStreamReader(fontData);
            reader.Position = glyfTableOffset + glyphOffsets[glyph.Index];
            parser.ReadGlyphOutlines(reader, glyph, glyphs, budget);
        }
        finally
        {
            componentDepth--;
        }
    }

    public IGlyphOutlineSource Vary(Font font, float[] coordinates, Typeface variedTypeface)
    {
        return font.GlyphVariations == null
            ? null
            : new VariedTTFGlyphOutlineSource(parser, fontData, glyfTableOffset, glyphOffsets, glyphs.Length,
                variedTypeface, font, coordinates);
    }
}
