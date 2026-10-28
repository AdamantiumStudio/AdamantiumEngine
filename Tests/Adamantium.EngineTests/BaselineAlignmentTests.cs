using System.IO;
using System.Linq;
using Adamantium.Fonts;
using Adamantium.Graphics.Fonts;
using Adamantium.Mathematics;
using NUnit.Framework;

namespace Adamantium.EngineTests;

/// <summary>Baseline alignment, as WPF's: a range set where the font sets a superscript or a subscript, at the
/// ascender or descender of the layout's font, or at the top, middle or bottom of its line; a baseline shift adds to it
/// and its lines follow it.</summary>
[TestFixture]
public class BaselineAlignmentTests
{
    private const double FontSize = 20;

    private const double Small = 11;

    private static IFont Regular =>
        Typeface.LoadFont(Path.Combine(TestContext.CurrentContext.TestDirectory, "Fonts", "SourceSans3-Regular.ttf"))
            .Fonts[0];

    private static TextLayout Lay(string text, int start, int length, TextAttributes attributes, int bigStart = -1)
    {
        var typeface = Typeface.LoadFont(Path.Combine(TestContext.CurrentContext.TestDirectory, "Fonts",
            "SourceSans3-Regular.ttf"));
        var layout = new TextLayout(typeface, typeface.Fonts[0]) { Fallback = null };
        var attributed = new AttributedText(text);
        if (bigStart >= 0)
        {
            attributed.Apply(bigStart, 1, new TextAttributes { FontSize = 40 });
        }

        attributed.Apply(start, length, attributes);
        layout.ProcessText(attributed, FontSize, new Size<double>(double.NaN, double.NaN), TextWrapping.WrapByWords,
            TextTrimming.None, HorizontalTextAlignment.Left, VerticalTextAlignment.Top);
        return layout;
    }

    private static float Top(TextLayout layout, int index) =>
        layout.GetTextData().Single(glyph => glyph.PositionInString == index).Rect.Y;

    private static double Raised(BaselineAlignment alignment, string text = "x2 y", int bigStart = -1)
    {
        var plain = Lay(text, 1, 1, new TextAttributes { FontSize = Small }, bigStart);
        var aligned = Lay(text, 1, 1, new TextAttributes { FontSize = Small, BaselineAlignment = alignment }, bigStart);
        return Top(plain, 1) - Top(aligned, 1);
    }

    [Test]
    public void Superscript_IsRaisedAsTheFontSetsIt()
    {
        var font = Regular;
        Assert.That(font.SuperscriptYOffset, Is.GreaterThan(0), "the font says where");

        Assert.That(Raised(BaselineAlignment.Superscript),
            Is.EqualTo(font.SuperscriptYOffset * FontSize / font.UnitsPerEm).Within(1));
    }

    [Test]
    public void Subscript_IsLoweredAsTheFontSetsIt()
    {
        var font = Regular;
        Assert.That(font.SubscriptYOffset, Is.GreaterThan(0));

        Assert.That(Raised(BaselineAlignment.Subscript),
            Is.EqualTo(-font.SubscriptYOffset * FontSize / font.UnitsPerEm).Within(1));
    }

    [Test]
    public void TextTopAndBottom_MeetTheLayoutFontsAscenderAndDescender()
    {
        var font = Regular;
        var big = FontSize / font.UnitsPerEm;
        var small = Small / font.UnitsPerEm;

        Assert.That(Raised(BaselineAlignment.TextTop), Is.EqualTo(font.Ascender * (big - small)).Within(1));
        Assert.That(Raised(BaselineAlignment.TextBottom), Is.EqualTo(font.Descender * (big - small)).Within(1));
    }

    [Test]
    public void TopCenterAndBottom_SpanTheLine_TheMiddleHalfwayBetween()
    {
        var font = Regular;
        var line = Lay("x2 yW", 1, 1, new TextAttributes { FontSize = Small }, 4).GetLine(0);
        var box = (font.LineAscent + font.LineDescent + font.LineGap) * Small / font.UnitsPerEm;
        var top = Raised(BaselineAlignment.Top, "x2 yW", 4);
        var bottom = Raised(BaselineAlignment.Bottom, "x2 yW", 4);
        var center = Raised(BaselineAlignment.Center, "x2 yW", 4);

        Assert.That(top, Is.GreaterThan(0), "a small run at the top of a tall line goes up");
        Assert.That(bottom, Is.LessThan(top));
        Assert.That(top - bottom, Is.EqualTo(line.Height - box).Within(1), "from one edge of the line to the other");
        Assert.That(center, Is.EqualTo((top + bottom) / 2).Within(1));
    }

    [Test]
    public void AnObjectAtTheTop_StandsAtTheLinesTop()
    {
        var typeface = Typeface.LoadFont(Path.Combine(TestContext.CurrentContext.TestDirectory, "Fonts",
            "SourceSans3-Regular.ttf"));
        var layout = new TextLayout(typeface, typeface.Fonts[0]) { Fallback = null };
        var attributed = new AttributedText("ab￼W");
        attributed.Apply(3, 1, new TextAttributes { FontSize = 40 });
        attributed.Apply(2, 1, new TextAttributes
        {
            ObjectSize = new Size<double>(10, 12), BaselineAlignment = BaselineAlignment.Top,
        });
        layout.ProcessText(attributed, FontSize, new Size<double>(double.NaN, double.NaN), TextWrapping.WrapByWords,
            TextTrimming.None, HorizontalTextAlignment.Left, VerticalTextAlignment.Top);

        Assert.That(layout.GetInlineObjects().Single().Rect.Y, Is.EqualTo(layout.GetLine(0).Top).Within(1));
    }

