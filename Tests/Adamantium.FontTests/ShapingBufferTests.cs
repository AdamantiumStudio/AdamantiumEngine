using System.Linq;
using Adamantium.Fonts;
using Adamantium.Fonts.Shaping;
using NUnit.Framework;

namespace Adamantium.FontTests;

/// <summary>Text shaped in a buffer kept from one shaping to the next comes out as shaped in a buffer of its own: what
/// the last shaping left in it, a long text among them, changes nothing.</summary>
[TestFixture]
public class ShapingBufferTests
{
    private const string NoGpos = "ScriptFonts/NotoSansHebrew-NoGpos.subset.ttf";

    [Test]
    public void TextShapedInAKeptBuffer_IsShapedAsInItsOwn()
    {
        var font = Typeface.LoadFont(NoGpos, 3).GetFont(0);
        var buffer = new ShapingBuffer();
        string[] texts = ["שּׁ בּ", "וֹ אַ", string.Concat(Enumerable.Repeat("אָ יִ ", 4000)), "פֿ ײַ"];

        foreach (var text in texts)
        {
            Assert.That(Glyphs(TextShaper.Shape(font, text, null, buffer)),
                Is.EqualTo(Glyphs(TextShaper.Shape(font, text))), text.Length > 40 ? "the long text" : text);
        }

        static (uint, int, int)[] Glyphs(ShapedGlyph[] glyphs) =>
            glyphs.Select(glyph => (glyph.GlyphIndex, glyph.Cluster, glyph.XAdvance)).ToArray();
    }
}
