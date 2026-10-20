using System.Linq;
using Adamantium.Fonts;
using Adamantium.Fonts.Shaping;
using NUnit.Framework;

namespace Adamantium.FontTests;

/// <summary>Shaping works in memory rented for the one shaping: what the shapings before it left there - a long text,
/// marks reordered and composed - changes nothing of a text shaped after them.</summary>
[TestFixture]
public class ShapingMemoryTests
{
    private const string NoGpos = "ScriptFonts/NotoSansHebrew-NoGpos.subset.ttf";

    [Test]
    public void AText_IsShapedAlike_WhateverWasShapedBeforeIt()
    {
        var font = Typeface.LoadFont(NoGpos, 3).GetFont(0);
        string[] texts = ["שּׁ בּ", "וֹ אַ", "ײַ פֿ יִ", "חּ אָ"];
        var first = texts.Select(text => Glyphs(TextShaper.Shape(font, text))).ToArray();

        TextShaper.Shape(font, string.Concat(Enumerable.Repeat("אָ יִ שּׁ ", 4000)));
        var again = texts.Reverse().Select(text => Glyphs(TextShaper.Shape(font, text))).Reverse().ToArray();

        Assert.That(again, Is.EqualTo(first));

        static (uint, int, int)[] Glyphs(ShapedGlyph[] glyphs) =>
            glyphs.Select(glyph => (glyph.GlyphIndex, glyph.Cluster, glyph.XAdvance)).ToArray();
    }
}
