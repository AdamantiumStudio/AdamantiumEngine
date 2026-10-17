using System;
using System.IO;
using System.Linq;
using Adamantium.Fonts;
using Adamantium.Graphics.Fonts;
using Adamantium.Mathematics;
using NUnit.Framework;

namespace Adamantium.EngineTests;

/// <summary>Ruby, as HTML's: an annotation centered over its base, a base spread to a longer annotation, a line grown
/// to hold it unless its height is set, the annotation under the line when asked, a base kept on one line.</summary>
[TestFixture]
public class RubyTests
{
    private const double FontSize = 20;
    private const string Text = "日本の漢字を読む";
    private const int Base = 3;

    private static readonly Lazy<Typeface> NotoSansCjk = new(() =>
        Typeface.LoadFont(Path.Combine(TestContext.CurrentContext.TestDirectory, "Fonts", "NotoSansCJK-Regular.ttc")));

    private static TextLayout Lay(string reading, RubyPosition position = RubyPosition.Over, double width = 400,
        Action<TextLayout> setUp = null, string text = Text, int start = Base, int length = 2)
    {
        var typeface = NotoSansCjk.Value;
        var layout = new TextLayout(typeface, typeface.Fonts[0]) { Fallback = null };
        setUp?.Invoke(layout);
        var attributed = new AttributedText(text);
        if (reading != null)
        {
            attributed.SetRuby(start, length, new RubyAnnotation(reading, position));
        }

        layout.ProcessText(attributed, FontSize, new Size(width, double.NaN), TextWrapping.WrapByWords,
            TextTrimming.None, HorizontalTextAlignment.Left, VerticalTextAlignment.Top);
        return layout;
    }

    private static GlyphWordData[] Annotation(TextLayout layout) =>
        layout.GetTextData().Where(glyph => glyph.PositionInString < 0).ToArray();

    private static GlyphWordData[] BaseGlyphs(TextLayout layout, int start = Base, int length = 2) =>
        layout.GetTextData().Where(glyph => glyph.PositionInString >= start && glyph.PositionInString < start + length)
            .ToArray();

    private static double Left(GlyphWordData[] glyphs) => glyphs.Min(glyph => glyph.PenX);

    private static double Right(GlyphWordData[] glyphs) => glyphs.Max(glyph => glyph.PenX + glyph.Advance);

    [Test]
    public void TheAnnotation_StandsCenteredOverItsBase_AtHalfItsSize()
    {
        var layout = Lay("かんじ");
        var annotation = Annotation(layout);
        var bases = BaseGlyphs(layout);

        Assert.Multiple(() =>
        {
            Assert.That(annotation.Select(glyph => glyph.Symbol), Is.EqualTo("かんじ"));
            Assert.That(annotation.All(glyph => glyph.FontSize.Equals(FontSize / 2)), Is.True);
            Assert.That((Left(annotation) + Right(annotation)) / 2, Is.EqualTo((Left(bases) + Right(bases)) / 2).Within(0.01));
            Assert.That(annotation.Max(glyph => glyph.Rect.Bottom), Is.LessThanOrEqualTo(bases.Min(glyph => glyph.Rect.Top) + 0.5));
            Assert.That(annotation.Min(glyph => glyph.Rect.Top), Is.GreaterThanOrEqualTo(layout.GetLine(0).Top - 0.5),
                "inside its line");
        });
    }

    [Test]
    public void ALongerAnnotation_SpreadsItsBase_SpaceAroundEachGlyph()
    {
        const string reading = "とてもながいよみがな";
        var plain = Lay(null);
        var layout = Lay(reading);
        var annotation = Annotation(layout);
        var bases = BaseGlyphs(layout);
        var plainBases = BaseGlyphs(plain);

        Assert.Multiple(() =>
        {
            Assert.That(Right(bases) - Left(bases), Is.EqualTo(Right(annotation) - Left(annotation)).Within(0.01));
            Assert.That(bases[0].Advance, Is.EqualTo(bases[1].Advance).Within(1e-6), "the room shared alike");
            Assert.That(bases[0].Rect.Left - Left(bases),
                Is.EqualTo(plainBases[0].Rect.Left - Left(plainBases) + (bases[0].Advance - plainBases[0].Advance) / 2)
                    .Within(0.01), "half of its share before each glyph");
            Assert.That(layout.GetTextData().First(glyph => glyph.PositionInString == Base + 2).PenX,
                Is.EqualTo(Right(bases)).Within(1e-6), "the text after it follows the spread base");
        });
    }

    [Test]
    public void TheLine_GrowsToHoldTheAnnotation()
    {
        var plain = Lay(null).GetLine(0);
        var line = Lay("かんじ").GetLine(0);

        Assert.Multiple(() =>
        {
            Assert.That(line.Height, Is.GreaterThan(plain.Height + FontSize / 4));
            Assert.That(line.Baseline - line.Top, Is.GreaterThan(plain.Baseline - plain.Top + FontSize / 4));
        });
    }

