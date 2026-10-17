using System;
using System.IO;
using System.Linq;
using Adamantium.Fonts;
using Adamantium.Graphics.Fonts;
using Adamantium.Mathematics;
using NUnit.Framework;

namespace Adamantium.EngineTests;

/// <summary>Named paragraph and character styles, as InDesign's: the defaults under the paragraph's style, under the
/// character style, under what a range sets itself; a style based on another sets over it; changing a style changes
/// the text it is set on.</summary>
[TestFixture]
public class TextStyleTests
{
    private static readonly string Text = "A heading" + (char)0x2029 + "Body text, and an emphasis in it.";
    private static readonly Color Red = Color.FromRgba(255, 0, 0, 255);
    private static readonly Color Blue = Color.FromRgba(0, 0, 255, 255);

    [Test]
    public void ACharacterStyle_SetsWhatItSays_UnderWhatTheRangeSetsItself()
    {
        var emphasis = new CharacterStyle("Emphasis", new TextAttributes { Foreground = Red, FontSize = 20 });
        var text = new AttributedText(Text, new TextAttributes { FontSize = 12 })
            .SetCharacterStyle(10, 4, emphasis)
            .Apply(12, 2, new TextAttributes { FontSize = 30 });

        Assert.Multiple(() =>
        {
            Assert.That(text.AttributesAt(9).FontSize, Is.EqualTo(12), "outside it, the defaults");
            Assert.That(text.AttributesAt(10).FontSize, Is.EqualTo(20));
            Assert.That(text.AttributesAt(10).Foreground, Is.EqualTo(Red));
            Assert.That(text.AttributesAt(12).FontSize, Is.EqualTo(30), "the range's own size wins");
            Assert.That(text.AttributesAt(12).Foreground, Is.EqualTo(Red), "and the style's color stays");
            Assert.That(text.CharacterStyleAt(11), Is.SameAs(emphasis));
        });
    }

    [Test]
    public void AStyleBasedOnAnother_SetsOverIt()
    {
        var body = new CharacterStyle("Body", new TextAttributes { Foreground = Red, FontSize = 14 });
        var strong = new CharacterStyle("Strong", new TextAttributes { FontSize = 18 }, body);

        Assert.Multiple(() =>
        {
            Assert.That(strong.Resolved.Foreground, Is.EqualTo(Red));
            Assert.That(strong.Resolved.FontSize, Is.EqualTo(18));
            Assert.Throws<ArgumentException>(() => body.BasedOn = strong);
        });
    }

    [Test]
    public void ChangingAStyle_ChangesTheTextItIsSetOn_AndThroughTheStylesBasedOnIt()
    {
        var body = new CharacterStyle("Body", new TextAttributes { Foreground = Red });
        var strong = new CharacterStyle("Strong", new TextAttributes { FontSize = 18 }, body);
        var text = new AttributedText(Text).SetCharacterStyle(10, 4, strong);
        var before = text.AttributesAt(10).Foreground;
        body.Attributes = new TextAttributes { Foreground = Blue };

        Assert.Multiple(() =>
        {
            Assert.That(before, Is.EqualTo(Red));
            Assert.That(text.AttributesAt(10).Foreground, Is.EqualTo(Blue));
            Assert.That(text.AttributesAt(10).FontSize, Is.EqualTo(18));
        });
    }

    [Test]
    public void AParagraphStyle_SetsHowItsParagraphStands_AndItsText_UnderItsOwnFields()
    {
        var heading = new ParagraphStyle("Heading", new ParagraphAttributes { SpaceAfter = 12, MarginLeft = 20 },
            new TextAttributes { FontSize = 28, Foreground = Red });
        var text = new AttributedText(Text, new TextAttributes { FontSize = 12 })
            .SetParagraphStyle(0, 10, heading)
            .SetParagraph(0, 10, new ParagraphAttributes { MarginLeft = 4 });
        var paragraph = text.ParagraphAt(0)!.Value.Attributes;

        Assert.Multiple(() =>
        {
            Assert.That(paragraph.SpaceAfter, Is.EqualTo(12), "the style's");
            Assert.That(paragraph.MarginLeft, Is.EqualTo(4), "the paragraph's own wins");
            Assert.That(text.AttributesAt(2).FontSize, Is.EqualTo(28));
            Assert.That(text.AttributesAt(12).FontSize, Is.EqualTo(12), "the next paragraph keeps the defaults");
            Assert.That(text.ParagraphStyleAt(3), Is.SameAs(heading));
        });
    }

