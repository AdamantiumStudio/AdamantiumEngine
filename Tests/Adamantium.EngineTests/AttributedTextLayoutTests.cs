using System.IO;
using System.Linq;
using Adamantium.Core.Fonts.Shaping;
using Adamantium.Fonts;
using Adamantium.Fonts.Shaping;
using Adamantium.Graphics.Fonts;
using Adamantium.Mathematics;
using NUnit.Framework;

namespace Adamantium.EngineTests;

[TestFixture]
public class AttributedTextLayoutTests
{
    private const double Tolerance = 1e-4;

    private static TextLayout NewLayout()
    {
        var path = Path.Combine(TestContext.CurrentContext.TestDirectory, "Fonts", "SourceSans3-Regular.ttf");
        var typeface = Typeface.LoadFont(path);
        return new TextLayout(typeface, typeface.Fonts[0]);
    }

    private static void Process(TextLayout layout, AttributedText text, double width = double.NaN)
    {
        var wrapping = double.IsNaN(width) ? TextWrapping.NoWrap : TextWrapping.WrapByWords;
        layout.ProcessText(text, 20, new Size(width, double.NaN), wrapping, TextTrimming.None,
            HorizontalTextAlignment.Left, VerticalTextAlignment.Top);
    }

    [Test]
    public void ApplyingOverARange_SplitsTheRunsAndKeepsWhatItDoesNotSet()
    {
        var text = new AttributedText("abcdef", new TextAttributes { Foreground = Colors.White, Language = "en" });

        text.Apply(2, 2, new TextAttributes { Foreground = Colors.Red });

        Assert.That(text.Runs.Select(r => (r.Start, r.Length)), Is.EqualTo(new[] { (0, 2), (2, 2), (4, 2) }));
        Assert.That(text.Runs[1].Attributes.Foreground, Is.EqualTo(Colors.Red));
        Assert.That(text.Runs[1].Attributes.Language, Is.EqualTo("en"), "the language stays");
        Assert.That(text.AttributesAt(5).Foreground, Is.EqualTo(Colors.White));
    }

    [Test]
    public void AFeatureOnARange_ShapesOnlyThatRange()
    {
        var layout = NewLayout();
        var text = new AttributedText("office office")
            .Apply(0, 6, new TextAttributes { Features = FontFeature.ParseList("liga=0") });

        Process(layout, text);
        var glyphs = layout.GetTextData();

        Assert.That(glyphs.Count(g => g.PositionInString < 6), Is.EqualTo(6), "f and f stay apart in the first word");
        Assert.That(glyphs.Count(g => g.PositionInString > 6), Is.EqualTo(5), "the ff ligature in the second");
    }

    [Test]
    public void ALanguageOnARange_PicksTheFontsLocalForms()
    {
        var layout = NewLayout();
        Process(layout, new AttributedText("бгд бгд").Apply(4, 3, new TextAttributes { Language = "sr" }));
        var glyphs = layout.GetTextData();

        var plain = glyphs.Where(g => g.PositionInString < 3).Select(g => g.Glyph.Index);
        var serbian = glyphs.Where(g => g.PositionInString > 3).Select(g => g.Glyph.Index);
        Assert.That(serbian, Is.Not.EqualTo(plain));
    }

    [Test]
    public void GlyphsCarryTheAttributesOfTheirText()
    {
        var layout = NewLayout();
        Process(layout, new AttributedText("ab cd").Apply(3, 2, new TextAttributes { Foreground = Colors.Red }));
        var glyphs = layout.GetTextData();

        Assert.That(glyphs.Single(g => g.PositionInString == 0).Attributes.Foreground, Is.Null);
        Assert.That(glyphs.Single(g => g.PositionInString == 4).Attributes.Foreground, Is.EqualTo(Colors.Red));
    }

    [Test]
    public void ABackground_CoversItsRangeLineByLine()
    {
        var layout = NewLayout();
        var text = new AttributedText("alpha beta gamma delta")
            .Apply(6, 10, new TextAttributes { Background = Colors.Yellow });

        Process(layout, text, 70);
        var stops = layout.GetCaretStops();
        var backgrounds = layout.GetAdornments().Where(a => a.Kind == TextAdornmentKind.Background).ToArray();

        Assert.That(stops[6].LineIndex, Is.Not.EqualTo(stops[15].LineIndex), "the range wraps");
        Assert.That(backgrounds, Has.Length.EqualTo(stops[15].LineIndex - stops[6].LineIndex + 1));
        Assert.That(backgrounds[0].Rect.X, Is.EqualTo(stops[6].X).Within(Tolerance));
        Assert.That(backgrounds.All(b => b.Color == Colors.Yellow));
        var line = layout.GetLine(stops[6].LineIndex);
        Assert.That(backgrounds[0].Rect.Y, Is.EqualTo(line.Top).Within(Tolerance));
        Assert.That(backgrounds[0].Rect.Height, Is.EqualTo(line.Height).Within(Tolerance));
    }

    [Test]
    public void LinesStandWhereTheFontPutsThem()
    {
        var layout = NewLayout();
        var text = new AttributedText("Text")
            .Apply(0, 4, new TextAttributes { Decorations = TextDecorations.Underline | TextDecorations.Strikethrough });

        Process(layout, text);
        var baseline = layout.GetLine(0).Baseline;
        var adornments = layout.GetAdornments();
        var underline = adornments.Single(a => a.Kind == TextAdornmentKind.Underline);
        var strikethrough = adornments.Single(a => a.Kind == TextAdornmentKind.Strikethrough);

        Assert.That(underline.Rect.Y, Is.GreaterThan(baseline), "under the baseline");
        Assert.That(strikethrough.Rect.Y, Is.LessThan(baseline), "through the letters");
        Assert.That(underline.Color, Is.Null, "drawn in the text's color");
        Assert.That(underline.Rect.Width, Is.EqualTo(layout.GetCaretStops()[4].X).Within(Tolerance));
    }