    [TestCase(false)]
    [TestCase(true)]
    public void ALineWhoseHeightIsSet_DoesNotGrow(bool blockLineHeight)
    {
        void Set(TextLayout layout)
        {
            if (blockLineHeight)
            {
                layout.LineStacking = LineStackingStrategy.BlockLineHeight;
            }
            else
            {
                layout.LineHeight = 30;
            }
        }

        var plain = Lay(null, setUp: Set).GetLine(0);
        var line = Lay("かんじ", setUp: Set).GetLine(0);

        Assert.Multiple(() =>
        {
            Assert.That(line.Height, Is.EqualTo(plain.Height).Within(0.01));
            Assert.That(line.Baseline, Is.EqualTo(plain.Baseline).Within(0.01));
        });
    }

    [Test]
    public void UnderTheLine_TheAnnotationStandsBelowItsBase_AndTheLineGrowsDown()
    {
        var plain = Lay(null).GetLine(0);
        var layout = Lay("かんじ", RubyPosition.Under);
        var line = layout.GetLine(0);
        var annotation = Annotation(layout);
        var bases = BaseGlyphs(layout);

        Assert.Multiple(() =>
        {
            Assert.That(annotation.Min(glyph => glyph.Rect.Top), Is.GreaterThanOrEqualTo(bases.Max(glyph => glyph.Rect.Bottom) - 0.5));
            Assert.That(line.Baseline - line.Top, Is.EqualTo(plain.Baseline - plain.Top).Within(0.01), "nothing added above");
            Assert.That(line.Height, Is.GreaterThan(plain.Height + FontSize / 4));
        });
    }

    [TestCase(RubyPosition.Over)]
    [TestCase(RubyPosition.Under)]
    public void InVerticalText_TheAnnotationStandsUprightBesideItsBase(RubyPosition position)
    {
        TextLayout Vertical(string reading)
        {
            var typeface = NotoSansCjk.Value;
            var layout = new TextLayout(typeface, typeface.Fonts[0])
            {
                Fallback = null, WritingMode = WritingMode.VerticalRightToLeft,
            };
            var attributed = new AttributedText(Text);
            if (reading != null)
            {
                attributed.SetRuby(Base, 2, new RubyAnnotation(reading, position));
            }

            layout.ProcessText(attributed, FontSize, new Size(200, 400), TextWrapping.WrapByWords, TextTrimming.None,
                HorizontalTextAlignment.Left, VerticalTextAlignment.Top);
            return layout;
        }

        var layout = Vertical("かんじ");
        var annotation = Annotation(layout);
        var bases = BaseGlyphs(layout);
        var all = layout.GetTextData().Where(glyph => glyph.PositionInString >= 0).ToArray();
        double Middle(GlyphWordData[] glyphs, Func<RectangleF, double> low, Func<RectangleF, double> high) =>
            (glyphs.Min(glyph => low(glyph.Rect)) + glyphs.Max(glyph => high(glyph.Rect))) / 2;

        Assert.Multiple(() =>
        {
            Assert.That(annotation.All(glyph => glyph.Upright), Is.True);
            Assert.That(Middle(annotation, rect => rect.Top, rect => rect.Bottom),
                Is.EqualTo(Middle(bases, rect => rect.Top, rect => rect.Bottom)).Within(FontSize / 4), "centered along it");
            if (position == RubyPosition.Over)
            {
                Assert.That(annotation.Min(glyph => glyph.Rect.Left), Is.GreaterThanOrEqualTo(bases.Max(glyph => glyph.Rect.Right) - 0.5),
                    "to its right");
            }
            else
            {
                Assert.That(annotation.Max(glyph => glyph.Rect.Right), Is.LessThanOrEqualTo(bases.Min(glyph => glyph.Rect.Left) + 0.5),
                    "to its left");
            }

            Assert.That(annotation.Max(glyph => glyph.Rect.Right), Is.LessThanOrEqualTo(200.5), "inside the area");
            var centers = all.Select(glyph => glyph.Rect.Left + glyph.Rect.Width / 2).ToArray();
            Assert.That(centers.Max() - centers.Min(), Is.LessThan(FontSize / 4), "the line's own text stays in one column");
        });
    }

    [Test]
    public void ABase_StaysOnOneLine()
    {
        const string text = "漢字漢字漢字漢字漢字";
        var layout = Lay("かんじかんじ", width: 3.5 * FontSize, text: text, start: 2, length: 4);
        var bases = BaseGlyphs(layout, 2, 4);

        Assert.That(bases.Select(glyph => glyph.LineIndex).Distinct().Count(), Is.EqualTo(1),
            string.Join(", ", layout.GetTextData().Select(glyph => $"{glyph.Symbol}{glyph.LineIndex}")));
    }

    [Test]
    public void SettingARuby_ReplacesTheOnesItOverlaps_AndAnEmptyOneRemovesThem()
    {
        var text = new AttributedText(Text)
            .SetRuby(0, 2, new RubyAnnotation("にほん"))
            .SetRuby(3, 2, new RubyAnnotation("かんじ"))
            .SetRuby(1, 3, new RubyAnnotation("ほんのか"));

        Assert.That(text.Rubies.Select(run => (run.Start, run.Length, run.Annotation.Text)), Is.EqualTo(new[] { (1, 3, "ほんのか") }));
        text.SetRuby(0, Text.Length, new RubyAnnotation(string.Empty));
        Assert.That(text.Rubies, Is.Empty);
    }
}
