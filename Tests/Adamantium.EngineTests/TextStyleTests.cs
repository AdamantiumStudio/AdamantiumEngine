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
    public void ClearingOverrides_LeavesTheRangeOnlyItsStyles()
    {
        var emphasis = new CharacterStyle("Emphasis", new TextAttributes { Foreground = Red, FontSize = 20 });
        var text = new AttributedText(Text, new TextAttributes { FontSize = 12 })
            .SetCharacterStyle(10, 10, emphasis)
            .Apply(10, 10, new TextAttributes { FontSize = 30, Foreground = Blue })
            .ClearOverrides(10, 4);

        Assert.Multiple(() =>
        {
            Assert.That(text.AttributesAt(10).FontSize, Is.EqualTo(20), "the style's size again");
            Assert.That(text.AttributesAt(13).Foreground, Is.EqualTo(Red), "and its color");
            Assert.That(text.AttributesAt(14).FontSize, Is.EqualTo(30), "past the range, what was applied stays");
            Assert.That(text.AttributesAt(9).FontSize, Is.EqualTo(12));
            Assert.That(text.CharacterStyleAt(11), Is.SameAs(emphasis));
        });
    }

    [Test]
    public void ClearingOverridesToTheEnd_TakesAnyLengthPastIt()
    {
        var text = new AttributedText(Text).Apply(0, Text.Length, new TextAttributes { FontSize = 30 })
            .ClearOverrides(5, int.MaxValue);

        Assert.That(text.AttributesAt(4).FontSize, Is.EqualTo(30));
        Assert.That(text.AttributesAt(Text.Length - 1).FontSize, Is.Null);
    }

    [Test]
    public void StylesAndParagraphsToTheEnd_TakeAnyLengthPastIt()
    {
        var emphasis = new CharacterStyle("Emphasis", new TextAttributes { FontSize = 20 });
        var body = new ParagraphStyle("Body", new ParagraphAttributes(), new TextAttributes());
        var text = new AttributedText("0123456789")
            .SetCharacterStyle(1, int.MaxValue, emphasis)
            .SetParagraphStyle(2, int.MaxValue, body)
            .SetParagraph(10, int.MaxValue, new ParagraphAttributes());

        Assert.Multiple(() =>
        {
            Assert.That(text.CharacterStyleAt(5), Is.SameAs(emphasis));
            Assert.That(text.ParagraphStyleAt(5), Is.SameAs(body));
            Assert.That(text.Paragraphs[^1].Length, Is.Zero);
        });
    }

    [Test]
    public void ClearingOverrides_KeepsWhatPlacesAnInlineObject()
    {
        var text = new AttributedText("a" + (char)0xFFFC + "b")
            .Apply(1, 1, new TextAttributes
            {
                ObjectSize = new Size<double>(10, 12), BaselineAlignment = BaselineAlignment.Top, Foreground = Red,
            })
            .ClearOverrides(0, 3);

        Assert.Multiple(() =>
        {
            Assert.That(text.AttributesAt(1).ObjectSize, Is.EqualTo(new Size<double>(10, 12)));
            Assert.That(text.AttributesAt(1).BaselineAlignment, Is.EqualTo(BaselineAlignment.Top));
            Assert.That(text.AttributesAt(1).Foreground, Is.Null);
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
            layout.ProcessText(text, 16, new Size<double>(400, double.NaN), TextWrapping.WrapByWords, TextTrimming.None,
                HorizontalTextAlignment.Left, VerticalTextAlignment.Top);
            return layout.GetTextData().First().PenX;
        }

        var before = FirstPen();
        heading.Paragraph = new ParagraphAttributes { MarginLeft = 30 };

        Assert.That(FirstPen(), Is.EqualTo(before + 30).Within(0.01));
    }

    [Test]
    public void WhereAStyleAndAParagraphsOwnAttributesDisagree_EachPartTakesWhatCoversIt()
    {
        var p = ((char)0x2029).ToString();
        var style = new ParagraphStyle("Spaced", new ParagraphAttributes { SpaceBefore = 10 });
        var text = new AttributedText("aaaa" + p + "bbbb" + p + "cccc")
            .SetParagraph(0, 5, new ParagraphAttributes { MarginLeft = 5 })
            .SetParagraph(5, 5, new ParagraphAttributes { MarginLeft = 50 })
            .SetParagraphStyle(0, 10, style);
        var paragraphs = text.Paragraphs;

        Assert.Multiple(() =>
        {
            Assert.That(text.ParagraphAt(2)!.Value.Attributes.MarginLeft, Is.EqualTo(5));
            Assert.That(text.ParagraphAt(7)!.Value.Attributes.MarginLeft, Is.EqualTo(50));
            Assert.That(text.ParagraphAt(7)!.Value.Attributes.SpaceBefore, Is.EqualTo(10));
            Assert.That(text.ParagraphAt(12), Is.Null);
            for (var i = 1; i < paragraphs.Count; i++)
            {
                Assert.That(paragraphs[i].Start, Is.GreaterThanOrEqualTo(paragraphs[i - 1].End), "none overlap");
            }
        });
    }

    [Test]
    public void ParagraphAttributes_AreEqualByWhatTheySay()
    {
        Assert.Multiple(() =>
        {
            Assert.That(new ParagraphAttributes { MarginLeft = 0, LineHeight = double.NaN }, Is.EqualTo(new ParagraphAttributes()));
            Assert.That(new ParagraphAttributes { MarginLeft = 0 }.GetHashCode(), Is.EqualTo(new ParagraphAttributes().GetHashCode()));
            Assert.That(new ParagraphAttributes { MarginLeft = 1 }, Is.Not.EqualTo(new ParagraphAttributes()));
        });
    }

    [Test]
    public void ManyRangesAppliedAndStyled_EachCharacterTakesWhatWasSetLastOverIt()
    {
        var random = new Random(7);
        const int length = 400;
        var text = new AttributedText(new string('x', length));
        var styles = Enumerable.Range(0, 4).Select(i => new CharacterStyle("S" + i, new TextAttributes { Tracking = i + 1 })).ToArray();
        var sizes = new double?[length];
        var styled = new CharacterStyle[length];
        for (var step = 0; step < 2000; step++)
        {
            var start = random.Next(length);
            var count = random.Next(1, 30);
            if (random.Next(2) == 0)
            {
                var size = random.Next(8, 40);
                text.Apply(start, count, new TextAttributes { FontSize = size });
                for (var c = start; c < Math.Min(length, start + count); c++)
                {
                    sizes[c] = size;
                }
            }
            else
            {
                var style = random.Next(5) == 0 ? null : styles[random.Next(styles.Length)];
                text.SetCharacterStyle(start, count, style);
                for (var c = start; c < Math.Min(length, start + count); c++)
                {
                    styled[c] = style;
                }
            }
        }

        Assert.Multiple(() =>
        {
            for (var c = 0; c < length; c++)
            {
                Assert.That(text.AttributesAt(c).FontSize, Is.EqualTo(sizes[c]), $"size at {c}");
                Assert.That(text.CharacterStyleAt(c), Is.SameAs(styled[c]), $"style at {c}");
                Assert.That(text.AttributesAt(c).Tracking, Is.EqualTo(styled[c]?.Attributes.Tracking), $"tracking at {c}");
            }
        });
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
