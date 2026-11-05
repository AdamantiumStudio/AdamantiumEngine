using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using Adamantium.Fonts;
using Adamantium.Graphics.Fonts;
using Adamantium.Mathematics;
using NUnit.Framework;

namespace Adamantium.EngineTests;

/// <summary>BORDERS of paragraphs: the room their lines take above and below the text, kept at the top of a frame too;
/// the lines drawn as adornments, the side ones in the margins; paragraphs one after another with the same borders in
/// one frame.</summary>
[TestFixture]
public class ParagraphBorderTests
{
    private const double FontSize = 20;
    private const char Separator = (char)0x2029;

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

    private static ParagraphAttributes Boxed(double margin = 0) => new()
    {
        MarginLeft = margin,
        MarginRight = margin,
        BorderTop = Line,
        BorderBottom = Line,
        BorderLeft = Line,
        BorderRight = Line,
    };

    private static List<RectangleF> Borders(TextLayout layout) =>
        layout.GetAdornments().Where(adornment => adornment.Kind == TextAdornmentKind.Border)
            .Select(adornment => adornment.Rect).ToList();

    private static double Bottom(TextLayout layout, int line) => layout.GetLine(line).Top + layout.GetLine(line).Height;

    [Test]
    public void TheLinesRoomIsKeptAboveTheFirstLineAndBelowTheLast()
    {
        var plain = Lay(Paragraphs(("one", new ParagraphAttributes())));
        var boxed = Lay(Paragraphs(("one", Boxed())));

        Assert.Multiple(() =>
        {
            Assert.That(boxed.GetLine(0).Top - plain.GetLine(0).Top, Is.EqualTo(Line.Room).Within(1e-6));
            Assert.That(boxed.TextBottom - Bottom(boxed, 0), Is.EqualTo(Line.Room).Within(1e-6));
            Assert.That(plain.TextBottom, Is.EqualTo(Bottom(plain, 0)).Within(1e-6));
        });
    }

    [Test]
    public void TheLinesStandRoundTheText_TheSidesInTheMargins()
    {
        var layout = Lay(Paragraphs(("one", Boxed(20))));
        var borders = Borders(layout);
        var top = layout.GetLine(0).Top;
        var bottom = Bottom(layout, 0);

        Assert.Multiple(() =>
        {
            Assert.That(borders, Has.Count.EqualTo(4));
            Assert.That(borders.Min(rect => rect.Left), Is.EqualTo(20 - Line.Room).Within(1e-4), "left in the margin");
            Assert.That(borders.Max(rect => rect.Right), Is.EqualTo(300 - 20 + Line.Room).Within(1e-4));
            Assert.That(borders.Min(rect => rect.Top), Is.EqualTo(top - Line.Room).Within(1e-4));
            Assert.That(borders.Max(rect => rect.Bottom), Is.EqualTo(bottom + Line.Room).Within(1e-4));
            Assert.That(borders.Count(rect => Math.Abs(rect.Height - Line.Width) < 1e-4), Is.EqualTo(2),
                "a line above and one below");
            Assert.That(layout.GetLine(0).Start, Is.EqualTo(0));
            Assert.That(layout.GetCaretRect(new CaretPosition(0), 0).X, Is.EqualTo(20).Within(1),
                "the text stays at its margin");
        });
    }

    [Test]
    public void ParagraphsSharingTheirBordersStandInOneFrame()
    {
        var plain = Lay(Paragraphs(("one", new ParagraphAttributes()), ("two", new ParagraphAttributes())));
        var shared = Lay(Paragraphs(("one", Boxed()), ("two", Boxed())));
        var borders = Borders(shared);

        Assert.Multiple(() =>
        {
            Assert.That(shared.GetLine(1).Top - shared.GetLine(0).Top,
                Is.EqualTo(plain.GetLine(1).Top - plain.GetLine(0).Top).Within(1e-6), "no room between them");
            Assert.That(borders, Has.Count.EqualTo(4));
            Assert.That(borders.Max(rect => rect.Bottom), Is.EqualTo(Bottom(shared, 1) + Line.Room).Within(1e-4));
        });
    }

