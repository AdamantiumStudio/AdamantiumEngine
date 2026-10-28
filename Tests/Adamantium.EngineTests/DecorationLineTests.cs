using System.IO;
using System.Linq;
using Adamantium.Fonts;
using Adamantium.Graphics.Fonts;
using Adamantium.Mathematics;
using NUnit.Framework;

namespace Adamantium.EngineTests;

/// <summary>Lines drawn with text, as WPF's text decorations: an overline at the font's ascender, and lines each with
/// their own place, thickness, offset, color and dashes.</summary>
[TestFixture]
public class DecorationLineTests
{
    private const double FontSize = 20;

    private static TextLayout Lay(TextAttributes attributes, string font = "SourceSans3-Regular.ttf",
        WritingMode mode = WritingMode.Horizontal)
    {
        var typeface = Typeface.LoadFont(Path.Combine(TestContext.CurrentContext.TestDirectory, "Fonts", font));
        var layout = new TextLayout(typeface, typeface.Fonts[0]) { Fallback = null, WritingMode = mode };
        var attributed = new AttributedText("Lined text");
        attributed.Apply(0, 10, attributes);
        layout.ProcessText(attributed, FontSize, new Size<double>(double.NaN, mode == WritingMode.Horizontal ? double.NaN : 300),
            TextWrapping.WrapByWords, TextTrimming.None, HorizontalTextAlignment.Left, VerticalTextAlignment.Top);
        return layout;
    }

    private static RectangleF Rect(TextLayout layout, TextAdornmentKind kind) =>
        layout.GetAdornments().Single(adornment => adornment.Kind == kind).Rect;

    [Test]
    public void AnOverline_RunsAtTheAscender()
    {
        var layout = Lay(new TextAttributes { Decorations = TextDecorations.Overline | TextDecorations.Underline });
        var font = layout.Font;
        var overline = Rect(layout, TextAdornmentKind.Overline);

        Assert.That(overline.Bottom,
            Is.EqualTo(layout.GetLine(0).Baseline - font.Ascender * FontSize / font.UnitsPerEm).Within(1));
        Assert.That(overline.Bottom,
            Is.LessThanOrEqualTo(layout.GetTextData().Where(g => g.Symbol != ' ').Min(g => g.Rect.Y) + 1),
            "over the letters");
        Assert.That(overline.Height, Is.EqualTo(Rect(layout, TextAdornmentKind.Underline).Height).Within(1e-3));
    }

    [Test]
    public void ALine_TakesItsThicknessOffsetColorAndDashes()
    {
        var plain = Lay(new TextAttributes { Decorations = TextDecorations.Underline });
        var red = new Color(255, 0, 0, 255);
        var layout = Lay(new TextAttributes
        {
            DecorationLines =
            [
                new TextDecorationLine
                {
                    Location = TextDecorationLocation.Underline, Thickness = 3, Offset = 2, Color = red, Dashes = [4, 2],
                },
            ],
        });
        var line = layout.GetAdornments().Single();

        Assert.That(line.Kind, Is.EqualTo(TextAdornmentKind.Underline));
        Assert.That(line.Rect.Height, Is.EqualTo(3).Within(1e-3));
        Assert.That(line.Rect.Y, Is.EqualTo(Rect(plain, TextAdornmentKind.Underline).Y + 2).Within(1e-3));
        Assert.That(line.Color, Is.EqualTo(red));
        Assert.That(line.Dashes, Is.EqualTo(new double[] { 4, 2 }));
    }

    [Test]
    public void AThickerStrike_StaysCenteredOnTheFontsStrikeout()
    {
        var plain = Rect(Lay(new TextAttributes { Decorations = TextDecorations.Strikethrough }),
            TextAdornmentKind.Strikethrough);
        var thick = Lay(new TextAttributes
        {
            DecorationLines = [new TextDecorationLine { Location = TextDecorationLocation.Strikethrough, Thickness = 6 }],
        }).GetAdornments().Single().Rect;

        Assert.That(thick.Y + thick.Height / 2, Is.EqualTo(plain.Y + plain.Height / 2).Within(1e-3));
    }

    [Test]
    public void ABaselineLine_IsCenteredOnTheBaseline_WithoutDashesIsSolid()
    {
        var layout = Lay(new TextAttributes
        {
            DecorationLines = [new TextDecorationLine { Location = TextDecorationLocation.Baseline, Thickness = 2 }],
        });
        var line = layout.GetAdornments().Single();

        Assert.That(line.Kind, Is.EqualTo(TextAdornmentKind.Baseline));
        Assert.That(line.Rect.Y + 1, Is.EqualTo(layout.GetLine(0).Baseline).Within(1e-3));
        Assert.That(line.Dashes, Is.Null);
    }

    [Test]
    public void AThickOverline_GrowsAwayFromTheLetters()
    {
        var thin = Lay(new TextAttributes { Decorations = TextDecorations.Overline });
        var thick = Lay(new TextAttributes
        {
            DecorationLines = [new TextDecorationLine { Location = TextDecorationLocation.Overline, Thickness = 6 }],
        });

        Assert.That(thick.GetAdornments().Single().Rect.Bottom,
            Is.EqualTo(Rect(thin, TextAdornmentKind.Overline).Bottom).Within(1e-3));
    }

    [Test]
    public void UnusableValues_FallBackToTheFonts()
    {
        var plain = Rect(Lay(new TextAttributes { Decorations = TextDecorations.Underline }), TextAdornmentKind.Underline);
        var line = Lay(new TextAttributes
        {
            DecorationLines =
            [
                null,
                new TextDecorationLine
                {
                    Thickness = double.PositiveInfinity, Offset = double.NaN, Dashes = [0, 0],
                },
            ],
        }).GetAdornments().Single();

        Assert.That(line.Rect, Is.EqualTo(plain));
        Assert.That(line.Dashes, Is.Null);
    }

    [Test]
    public void InVerticalText_TheOverlineRunsOnTheOtherSide()
    {
        var layout = Lay(new TextAttributes { Decorations = TextDecorations.Overline | TextDecorations.Underline },
            "NotoSansCJK-Regular.ttc", WritingMode.VerticalRightToLeft);

        Assert.That(Rect(layout, TextAdornmentKind.Overline).X, Is.LessThan(Rect(layout, TextAdornmentKind.Underline).X),
            "the underline runs right of the line, the overline left of it");
    }
}