    [Test]
    public void ACharacterStyle_SetsOverItsParagraphsStyle()
    {
        var body = new ParagraphStyle("Body", text: new TextAttributes { FontSize = 14, Foreground = Red });
        var emphasis = new CharacterStyle("Emphasis", new TextAttributes { Foreground = Blue });
        var text = new AttributedText(Text).SetParagraphStyle(10, Text.Length - 10, body)
            .SetCharacterStyle(20, 5, emphasis);

        Assert.Multiple(() =>
        {
            Assert.That(text.AttributesAt(21).Foreground, Is.EqualTo(Blue));
            Assert.That(text.AttributesAt(21).FontSize, Is.EqualTo(14));
            Assert.That(text.AttributesAt(15).Foreground, Is.EqualTo(Red));
        });
    }

    [Test]
    public void SettingAStyle_ReplacesTheOnesItOverlaps_AndNoneTakesThemAway()
    {
        var one = new CharacterStyle("One");
        var two = new CharacterStyle("Two");
        var text = new AttributedText(Text).SetCharacterStyle(0, 10, one).SetCharacterStyle(5, 10, two);

        Assert.Multiple(() =>
        {
            Assert.That(text.CharacterStyleAt(4), Is.SameAs(one));
            Assert.That(text.CharacterStyleAt(5), Is.SameAs(two));
            Assert.That(text.CharacterStyleAt(14), Is.SameAs(two));
        });
        text.SetCharacterStyle(0, Text.Length, null);
        Assert.That(text.CharacterStyleAt(7), Is.Null);
    }

    [Test]
    public void ChangingAParagraphStyle_LaysTheTextOutAnew()
    {
        var typeface = Typeface.LoadFont(Path.Combine(TestContext.CurrentContext.TestDirectory, "Fonts",
            "SourceSans3-Regular.ttf"));
        var heading = new ParagraphStyle("Heading", new ParagraphAttributes { MarginLeft = 0 });
        var text = new AttributedText(Text).SetParagraphStyle(0, 10, heading);
        var layout = new TextLayout(typeface, typeface.Fonts[0]) { Fallback = null };
        double FirstPen()
        {
            layout.ProcessText(text, 16, new Size(400, double.NaN), TextWrapping.WrapByWords, TextTrimming.None,
                HorizontalTextAlignment.Left, VerticalTextAlignment.Top);
            return layout.GetTextData().First().PenX;
        }

        var before = FirstPen();
        heading.Paragraph = new ParagraphAttributes { MarginLeft = 30 };

        Assert.That(FirstPen(), Is.EqualTo(before + 30).Within(0.01));
    }

    [Test]
    public void ParagraphAttributes_TakeOnlyTheFieldsSetOverThem()
    {
        var under = new ParagraphAttributes { MarginLeft = 10, LineHeight = 30, Alignment = HorizontalTextAlignment.Center };
        var over = new ParagraphAttributes { MarginLeft = 0, TextIndent = 5 };
        var merged = under.With(over);

        Assert.Multiple(() =>
        {
            Assert.That(merged.MarginLeft, Is.EqualTo(0), "set to nothing is set");
            Assert.That(merged.TextIndent, Is.EqualTo(5));
            Assert.That(merged.LineHeight, Is.EqualTo(30));
            Assert.That(merged.Alignment, Is.EqualTo(HorizontalTextAlignment.Center));
            Assert.That(new ParagraphAttributes().With(new ParagraphAttributes()), Is.EqualTo(new ParagraphAttributes()));
        });
    }
}