    [Test]
    public void ParagraphsWithOtherBordersEachHaveTheirOwnFrame()
    {
        var plain = Lay(Paragraphs(("one", new ParagraphAttributes()), ("two", new ParagraphAttributes())));
        var other = Boxed() with { BorderLeft = null };
        var apart = Lay(Paragraphs(("one", Boxed()), ("two", other)));

        Assert.Multiple(() =>
        {
            Assert.That(apart.GetLine(1).Top - apart.GetLine(0).Top - (plain.GetLine(1).Top - plain.GetLine(0).Top),
                Is.EqualTo(2 * Line.Room).Within(1e-6), "the room below the first and above the second");
            Assert.That(Borders(apart), Has.Count.EqualTo(7));
        });
    }

    [Test]
    public void ALineSetToNone_SharesAFrameWithOneNotSet()
    {
        var unset = Boxed() with { BorderLeft = null };
        var none = Boxed() with { BorderLeft = ParagraphBorder.None };
        var shared = Lay(Paragraphs(("one", none), ("two", unset)));

        Assert.That(Borders(shared), Has.Count.EqualTo(3), "one frame, open on its left");
    }

    [Test]
    public void ACenteredTextIsCenteredWithItsFrame()
    {
        var line = Lay(new AttributedText("x")).GetLine(0).Height;
        var box = line + 2 * Line.Room + 40;
        var typeface = Typeface.LoadFont(FontPath("SourceSans3-Regular.ttf"));
        var layout = new TextLayout(typeface, typeface.Fonts[0]) { Fallback = null };
        layout.ProcessText(Paragraphs(("one", Boxed())), FontSize, new Size<double>(300, box), TextWrapping.WrapByWords,
            TextTrimming.None, HorizontalTextAlignment.Left, VerticalTextAlignment.Center);
        var borders = Borders(layout);

        Assert.Multiple(() =>
        {
            Assert.That(borders.Min(rect => rect.Top), Is.EqualTo(20).Within(1), "the room above the frame");
            Assert.That(box - borders.Max(rect => rect.Bottom), Is.EqualTo(20).Within(1), "and below it");
        });
    }

    [Test]
    public void TheLineBelowADropCapStandsBelowTheCap()
    {
        var layout = Lay(Paragraphs(("Once", Boxed())), 300, set => set.DropCap = new DropCap(3));
        var bottom = Borders(layout).Max(rect => rect.Bottom);

        Assert.Multiple(() =>
        {
            Assert.That(layout.DropCapLength, Is.GreaterThan(0));
            Assert.That(bottom, Is.EqualTo(layout.TextBottom).Within(1e-3));
            Assert.That(layout.TextBottom - Line.Room,
                Is.GreaterThan(Bottom(layout, layout.LineCount - 1) + 1), "the cap reaches below the text");
        });
    }

    [Test]
    public void TheLineBelowAParagraphEndingBesideItsDropCap_StandsUnderItsText()
    {
        var layout = Lay(Paragraphs(("Once", Boxed()), ("then", new ParagraphAttributes())), 300,
            set => set.DropCap = new DropCap(3));
        var bottom = Borders(layout).Max(rect => rect.Bottom);

        Assert.Multiple(() =>
        {
            Assert.That(layout.DropCapLength, Is.GreaterThan(0));
            Assert.That(bottom, Is.EqualTo(Bottom(layout, 0) + Line.Room).Within(1e-3),
                "not across the next paragraph's lines beside the cap");
        });
    }

    [Test]
    public void TextCutShort_LeavesItsFrameOpenBelow()
    {
        var line = Lay(new AttributedText("x")).GetLine(0).Height;
        RectangleF[] frames = [new RectangleF(0, 0, 60, (float)(line + Line.Room + 1))];
        var layout = Lay(Paragraphs(("one two three four five six", Boxed())), 300, set => set.Frames = frames);

        Assert.Multiple(() =>
        {
            Assert.That(layout.LineCount, Is.EqualTo(1));
            Assert.That(Borders(layout), Has.Count.EqualTo(3), "above and the sides");
        });
    }

