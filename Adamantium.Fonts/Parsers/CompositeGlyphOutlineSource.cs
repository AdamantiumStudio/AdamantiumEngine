using Adamantium.Fonts.Common;

namespace Adamantium.Fonts.Parsers;

internal class CompositeGlyphOutlineSource : IGlyphOutlineSource
{
    private readonly Glyph[] glyphs;
    private readonly FontWorkBudget budget;

    public CompositeGlyphOutlineSource(Glyph[] glyphs, FontWorkBudget budget)
    {
        this.glyphs = glyphs;
        this.budget = budget;
    }

    public void LoadOutlines(Glyph glyph)
    {
        glyph.AddComponentOutlines(index => glyphs[index], budget);
    }
}
