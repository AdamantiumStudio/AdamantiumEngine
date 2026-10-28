using System.IO;
using System.Linq;
using Adamantium.Fonts;
using Adamantium.Graphics.Fonts;
using Adamantium.Mathematics;
using NUnit.Framework;

namespace Adamantium.EngineTests;

[TestFixture]
public class ColorGlyphLayoutTests
{
    private const string EmojiFont = @"C:\Windows\Fonts\seguiemj.ttf";

    [Test]
    public void AColorGlyph_AsksTheAtlasForItsLayersInsteadOfItself()
    {
        if (!File.Exists(EmojiFont))
        {
            Assert.Ignore("Segoe UI Emoji is not installed here.");
        }

        var font = TypefaceStore.GetTypeface(EmojiFont).Fonts[0];
        font.TryGetGlyphIndex(0x1F600, out var smile);
        var layout = new TextLayout(font.Typeface, font);
        layout.ProcessText("\U0001F600", 20, new Size<double>(double.NaN, double.NaN), TextWrapping.NoWrap, TextTrimming.None,
            HorizontalTextAlignment.Left, VerticalTextAlignment.Top);

        var drawn = layout.GetGlyphs().Select(g => g.Glyph.Index).ToArray();
        var outlines = font.GetColorPaint(smile)
            .Where(o => o.Kind == ColorPaintOperationKind.PushClip)
            .Select(o => o.GlyphIndex)
            .ToArray();

        Assert.That(outlines, Is.Not.Empty, "the emoji has a 'COLR' version 1 paint graph");
        Assert.That(drawn.Take(outlines.Length), Is.EqualTo(outlines), "the outlines its paint program clips to");
        Assert.That(drawn, Does.Not.Contain(smile), "the color glyph's own outline is not drawn");
    }
}
