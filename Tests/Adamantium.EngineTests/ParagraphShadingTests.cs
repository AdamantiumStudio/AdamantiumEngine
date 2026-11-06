using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using Adamantium.Fonts;
using Adamantium.Graphics.Fonts;
using Adamantium.Mathematics;
using NUnit.Framework;

namespace Adamantium.EngineTests;

/// <summary>SHADING of paragraphs, as Word's: from margin to margin down the lines, all inside the lines where there are
/// borders; the room down to a next paragraph between the same margins, shaded too, in the upper one's color; drawn under
/// everything else.</summary>
[TestFixture]
public class ParagraphShadingTests
{
    private const double FontSize = 20;
    private const char Separator = (char)0x2029;

    private static readonly Color Yellow = Color.FromRgba(255, 255, 0, 255);
    private static readonly Color Gray = Color.FromRgba(192, 192, 192, 255);
    private static readonly ParagraphBorder Line = new(2, 3);

    private static string FontPath(string name) => Path.Combine(TestContext.CurrentContext.TestDirectory, "Fonts", name);

    private static TextLayout Lay(AttributedText text, double width = 300, Action<TextLayout> setUp = null)
    {
        var typeface = Typeface.LoadFont(FontPath("SourceSans3-Regular.ttf"));
        var layout = new TextLayout(typeface, typeface.Fonts[0]) { Fallback = null };
        setUp?.Invoke(layout);
        layout.ProcessText(text, FontSize, new Size<double>(width, double.NaN), TextWrapping.WrapByWords,
            TextTrimming.None, HorizontalTextAlignment.Left, VerticalTextAlignment.Top);
        return layout;
    }

    private static AttributedText Paragraphs(params (string Text, ParagraphAttributes Attributes)[] paragraphs)
    {
        var text = new AttributedText(string.Join(Separator, paragraphs.Select(paragraph => paragraph.Text)));
        var start = 0;
        foreach (var (content, attributes) in paragraphs)
        {
            var length = Math.Min(content.Length + 1, text.Text.Length - start);
            text.SetParagraph(start, length, attributes);
            start += content.Length + 1;
        }

        return text;
    }

    private static ParagraphAttributes Shaded(Color color, double margin = 0) =>
        new() { Shading = color, MarginLeft = margin, MarginRight = margin, SpaceBefore = 12, SpaceAfter = 12 };

    private static List<TextAdornment> Shadings(TextLayout layout) =>
        layout.GetAdornments().Where(adornment => adornment.Kind == TextAdornmentKind.Background).ToList();

    private static double Bottom(TextLayout layout, int line) => layout.GetLine(line).Top + layout.GetLine(line).Height;

    [Test]
    public void AParagraphIsShadedFromMarginToMargin_DownItsLinesOnly()
    {
        var layout = Lay(Paragraphs(("plain", new ParagraphAttributes()), ("shaded", Shaded(Yellow, 20)),
            ("plain", new ParagraphAttributes())));
        var shading = Shadings(layout).Single();

        Assert.Multiple(() =>
        {
            Assert.That(shading.Color, Is.EqualTo(Yellow));
            Assert.That(shading.Rect.Left, Is.EqualTo(20).Within(1e-4));
            Assert.That(shading.Rect.Right, Is.EqualTo(280).Within(1e-4));
            Assert.That(shading.Rect.Top, Is.EqualTo(layout.GetLine(1).Top).Within(1e-4), "not the room above");
            Assert.That(shading.Rect.Bottom, Is.EqualTo(Bottom(layout, 1)).Within(1e-4), "nor the room below");
        });
    }

