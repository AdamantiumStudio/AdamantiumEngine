using System.IO;
using System.Linq;
using Adamantium.Core.Fonts;
using Adamantium.Fonts;
using Adamantium.Graphics.Fonts;
using Adamantium.Mathematics;
using NUnit.Framework;

namespace Adamantium.EngineTests;

[TestFixture]
public class FontSynthesisTests
{
    private const double Size = 20;

    private static IFont Load(string file) =>
        Typeface.LoadFont(Path.Combine(TestContext.CurrentContext.TestDirectory, "Fonts", file)).Fonts[0];

    [Test]
    public void BoldOnARegularFace_IsSynthesizedOnlyWhenAllowed()
    {
        var regular = Load("SourceSans3-Regular.ttf");

        Assert.That(FontSynthesisRules.Needed(regular, FontWeight.Bold, FontStyle.Normal, FontSynthesis.Weight),
            Is.EqualTo(FontSynthesis.Weight));
        Assert.That(FontSynthesisRules.Needed(regular, FontWeight.Bold, FontStyle.Normal, FontSynthesis.None),
            Is.EqualTo(FontSynthesis.None), "off by default");
        Assert.That(FontSynthesisRules.Needed(regular, FontWeight.Medium, FontStyle.Normal, FontSynthesis.Weight),
            Is.EqualTo(FontSynthesis.None), "below 600 is not bold");
    }

    [Test]
    public void AFaceTheFamilyHas_WinsOverSynthesis()
    {
        var all = FontSynthesis.Weight | FontSynthesis.Style;

        Assert.That(FontSynthesisRules.Needed(Load("SourceSans3-Bold.ttf"), FontWeight.Bold, FontStyle.Normal, all),
            Is.EqualTo(FontSynthesis.None));
        Assert.That(FontSynthesisRules.Needed(Load("SourceSans3-It.ttf"), FontWeight.Normal, FontStyle.Italic, all),
            Is.EqualTo(FontSynthesis.None));
        Assert.That(FontSynthesisRules.Needed(Load("SourceSans3-Regular.ttf"), FontWeight.Normal, FontStyle.Italic, all),
            Is.EqualTo(FontSynthesis.Style));
    }

    [Test]
    public void AnEmboldenedRange_AdvancesFurtherByTwiceTheOutline()
    {
        var plain = Layout(new AttributedText("abc"));
        var bold = Layout(new AttributedText("abc").Apply(0, 3,
            new TextAttributes { Synthesis = FontSynthesis.Weight }));
        var extra = 2 * FontSynthesisRules.EmboldenPerSide(Size) * Size;

        var plainAdvances = plain.GetTextData().Select(g => g.Advance).ToArray();
        var boldAdvances = bold.GetTextData().Select(g => g.Advance).ToArray();

        Assert.That(boldAdvances.Zip(plainAdvances, (b, p) => b - p), Is.All.EqualTo(extra).Within(1e-6));
    }

    [Test]
    public void ASlantedRange_KeepsItsAdvances()
    {
        var plain = Layout(new AttributedText("abc"));
        var italic = Layout(new AttributedText("abc").Apply(0, 3,
            new TextAttributes { Synthesis = FontSynthesis.Style }));

        Assert.That(italic.GetTextData().Select(g => g.Advance), Is.EqualTo(plain.GetTextData().Select(g => g.Advance)));
    }

    [Test]
    public void ASlantedRange_TakesTheRoomItsLastLetterLeansInto()
    {
        var plain = Layout(new AttributedText("Hd"));
        var italic = Layout(new AttributedText("Hd").Apply(0, 2,
            new TextAttributes { Synthesis = FontSynthesis.Style }));
        var d = plain.GetTextData().Last();
        var ascent = d.Rect.Bottom + d.Glyph.BoundingRectangle.Y * Size / d.Font.UnitsPerEm - d.Rect.Top;

        Assert.That(italic.RealTextDimensions.Width - plain.RealTextDimensions.Width,
            Is.EqualTo(ascent * FontSynthesisRules.Slant).Within(1e-3), "the top of d leans past its box");
    }

    [TestCase(9.0, 1.0 / 48)]
    [TestCase(36.0, 1.0 / 64)]
    [TestCase(72.0, 1.0 / 64)]
    public void TheOutlineMovesLessAtLargerSizes(double size, double perSide)
    {
        Assert.That(FontSynthesisRules.EmboldenPerSide(size), Is.EqualTo(perSide).Within(1e-12));
    }

    private static TextLayout Layout(AttributedText text)
    {
        var font = Load("SourceSans3-Regular.ttf");
        var layout = new TextLayout(font.Typeface, font);
        layout.ProcessText(text, Size, new Size<double>(double.NaN, double.NaN), TextWrapping.NoWrap, TextTrimming.None,
            HorizontalTextAlignment.Left, VerticalTextAlignment.Top);
        return layout;
    }
}
