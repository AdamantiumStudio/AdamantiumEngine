using System.IO;
using System.Linq;
using Adamantium.Fonts;
using Adamantium.Graphics.Fonts;
using Adamantium.Mathematics;
using NUnit.Framework;

namespace Adamantium.EngineTests;

/// <summary>Kerning laid out: the font's pairs by default, the letters' outlines with optical kerning, neither with none;
/// for the whole text or a range of it, and never in a joined script.</summary>
[TestFixture]
public class KerningLayoutTests
{
    private const double FontSize = 40;

    private static TextLayout Lay(string text, TextKerning kerning, string font = "SourceSans3-Regular.ttf",
        AttributedText attributed = null, WritingMode writingMode = WritingMode.Horizontal)
    {
        var typeface = Typeface.LoadFont(Path.Combine(TestContext.CurrentContext.TestDirectory, "Fonts", font));
        var layout = new TextLayout(typeface, typeface.Fonts[0])
        {
            Fallback = null, Kerning = kerning, WritingMode = writingMode,
        };
        if (attributed != null)
        {
            layout.ProcessText(attributed, FontSize, new Size<double>(double.NaN, double.NaN), TextWrapping.NoWrap,
                TextTrimming.None, HorizontalTextAlignment.Left, VerticalTextAlignment.Top);
        }
        else
        {
            layout.ProcessText(text, FontSize, new Size<double>(double.NaN, double.NaN), TextWrapping.NoWrap,
                TextTrimming.None, HorizontalTextAlignment.Left, VerticalTextAlignment.Top);
        }

        return layout;
    }

    private static double Width(TextLayout layout) => layout.GetTextData().Sum(glyph => glyph.Advance);

    [Test]
    public void TheFontsPairs_ByDefault_NoneWithoutKerning_TheOutlinesOptically()
    {
        const string text = "To LT";
        var metrics = Width(Lay(text, TextKerning.Metrics));
        var none = Width(Lay(text, TextKerning.None));
        var optical = Width(Lay(text, TextKerning.Optical));

        Assert.Multiple(() =>
        {
            Assert.That(metrics, Is.LessThan(none), "the font kerns these pairs");
            Assert.That(optical, Is.LessThan(none), "and so do the outlines");
            Assert.That(optical, Is.Not.EqualTo(metrics).Within(1e-3), "though not by the same amount");
        });
    }

    [Test]
    public void ARange_KernsOpticallyByItself()
    {
        const string text = "To To";
        var attributed = new AttributedText(text).Apply(0, 2, new TextAttributes { Kerning = TextKerning.Optical });

        var glyphs = Lay(text, TextKerning.None, attributed: attributed).GetTextData();
        var plain = Lay(text, TextKerning.None).GetTextData();

        Assert.Multiple(() =>
        {
            Assert.That(glyphs[0].Advance, Is.LessThan(plain[0].Advance), "the range's T moved its o closer");
            Assert.That(glyphs[3].Advance, Is.EqualTo(plain[3].Advance).Within(1e-6), "the rest is not kerned");
        });
    }

    [TestCase("بسم الله", "NotoSansArabic-Regular.ttf")]
    [TestCase("שלום עולם", "NotoSansHebrew-Regular.ttf")]
    public void RightToLeftText_KeepsTheFontsPairs(string text, string font)
    {
        var optical = Lay(text, TextKerning.Optical, font).GetTextData();
        var metrics = Lay(text, TextKerning.Metrics, font).GetTextData();

        Assert.That(optical.Select(glyph => glyph.Advance), Is.EqualTo(metrics.Select(glyph => glyph.Advance)));
    }

    [Test]
    public void VerticalText_KeepsTheFontsPairs()
    {
        const string text = "To LT AV";

        var optical = Lay(text, TextKerning.Optical, writingMode: WritingMode.VerticalRightToLeft).GetTextData();
        var metrics = Lay(text, TextKerning.Metrics, writingMode: WritingMode.VerticalRightToLeft).GetTextData();

        Assert.That(optical.Select(glyph => glyph.Advance), Is.EqualTo(metrics.Select(glyph => glyph.Advance)));
    }