    [Test]
    public void TheRoomDownToANextParagraphBetweenTheSameMargins_TakesTheUpperOnesColor()
    {
        var layout = Lay(Paragraphs(("one", Shaded(Yellow, 20)), ("two", Shaded(Gray, 20))));
        var shadings = Shadings(layout);

        Assert.Multiple(() =>
        {
            Assert.That(shadings, Has.Count.EqualTo(2));
            Assert.That(shadings[0].Color, Is.EqualTo(Yellow));
            Assert.That(shadings[0].Rect.Bottom, Is.EqualTo(layout.GetLine(1).Top).Within(1e-4), "the room filled");
            Assert.That(shadings[1].Color, Is.EqualTo(Gray));
            Assert.That(shadings[1].Rect.Top, Is.EqualTo(layout.GetLine(1).Top).Within(1e-4));
            Assert.That(layout.GetLine(1).Top - Bottom(layout, 0), Is.GreaterThan(1), "there is room between them");
        });
    }

    [Test]
    public void TheRoomDownToANextParagraphBetweenOtherMargins_IsNotShaded()
    {
        var layout = Lay(Paragraphs(("one", Shaded(Yellow, 20)), ("two", Shaded(Yellow, 40))));
        var shadings = Shadings(layout);

        Assert.That(shadings[0].Rect.Bottom, Is.EqualTo(Bottom(layout, 0)).Within(1e-4));
    }

    [Test]
    public void InBorders_AllInsideTheLinesIsShaded()
    {
        var boxed = Shaded(Yellow, 20) with
        {
            BorderTop = Line, BorderBottom = Line, BorderLeft = Line, BorderRight = Line,
        };
        var layout = Lay(Paragraphs(("boxed", boxed)));
        var shading = Shadings(layout).Single();
        var borders = layout.GetAdornments().Where(adornment => adornment.Kind == TextAdornmentKind.Border)
            .Select(adornment => adornment.Rect).ToList();

        Assert.Multiple(() =>
        {
            Assert.That(shading.Rect.Left, Is.EqualTo(borders.Min(rect => rect.Left) + Line.Thickness).Within(1e-4));
            Assert.That(shading.Rect.Right, Is.EqualTo(borders.Max(rect => rect.Right) - Line.Thickness).Within(1e-4));
            Assert.That(shading.Rect.Top, Is.EqualTo(borders.Min(rect => rect.Top) + Line.Thickness).Within(1e-4));
            Assert.That(shading.Rect.Bottom, Is.EqualTo(borders.Max(rect => rect.Bottom) - Line.Thickness).Within(1e-4));
        });
    }

    [Test]
    public void ParagraphsSharingAFrame_AreShadedAsOne_DownToTheLineBelow()
    {
        var boxed = Shaded(Yellow, 20) with
        {
            BorderTop = Line, BorderBottom = Line, BorderLeft = Line, BorderRight = Line,
        };
        var layout = Lay(Paragraphs(("one", boxed), ("two", boxed)));
        var shadings = Shadings(layout);

        Assert.Multiple(() =>
        {
            Assert.That(shadings[0].Rect.Bottom, Is.EqualTo(shadings[1].Rect.Top).Within(1e-4), "no gap between");
            Assert.That(shadings[1].Rect.Bottom, Is.EqualTo(Bottom(layout, 1) + Line.Space).Within(1e-4));
        });
    }

    [Test]
    public void TheShadingGoesOnBelow_ThroughTheRoomToAParagraphLaidOutElsewhere()
    {
        var layout = Lay(Paragraphs(("one", Shaded(Yellow) with { ShadingContinuesBelow = 12 })));
        var boxed = Lay(Paragraphs(("one", Shaded(Yellow) with { ShadingContinuesBelow = 12, BorderBottom = Line })));

        Assert.Multiple(() =>
        {
            Assert.That(Shadings(layout).Single().Rect.Bottom, Is.EqualTo(Bottom(layout, 0) + 12).Within(1e-4));
            Assert.That(Shadings(boxed).Single().Rect.Bottom, Is.EqualTo(Bottom(boxed, 0) + Line.Space).Within(1e-4),
                "a line below closing its frame stops it");
        });
    }