    [Test]
    public void InVerticalText_OnlyTheIndicesMove()
    {
        static TextLayout Vertical(BaselineAlignment? alignment)
        {
            var typeface = Typeface.LoadFont(Path.Combine(TestContext.CurrentContext.TestDirectory, "Fonts",
                "NotoSansCJK-Regular.ttc"));
            var layout = new TextLayout(typeface, typeface.Fonts[0])
            {
                Fallback = null, WritingMode = WritingMode.VerticalRightToLeft,
            };
            var attributed = new AttributedText("漢字");
            attributed.Apply(1, 1, new TextAttributes { FontSize = Small, BaselineAlignment = alignment });
            layout.ProcessText(attributed, FontSize, new Size<double>(double.NaN, 200), TextWrapping.WrapByWords,
                TextTrimming.None, HorizontalTextAlignment.Left, VerticalTextAlignment.Top);
            return layout;
        }

        var plain = Vertical(null).GetTextData()[1].Rect;

        Assert.That(Vertical(BaselineAlignment.Top).GetTextData()[1].Rect, Is.EqualTo(plain));
        Assert.That(Vertical(BaselineAlignment.TextTop).GetTextData()[1].Rect, Is.EqualTo(plain));
        Assert.That(Vertical(BaselineAlignment.Superscript).GetTextData()[1].Rect, Is.Not.EqualTo(plain));
    }

    [Test]
    public void TabLeaders_FollowALineAlignment()
    {
        static TextLayout WithLeaders(BaselineAlignment? alignment)
        {
            var typeface = Typeface.LoadFont(Path.Combine(TestContext.CurrentContext.TestDirectory, "Fonts",
                "SourceSans3-Regular.ttf"));
            var layout = new TextLayout(typeface, typeface.Fonts[0])
            {
                Fallback = null, TabStops = [new TabStop(200, TabAlignment.Left, ".")],
            };
            var attributed = new AttributedText("a\tbW");
            attributed.Apply(3, 1, new TextAttributes { FontSize = 40 });
            attributed.Apply(1, 1, new TextAttributes { BaselineAlignment = alignment });
            layout.ProcessText(attributed, FontSize, new Size<double>(double.NaN, double.NaN), TextWrapping.WrapByWords,
                TextTrimming.None, HorizontalTextAlignment.Left, VerticalTextAlignment.Top);
            return layout;
        }

        var plain = WithLeaders(null).GetTextData().First(glyph => glyph.PositionInString < 0).Rect.Y;
        var top = WithLeaders(BaselineAlignment.Top).GetTextData().First(glyph => glyph.PositionInString < 0).Rect.Y;

        Assert.That(top, Is.LessThan(plain - 1), "the dots go up with the tab");
    }

    [Test]
    public void ABaselineShift_AddsToTheAlignment()
    {
        var plain = Lay("x2 y", 1, 1, new TextAttributes
        {
            FontSize = Small, BaselineAlignment = BaselineAlignment.Superscript,
        });
        var shifted = Lay("x2 y", 1, 1, new TextAttributes
        {
            FontSize = Small, BaselineAlignment = BaselineAlignment.Superscript, BaselineShift = 3,
        });

        Assert.That(Top(plain, 1) - Top(shifted, 1), Is.EqualTo(3).Within(1e-3));
    }

    [TestCase(BaselineAlignment.Superscript)]
    [TestCase(BaselineAlignment.Top)]
    public void ItsUnderline_FollowsIt(BaselineAlignment alignment)
    {
        var attributes = new TextAttributes { FontSize = Small, Decorations = TextDecorations.Underline };
        var plain = Lay("x2 yW", 1, 1, attributes, 4);
        var aligned = Lay("x2 yW", 1, 1, attributes.With(new TextAttributes { BaselineAlignment = alignment }), 4);
        var raised = Top(plain, 1) - Top(aligned, 1);

        Assert.That(plain.GetAdornments().Single().Rect.Y - aligned.GetAdornments().Single().Rect.Y,
            Is.EqualTo(raised).Within(1));
    }

    [Test]
    public void TheLine_KeepsItsHeight()
    {
        var plain = Lay("x2 y\nnext", 1, 1, new TextAttributes { FontSize = Small });
        var raised = Lay("x2 y\nnext", 1, 1, new TextAttributes
        {
            FontSize = Small, BaselineAlignment = BaselineAlignment.Superscript,
        });

        Assert.That(raised.GetLine(0).Height, Is.EqualTo(plain.GetLine(0).Height).Within(1e-6));
        Assert.That(raised.GetLine(1).Top, Is.EqualTo(plain.GetLine(1).Top).Within(1e-6));
    }
}