    [Test]
    public void AtTheTopOfAFrame_OnlyTheNextParagraphsLineAboveTakesRoom()
    {
        var line = Lay(new AttributedText("x")).GetLine(0).Height;
        RectangleF[] frames =
            [new RectangleF(0, 0, 100, (float)line + 1), new RectangleF(200, 0, 100, (float)(line + 40))];
        var layout = Lay(Paragraphs(("one", new ParagraphAttributes { BorderBottom = Line }),
            ("two", new ParagraphAttributes { BorderTop = Line })), 300, set => set.Frames = frames);

        Assert.That(layout.GetLine(1).Top, Is.EqualTo(Line.Room).Within(1e-6));
    }

    [Test]
    public void TheRoomBetweenFramesAddsToTheParagraphsSpacing()
    {
        var plain = Lay(Paragraphs(("one", new ParagraphAttributes { SpaceAfter = 10 }),
            ("two", new ParagraphAttributes())));
        var spaced = Lay(Paragraphs(("one", new ParagraphAttributes { SpaceAfter = 10, BorderBottom = Line }),
            ("two", new ParagraphAttributes())));

        Assert.That(spaced.GetLine(1).Top - plain.GetLine(1).Top, Is.EqualTo(Line.Room).Within(1e-6));
    }

    [Test]
    public void AtTheTopOfAFrameTheLinesRoomIsKept_TheSpacingIsNot()
    {
        var line = Lay(new AttributedText("x")).GetLine(0).Height;
        RectangleF[] frames =
            [new RectangleF(0, 0, 100, (float)line + 1), new RectangleF(200, 0, 100, (float)(line + 40))];
        var layout = Lay(Paragraphs(("one", new ParagraphAttributes()),
            ("two", new ParagraphAttributes { SpaceBefore = 30, BorderTop = Line })), 300, set => set.Frames = frames);
        var borders = Borders(layout);

        Assert.Multiple(() =>
        {
            Assert.That(layout.GetLine(1).Top, Is.EqualTo(Line.Room).Within(1e-6));
            Assert.That(borders, Has.Count.EqualTo(1));
            Assert.That(borders[0].Left, Is.EqualTo(200).Within(1e-4), "in its own frame");
            Assert.That(borders[0].Top, Is.EqualTo(0).Within(1e-4));
        });
    }

    [Test]
    public void ALineOfNoWidthTakesNoRoomAndDrawsNothing()
    {
        var plain = Lay(Paragraphs(("one", new ParagraphAttributes())));
        var none = Lay(Paragraphs(("one", new ParagraphAttributes
        {
            BorderTop = ParagraphBorder.None,
            BorderBottom = ParagraphBorder.None,
        })));

        Assert.Multiple(() =>
        {
            Assert.That(none.GetLine(0).Top, Is.EqualTo(plain.GetLine(0).Top).Within(1e-6));
            Assert.That(none.TextBottom, Is.EqualTo(plain.TextBottom).Within(1e-6));
            Assert.That(Borders(none), Is.Empty);
        });
    }

    [Test]
    public void ABorderOverTheStyleUnderItWins()
    {
        var style = new ParagraphAttributes { BorderTop = Line, MarginLeft = 4 };
        var over = style.With(new ParagraphAttributes { BorderTop = ParagraphBorder.None });

        Assert.Multiple(() =>
        {
            Assert.That(over.BorderTop, Is.EqualTo(ParagraphBorder.None));
            Assert.That(over.HasBorders, Is.False);
            Assert.That(over.MarginLeft, Is.EqualTo(4));
            Assert.That(style.With(new ParagraphAttributes()).BorderTop, Is.EqualTo(Line));
        });
    }

    [Test]
    public void ABorderIsNeverNegative()
    {
        Assert.Multiple(() =>
        {
            Assert.Throws<ArgumentOutOfRangeException>(() => _ = new ParagraphBorder(-1, 0));
            Assert.Throws<ArgumentOutOfRangeException>(() => _ = new ParagraphBorder(1, double.NaN));
            Assert.That(new ParagraphBorder(0, 5).Room, Is.EqualTo(0));
        });
    }

    [Test]
    public void AFrameGoingOnAbove_DrawsNoLineThere_AndReachesUp()
    {
        var plain = Lay(Paragraphs(("one", new ParagraphAttributes())));
        var layout = Lay(Paragraphs(("one", Boxed() with { BordersContinueAbove = 6 })));
        var borders = Borders(layout);
        var top = layout.GetLine(0).Top;

        Assert.Multiple(() =>
        {
            Assert.That(top, Is.EqualTo(plain.GetLine(0).Top).Within(1e-6), "no room above");
            Assert.That(borders, Has.Count.EqualTo(3), "below and the sides");
            Assert.That(borders.Min(rect => rect.Top), Is.EqualTo(top - 6).Within(1e-4));
            Assert.That(layout.TextBottom - Bottom(layout, 0), Is.EqualTo(Line.Room).Within(1e-6));
        });
    }