    [Test]
    public void AParagraphGoingOnInTheNextColumn_IsShadedInEach()
    {
        var text = Paragraphs(("Once upon a time there was a long paragraph going on and on", Shaded(Yellow)));
        var line = Lay(new AttributedText("x")).GetLine(0).Height;
        var layout = Lay(text, 400, set => set.Frames =
            [new RectangleF(0, 0, 160, (float)(2 * line + 1)), new RectangleF(200, 0, 160, 400)]);
        var shadings = Shadings(layout);

        Assert.Multiple(() =>
        {
            Assert.That(shadings, Has.Count.EqualTo(2));
            Assert.That(shadings[0].Rect.Left, Is.EqualTo(0).Within(1e-4));
            Assert.That(shadings[1].Rect.Left, Is.EqualTo(200).Within(1e-4));
        });
    }

    [Test]
    public void AShortParagraphBesideItsDropCap_IsShadedDownToTheCapsBottom()
    {
        var layout = Lay(Paragraphs(("Once", Shaded(Yellow))), 300, set => set.DropCap = new DropCap(3));

        Assert.Multiple(() =>
        {
            Assert.That(layout.DropCapLength, Is.GreaterThan(0));
            Assert.That(Shadings(layout).Single().Rect.Bottom, Is.GreaterThan(Bottom(layout, 0) + 1), "the cap's lines");
        });
    }

    [Test]
    public void AHungFirstLineOrAMarksRoom_IsInside_AsWords()
    {
        var hung = Lay(Paragraphs(("hung", Shaded(Yellow, 40) with { TextIndent = -16 })));
        var marked = Lay(Paragraphs(("marked", Shaded(Yellow, 40) with { MarkRoom = 24 })));
        var boxed = Lay(Paragraphs(("boxed", new ParagraphAttributes
        {
            MarginLeft = 40, TextIndent = -16, BorderLeft = Line,
        })));
        var leftLine = boxed.GetAdornments().Single(adornment => adornment.Kind == TextAdornmentKind.Border).Rect;

        Assert.Multiple(() =>
        {
            Assert.That(Shadings(hung).Single().Rect.Left, Is.EqualTo(24).Within(1e-4), "from the first line's start");
            Assert.That(Shadings(marked).Single().Rect.Left, Is.EqualTo(16).Within(1e-4), "from the mark's");
            Assert.That(leftLine.Left, Is.EqualTo(24 - Line.Room).Within(1e-4), "the border goes round it too");
        });
    }

    // A column narrower than the width its paragraphs are laid at is a strip kept out all the way down: the shading and
    // the borders stop at it. One that keeps out part of the paragraph only - a picture the lines go round - does not.
    [Test]
    public void AStripKeptOutAllTheWayDown_IsTheColumnsEdge_APictureIsNot()
    {
        var text = Paragraphs(("Each paragraph keeps to the column it stands in, short or wide.", Shaded(Yellow) with
        {
            BorderRight = Line,
        }));
        var narrowed = Lay(text, 300, set => set.Exclusions = [new RectangleF(160, -10000, 10000, 20000)]);
        var pictured = Lay(text, 300, set => set.Exclusions = [new RectangleF(200, 0, 100, 10)]);
        var line = narrowed.GetAdornments().Single(adornment => adornment.Kind == TextAdornmentKind.Border).Rect;

        Assert.Multiple(() =>
        {
            Assert.That(Shadings(narrowed).Single().Rect.Right, Is.EqualTo(160 + Line.Space).Within(1e-3));
            Assert.That(line.Right, Is.EqualTo(160 + Line.Room).Within(1e-3), "the border too");
            Assert.That(Shadings(pictured).Single().Rect.Right, Is.EqualTo(300 + Line.Space).Within(1e-3),
                "behind a picture, as Word's");
        });
    }

