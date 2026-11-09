using System;
using System.IO;
using System.Threading;

namespace Adamantium.Fonts.Common;

/// <summary>The work all glyphs of a font may take together, in proportion to the font's size: a hostile font whose
/// every glyph is as costly as one glyph may be then still costs time and memory linear in its bytes.</summary>
internal sealed class FontWorkBudget
{
    private long remaining;

    private FontWorkBudget(long units)
    {
        remaining = units;
    }

    /// <summary>Charstring steps: CFF and Type 1 interpreters spend one per operand or operator. Subroutines make
    /// a glyph run far longer than its bytes (a CJK font's take about a thousand steps each), so each glyph adds its
    /// own share; a glyph takes at least two bytes of the data, so the whole stays linear in the font's size.</summary>
    public static FontWorkBudget ForCharStrings(long dataLength, long glyphCount) =>
        new(4_000_000 + 64 * Math.Max(0, dataLength) + 4096 * Math.Max(0, glyphCount));

    /// <summary>Outline points composite glyphs copy from their components.</summary>
    public static FontWorkBudget ForCompositePoints(long dataLength) => new(1_000_000 + 4 * Math.Max(0, dataLength));

    /// <exception cref="InvalidDataException">The font has used up its budget.</exception>
    public void Spend(long units)
    {
        if (Interlocked.Add(ref remaining, -units) < 0)
        {
            throw new InvalidDataException("The font's glyphs take more work in all than its size allows.");
        }
    }
}