    [Test]
    public void AFrameGoingOnBelow_DrawsNoLineThere_AndReachesDown()
    {
        var layout = Lay(Paragraphs(("one", Boxed()), ("two", Boxed() with { BordersContinueBelow = 9 })));
        var borders = Borders(layout);
        var bottom = Bottom(layout, 1);

        Assert.Multiple(() =>
        {
            Assert.That(layout.TextBottom, Is.EqualTo(bottom).Within(1e-6), "no room below");
            Assert.That(borders, Has.Count.EqualTo(3), "above and the sides");
            Assert.That(borders.Max(rect => rect.Bottom), Is.EqualTo(bottom + 9).Within(1e-4));
        });
    }

    [Test]
    public void AFrameGoesOnOnlyFromTheEdgesOfTheLayout()
    {
        var goesOn = Boxed() with { BordersContinueAbove = 6, BordersContinueBelow = 9 };
        var layout = Lay(Paragraphs(("one", new ParagraphAttributes()), ("two", goesOn),
            ("three", new ParagraphAttributes())));

        Assert.That(Borders(layout), Has.Count.EqualTo(4), "a frame closed round the middle paragraph");
    }

    [Test]
    public void ALastLineWhoseLineBelowLeavesItsFrame_GoesOnInTheNext()
    {
        var line = Lay(new AttributedText("x")).GetLine(0).Height;
        RectangleF[] frames =
            [new RectangleF(0, 0, 100, (float)(2 * line + 1)), new RectangleF(200, 0, 100, (float)(3 * line))];
        var layout = Lay(Paragraphs(("one", new ParagraphAttributes()),
            ("two", new ParagraphAttributes { BorderBottom = Line })), 300, set => set.Frames = frames);
        var borders = Borders(layout);

        Assert.Multiple(() =>
        {
            Assert.That(layout.GetLine(1).Top, Is.EqualTo(0).Within(1e-6), "at the top of the next frame");
            Assert.That(borders, Has.Count.EqualTo(1));
            Assert.That(borders[0].Left, Is.EqualTo(200).Within(1e-4));
            Assert.That(borders[0].Bottom, Is.EqualTo(Bottom(layout, 1) + Line.Room).Within(1e-4));
        });
    }

    [Test]
    public void AFrameGoesOnNoNegativeWay()
    {
        var goesOn = new ParagraphAttributes { BordersContinueAbove = 2 };

        Assert.Multiple(() =>
        {
            Assert.Throws<ArgumentOutOfRangeException>(() => _ = new ParagraphAttributes { BordersContinueAbove = -1 });
            Assert.Throws<ArgumentOutOfRangeException>(() =>
                _ = new ParagraphAttributes { BordersContinueBelow = double.PositiveInfinity });
            Assert.That(goesOn, Is.Not.EqualTo(new ParagraphAttributes()));
            Assert.That(new ParagraphAttributes().With(goesOn).BordersContinueAbove, Is.EqualTo(2));
            Assert.That(goesOn.SharesBordersWith(Boxed()), Is.False, "it draws no lines of its own");
        });
    }

    [Test]
    public void AnEmptyParagraphWithABorderBelowDrawsItsLine()
    {
        var layout = Lay(Paragraphs(("one", new ParagraphAttributes()),
            ("", new ParagraphAttributes { BorderBottom = Line })));
        var borders = Borders(layout);

        Assert.Multiple(() =>
        {
            Assert.That(layout.LineCount, Is.EqualTo(2));
            Assert.That(borders, Has.Count.EqualTo(1));
            Assert.That(borders[0].Width, Is.EqualTo(300).Within(1e-4));
            Assert.That(borders[0].Top, Is.GreaterThanOrEqualTo(Bottom(layout, 1) - 1e-4));
            Assert.That(borders[0].Bottom, Is.EqualTo(layout.TextBottom).Within(1e-4));
        });
    }
}