    // A paragraph running on from a narrow column into a wide one: its lines in each are shaded and framed as wide as
    // that column, the side lines going on with no line across where it widens.
    [Test]
    public void AParagraphRunningOnFromANarrowColumnIntoAWideOne_IsShadedAndFramedInEach()
    {
        var text = Paragraphs(("Each paragraph keeps to the column it stands in, short in the narrow one and long in " +
                               "the wide one, as long as it is wide, whichever it stands in.", Shaded(Yellow) with
        {
            BorderTop = Line, BorderBottom = Line, BorderRight = Line,
        }));
        var line = Lay(new AttributedText("x")).GetLine(0).Height;
        var layout = Lay(text, 300, set => set.Exclusions = [new RectangleF(160, -10000, 10000, (float)(10000 + 2 * line))]);
        var shadings = Shadings(layout);
        var borders = layout.GetAdornments().Where(adornment => adornment.Kind == TextAdornmentKind.Border)
            .Select(adornment => adornment.Rect).ToList();

        Assert.Multiple(() =>
        {
            Assert.That(shadings, Has.Count.EqualTo(2));
            Assert.That(shadings[0].Rect.Right, Is.EqualTo(160 + Line.Space).Within(1e-3), "in the narrow one");
            Assert.That(shadings[1].Rect.Right, Is.EqualTo(300 + Line.Space).Within(1e-3), "in the wide one");
            Assert.That(shadings[1].Rect.Top, Is.EqualTo(shadings[0].Rect.Bottom).Within(1e-3), "no gap where it widens");
            Assert.That(borders.Count(rect => rect.Height < 2 * Line.Width + 1e-3), Is.EqualTo(2),
                "one line above, one below - none where it widens");
        });
    }

    [Test]
    public void ANegativeMargin_ShadesNoFurtherOutThanTheTextGoes()
    {
        var layout = Lay(Paragraphs(("out", Shaded(Yellow, -20))));

        Assert.That(Shadings(layout).Single().Rect.Left, Is.EqualTo(0).Within(1e-4));
    }

    [Test]
    public void ADropCapsShading_StopsAtANextParagraphBesideTheCap()
    {
        var layout = Lay(Paragraphs(("Once", Shaded(Yellow)), ("next", new ParagraphAttributes())), 300,
            set => set.DropCap = new DropCap(3));
        var nextTop = layout.GetLine(1).Top;

        Assert.That(Shadings(layout).Single().Rect.Bottom, Is.LessThanOrEqualTo(nextTop + 1e-4));
    }

    [Test]
    public void TheShadingIsUnderTheTextsOwnBackground()
    {
        var text = Paragraphs(("marked", Shaded(Yellow)));
        text.Apply(0, 3, new TextAttributes { Background = Gray });
        var backgrounds = Shadings(Lay(text));

        Assert.That(backgrounds.Select(adornment => adornment.Color), Is.EqualTo(new Color?[] { Yellow, Gray }),
            "the shading first, drawn under the marked letters");
    }

    [Test]
    public void TheShadingIsAFieldLikeTheOthers()
    {
        var under = new ParagraphAttributes { Shading = Yellow };
        var over = new ParagraphAttributes { MarginLeft = 4 };

        Assert.Multiple(() =>
        {
            Assert.That(under.With(over).Shading, Is.EqualTo(Yellow));
            Assert.That(under.With(new ParagraphAttributes { Shading = Gray }).Shading, Is.EqualTo(Gray));
            Assert.That(under, Is.Not.EqualTo(new ParagraphAttributes { Shading = Gray }));
            Assert.That(under, Is.EqualTo(new ParagraphAttributes { Shading = Yellow }));
            Assert.That(under.With(new ParagraphAttributes { ShadingContinuesBelow = 3 }).ShadingContinuesBelow,
                Is.EqualTo(3));
            Assert.That(() => new ParagraphAttributes { ShadingContinuesBelow = -1 },
                Throws.TypeOf<ArgumentOutOfRangeException>());
        });
    }
}
