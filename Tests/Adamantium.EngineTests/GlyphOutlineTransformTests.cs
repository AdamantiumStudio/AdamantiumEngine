using System;
using System.IO;
using System.Linq;
using Adamantium.Fonts;
using Adamantium.Graphics.Fonts;
using Adamantium.Mathematics;
using NUnit.Framework;

namespace Adamantium.EngineTests;

/// <summary><see cref="GlyphWordData.OutlineTransform"/>: a glyph's outline carried to where the layout draws it -
/// upright, stretched, turned in a vertical line, slanted by a synthesized italic.</summary>
[TestFixture]
public class GlyphOutlineTransformTests
{
    private const double FontSize = 20;

    private static readonly Lazy<Typeface> SourceSans = new(() =>
        Typeface.LoadFont(Path.Combine(TestContext.CurrentContext.TestDirectory, "Fonts", "SourceSans3-Regular.ttf")));

    private static readonly Lazy<Typeface> NotoSansCjk = new(() =>
        Typeface.LoadFont(Path.Combine(TestContext.CurrentContext.TestDirectory, "Fonts", "NotoSansCJK-Regular.ttc")));

    private static GlyphWordData[] Lay(AttributedText text, Typeface typeface = null,
        WritingMode mode = WritingMode.Horizontal, Action<TextLayout> more = null,
        HorizontalTextAlignment alignment = HorizontalTextAlignment.Left, double width = double.NaN)
    {
        typeface ??= SourceSans.Value;
        var layout = new TextLayout(typeface, typeface.Fonts[0]) { Fallback = null, WritingMode = mode };
        more?.Invoke(layout);
        layout.ProcessText(text, FontSize, new Size<double>(width, double.NaN),
            double.IsNaN(width) ? TextWrapping.NoWrap : TextWrapping.WrapByWords, TextTrimming.None, alignment,
            VerticalTextAlignment.Top, justifyLastLine: alignment == HorizontalTextAlignment.Justify);
        return layout.GetTextData().Where(glyph => glyph.Glyph is { HasOutlines: true }).ToArray();
    }

    // The glyph's box in its font's units, carried by the transform: the bounds of its four corners.
    private static RectangleF Carried(GlyphWordData glyph)
    {
        var box = glyph.Glyph.BoundingRectangle;
        var transform = glyph.OutlineTransform;
        var corners = new[]
        {
            Vector2.Transform(new Vector2<double>(box.X, box.Y), transform),
            Vector2.Transform(new Vector2<double>(box.X + box.Width, box.Y), transform),
            Vector2.Transform(new Vector2<double>(box.X, box.Y + box.Height), transform),
            Vector2.Transform(new Vector2<double>(box.X + box.Width, box.Y + box.Height), transform),
        };
        var left = corners.Min(corner => corner.X);
        var top = corners.Min(corner => corner.Y);
        return new RectangleF((float)left, (float)top, (float)(corners.Max(corner => corner.X) - left),
            (float)(corners.Max(corner => corner.Y) - top));
    }

    private static void LiesOn(GlyphWordData glyph, RectangleF expected)
    {
        var carried = Carried(glyph);
        Assert.Multiple(() =>
        {
            Assert.That(carried.X, Is.EqualTo(expected.X).Within(0.01), $"{glyph.Symbol}: left");
            Assert.That(carried.Y, Is.EqualTo(expected.Y).Within(0.01), $"{glyph.Symbol}: top");
            Assert.That(carried.Width, Is.EqualTo(expected.Width).Within(0.01), $"{glyph.Symbol}: width");
            Assert.That(carried.Height, Is.EqualTo(expected.Height).Within(0.01), $"{glyph.Symbol}: height");
        });
    }

    [Test]
    public void AnUprightGlyph_LiesOnWhereItIsDrawn()
    {
        foreach (var glyph in Lay(new AttributedText("Where")))
        {
            LiesOn(glyph, glyph.Rect);
        }
    }

    [Test]
    public void AStretchedGlyph_LiesOnWhereItIsDrawn()
    {
        var glyphs = Lay(new AttributedText("a few words to stretch across a wide line of text"),
            more: layout => layout.GlyphScaling = new SpacingRange(0.5, 1, 2),
            alignment: HorizontalTextAlignment.Justify, width: 700);

        Assert.That(glyphs.Any(glyph => glyph.HorizontalScale != 1), Is.True, "nothing was stretched");
        foreach (var glyph in glyphs)
        {
            LiesOn(glyph, glyph.Rect);
        }
    }

    [Test]
    public void AGlyphTurnedInAVerticalLine_LiesOnWhereItIsDrawn()
    {
        var glyphs = Lay(new AttributedText("漢Abc"), NotoSansCjk.Value, WritingMode.VerticalRightToLeft);

        Assert.That(glyphs.Count(glyph => glyph.Sideways), Is.EqualTo(3));
        Assert.That(glyphs.Count(glyph => glyph.Upright), Is.EqualTo(1));
        foreach (var glyph in glyphs)
        {
            LiesOn(glyph, glyph.Rect);
        }

        // Turned clockwise: the glyph's baseline runs down the line.
        var turned = glyphs[1].OutlineTransform;
        Assert.That(turned.M12, Is.GreaterThan(0), "the baseline does not run down");
        Assert.That(turned.M21, Is.GreaterThan(0), "the glyph's up does not point right");
    }

    [Test]
    public void ASynthesizedItalicTurnedInAVerticalLine_LeansInItsOwnFrame()
    {
        var plain = Lay(new AttributedText("漢Abc"), NotoSansCjk.Value, WritingMode.VerticalRightToLeft);
        var slanted = Lay(new AttributedText("漢Abc").Apply(0, 4, new TextAttributes { Synthesis = FontSynthesis.Style }),
            NotoSansCjk.Value, WritingMode.VerticalRightToLeft);

        for (var i = 0; i < plain.Length; i++)
        {
            // The glyph's up, turned or not, leans along its baseline by the slant: the same baseline, the up carried.
            var upright = plain[i].OutlineTransform;
            var leaning = slanted[i].OutlineTransform;
            Assert.That(leaning.Row1, Is.EqualTo(upright.Row1), $"{plain[i].Symbol}: the baseline turned otherwise");
            var lean = leaning.Row2 - upright.Row2;
            Assert.That(lean.X, Is.EqualTo(upright.Row1.X / plain[i].HorizontalScale * FontSynthesisRules.Slant).Within(1e-9),
                $"{plain[i].Symbol}: not slanted along its baseline");
            Assert.That(lean.Y, Is.EqualTo(upright.Row1.Y / plain[i].HorizontalScale * FontSynthesisRules.Slant).Within(1e-9),
                $"{plain[i].Symbol}: not slanted along its baseline");
        }
    }

    [Test]
    public void ASynthesizedItalic_LeansRight_ItsBaselineWhereItWas()
    {
        var plain = Lay(new AttributedText("slant"));
        var slanted = Lay(new AttributedText("slant").Apply(0, 5, new TextAttributes { Synthesis = FontSynthesis.Style }));

        for (var i = 0; i < plain.Length; i++)
        {
            var upright = plain[i].OutlineTransform;
            var leaning = slanted[i].OutlineTransform;
            Assert.That(leaning.M31, Is.EqualTo(upright.M31).Within(1e-6), "the pen moved");
            Assert.That(leaning.M32, Is.EqualTo(upright.M32).Within(1e-6), "the baseline moved");
            Assert.That(leaning.M21, Is.EqualTo(FontSynthesisRules.Slant * FontSize / plain[i].Font.UnitsPerEm).Within(1e-9),
                "not slanted as the screen slants");
        }
    }
}