    [Test]
    public void LargerTextOnARange_IsWiderAndSetsItsLine()
    {
        var plain = NewLayout();
        Process(plain, new AttributedText("ab"));
        var large = NewLayout();
        large.ProcessText(new AttributedText("ab"), 40, new Size(double.NaN, double.NaN), TextWrapping.NoWrap,
            TextTrimming.None, HorizontalTextAlignment.Left, VerticalTextAlignment.Top);
        var layout = NewLayout();
        layout.EmitNewlineCarets = true;
        var text = new AttributedText("ab ab\nab").Apply(3, 2, new TextAttributes { FontSize = 40 });

        Process(layout, text);
        var stops = layout.GetCaretStops();
        var first = layout.GetLine(0);
        var second = layout.GetLine(1);

        Assert.That(stops[5].X - stops[3].X, Is.EqualTo(large.GetCaretStops()[2].X).Within(Tolerance),
            "the large word is as wide as at 40");
        Assert.That(first.Height, Is.EqualTo(large.GetLine(0).Height).Within(Tolerance), "the line is as tall");
        Assert.That(first.Baseline, Is.EqualTo(large.GetLine(0).Baseline).Within(Tolerance));
        Assert.That(second.Top, Is.EqualTo(first.Top + first.Height).Within(Tolerance));
        Assert.That(second.Height, Is.EqualTo(plain.GetLine(0).Height).Within(Tolerance), "the next line is plain");

        var glyphs = layout.GetTextData();
        var small = glyphs.Single(g => g.PositionInString == 1);
        var big = glyphs.Single(g => g.PositionInString == 4);
        Assert.That(small.Rect.Bottom, Is.EqualTo(first.Baseline).Within(1), "the small b stands on the line's baseline");
        Assert.That(big.Rect.Bottom, Is.EqualTo(first.Baseline).Within(1), "and so does the large one");
        Assert.That(layout.HitTest(stops[6].X + 1, second.Top + 1).Index, Is.EqualTo(6), "a click finds the second line");
    }

    [Test]
    public void AFontOnARange_SetsThatRangeInItsFace()
    {
        var boldPath = Path.Combine(TestContext.CurrentContext.TestDirectory, "Fonts", "SourceSans3-Bold.ttf");
        var boldFont = Typeface.LoadFont(boldPath).Fonts[0];
        var bold = new TextLayout(boldFont.Typeface, boldFont);
        Process(bold, new AttributedText("bold"));
        var layout = NewLayout();
        layout.EmitNewlineCarets = true;

        Process(layout, new AttributedText("a bold word").Apply(2, 4, new TextAttributes { Font = boldFont }));
        var glyphs = layout.GetTextData();
        var stops = layout.GetCaretStops();

        Assert.That(glyphs.Where(g => g.PositionInString is >= 2 and < 6).All(g => g.Font == boldFont));
        Assert.That(glyphs.Where(g => g.PositionInString >= 7).All(g => g.Font == layout.Font));
        Assert.That(stops[6].X - stops[2].X, Is.EqualTo(bold.GetCaretStops()[4].X).Within(Tolerance),
            "the bold word is as wide as in its own face");
    }

    [Test]
    public void ALineHoldsItsDescenders()
    {
        var layout = NewLayout();
        layout.EmitNewlineCarets = true;
        var text = new AttributedText("gy\ngy").Apply(0, 5, new TextAttributes { Background = Colors.Yellow });

        Process(layout, text);
        var font = layout.Font;
        var scale = 20.0 / font.UnitsPerEm;
        var first = layout.GetLine(0);
        var second = layout.GetLine(1);
        var g = layout.GetTextData().First(x => x.Symbol == 'g');
        var background = layout.GetAdornments().First(a => a.Kind == TextAdornmentKind.Background);

        Assert.That(first.Height, Is.EqualTo((font.LineAscent + font.LineDescent + font.LineGap) * scale).Within(Tolerance));
        Assert.That(g.Rect.Bottom, Is.LessThanOrEqualTo(background.Rect.Bottom + Tolerance), "the background holds the tail");
        Assert.That(g.Rect.Bottom, Is.LessThanOrEqualTo(second.Top + 1), "and the tail stays out of the next line");
        Assert.That(first.Baseline - first.Top,
            Is.EqualTo((font.LineGap / 2.0 + font.LineAscent) * scale).Within(1), "the baseline sits at the ascent");
    }

    [Test]
    public void TheRangeRectsOfASelection_FollowTheCaretStops()
    {
        var layout = NewLayout();
        Process(layout, new AttributedText("selection"));
        var stops = layout.GetCaretStops();

        var rects = layout.GetRangeRects(2, 5);

        Assert.That(rects, Has.Count.EqualTo(1));
        Assert.That(rects[0].X, Is.EqualTo(stops[2].X).Within(Tolerance));
        Assert.That(rects[0].Right, Is.EqualTo(stops[5].X).Within(Tolerance));
        Assert.That(layout.LineCount, Is.EqualTo(1));
    }
}
