using System.Collections.Generic;
using Adamantium.Fonts.Common;
using Adamantium.Fonts.Tables.CFF;

namespace Adamantium.Fonts.Parsers.CFF;

internal class CFFGlyphOutlineSource : IGlyphOutlineSource, IVariableGlyphOutlineSource
{
    private readonly ICFFParser parser;
    private readonly CFFFont font;
    private readonly FontDict[] fontDicts;
    private readonly float[] coordinates;
    private readonly FontWorkBudget budget;

    public CFFGlyphOutlineSource(ICFFParser parser, CFFFont font, FontDict[] fontDicts, long dataLength)
        : this(parser, font, fontDicts, null, FontWorkBudget.ForCharStrings(dataLength, fontDicts.Length))
    {
    }

    private CFFGlyphOutlineSource(ICFFParser parser, CFFFont font, FontDict[] fontDicts, float[] coordinates,
        FontWorkBudget budget)
    {
        this.parser = parser;
        this.font = font;
        this.fontDicts = fontDicts;
        this.coordinates = coordinates;
        this.budget = budget;
    }

    public void LoadOutlines(Glyph glyph)
    {
        var index = (int)glyph.Index;
        var data = font.CharStringsIndex.DataByOffset[index];
        var stack = new Stack<byte>(data.Length);
        for (var j = data.Length - 1; j >= 0; --j)
        {
            stack.Push(data[j]);
        }

        var commands = new CommandParser(parser, budget).Parse(font, stack, fontDicts[index], index);
        glyph.FillOutlines(commands, font.VariationStore?.VariationRegionList, coordinates).RecalculateBounds(true);
    }

    public IGlyphOutlineSource Vary(Font variedFont, float[] variedCoordinates, Typeface variedTypeface)
    {
        return font.VariationStore == null
            ? null
            : new CFFGlyphOutlineSource(parser, font, fontDicts, variedCoordinates, budget);
    }
}