    [Test]
    public void LettersOfDifferentSizes_AreLeftAsTheyAre()
    {
        const string text = "LT";
        var attributed = new AttributedText(text).Apply(1, 1, new TextAttributes { FontSize = FontSize / 8 });

        var optical = Lay(text, TextKerning.Optical, attributed: attributed).GetTextData();
        var none = Lay(text, TextKerning.None, attributed: attributed).GetTextData();

        Assert.That(optical[0].Advance, Is.EqualTo(none[0].Advance).Within(1e-6), "a small T is not drawn into the L");
    }

    [Test]
    public void AMark_StaysOnItsLetter()
    {
        const string text = "Tq̃T";

        double Off(TextKerning kerning)
        {
            var glyphs = Lay(text, kerning).GetTextData();
            var mark = System.Array.FindIndex(glyphs, glyph => glyph.Advance == 0);
            return glyphs[mark].Rect.X - glyphs[mark - 1].Rect.X;
        }

        Assert.That(Off(TextKerning.Optical), Is.EqualTo(Off(TextKerning.None)).Within(1e-3));
    }

    [Test]
    public void AFontWithNoStraightLetterToMeasureBy_KeepsItsPairs()
    {
        const string text = "12 34 (7)";

        var optical = Lay(text, TextKerning.Optical, "NotoSansHebrew-Regular.ttf").GetTextData();
        var metrics = Lay(text, TextKerning.Metrics, "NotoSansHebrew-Regular.ttf").GetTextData();

        Assert.That(optical.Select(glyph => glyph.Advance), Is.EqualTo(metrics.Select(glyph => glyph.Advance)));
    }

    [Test]
    public void ALetterOfAnotherScript_LeavesTheRestKernedOptically()
    {
        var mixed = Lay("To ก", TextKerning.Optical).GetTextData();
        var latin = Lay("To", TextKerning.Optical).GetTextData();

        Assert.That(mixed[0].Advance, Is.EqualTo(latin[0].Advance).Within(1e-6));
    }

    [Test]
    public void ADropCap_IsKernedAsTheText()
    {
        const string text = "To begin with, a line long enough to run past the cap and on below it.";

        double CapWidth(TextKerning kerning)
        {
            var typeface = Typeface.LoadFont(Path.Combine(TestContext.CurrentContext.TestDirectory, "Fonts",
                "SourceSans3-Regular.ttf"));
            var layout = new TextLayout(typeface, typeface.Fonts[0])
            {
                Fallback = null, Kerning = kerning, DropCap = new DropCap(2, 2),
            };
            layout.ProcessText(text, FontSize, new Size<double>(300, double.NaN), TextWrapping.WrapByWords, TextTrimming.None,
                HorizontalTextAlignment.Left, VerticalTextAlignment.Top);
            return layout.GetTextData()[1].Rect.X - layout.GetTextData()[0].Rect.X;
        }

        Assert.Multiple(() =>
        {
            Assert.That(CapWidth(TextKerning.None), Is.GreaterThan(CapWidth(TextKerning.Metrics)), "the font's To pair");
            Assert.That(CapWidth(TextKerning.Optical), Is.LessThan(CapWidth(TextKerning.None)));
        });
    }

    [Test]
    public void ARangeThatSaysWhatTheLayoutSays_IsKernedAcrossItsEdge()
    {
        const string text = "To";
        var attributed = new AttributedText(text).Apply(0, 1, new TextAttributes { Kerning = TextKerning.Optical });

        var edged = Lay(text, TextKerning.Optical, attributed: attributed).GetTextData();
        var whole = Lay(text, TextKerning.Optical).GetTextData();

        Assert.That(edged[0].Advance, Is.EqualTo(whole[0].Advance).Within(1e-6));
    }
}
