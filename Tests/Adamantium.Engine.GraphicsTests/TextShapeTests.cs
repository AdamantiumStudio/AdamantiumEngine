using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using Adamantium.Fonts;
using Adamantium.Graphics.Fonts;
using Adamantium.Mathematics;
using NUnit.Framework;

namespace Adamantium.Engine.GraphicsTests;

// Text set inside a shape, or flowing round one: a row takes the stretches inside the shape all the way down it, or
// that nothing of the shape reaches.
[TestFixture]
public class TextShapeTests
{
    private const string Words = "the quick brown fox jumps over the lazy dog and runs on past the old mill by the river ";

    private static Vector2<double> V(double x, double y) => new(x, y);

    private static Vector2<double>[] Square(double left, double top, double side) =>
        [V(left, top), V(left + side, top), V(left + side, top + side), V(left, top + side)];

    private static Vector2<double>[] Circle(double x, double y, double radius) =>
        Enumerable.Range(0, 96).Select(i => i * Math.PI * 2 / 96)
            .Select(a => V(x + radius * Math.Cos(a), y + radius * Math.Sin(a))).ToArray();

    private static TextLayout Layout(string text, TextShape shape = null, TextShape around = null)
    {
        var typeface = Typeface.LoadFont(Path.Combine(TestContext.CurrentContext.TestDirectory, "Fonts",
            "SourceSans3-Regular.ttf"));
        var layout = new TextLayout(typeface, typeface.Fonts[0])
        {
            Shape = shape,
            ExclusionShapes = around == null ? null : [around],
            Frames = around == null ? null : [new RectangleF(0, 0, 400, 400)],
        };
        layout.ProcessText(text, 14, new Size<double>(400, 400), TextWrapping.WrapByWords, TextTrimming.None,
            HorizontalTextAlignment.Left, VerticalTextAlignment.Top);
        return layout;
    }

    private static bool InCircle(RectangleF box, double x, double y, double radius) =>
        new[] { V(box.X, box.Y), V(box.X + box.Width, box.Y), V(box.X, box.Y + box.Height),
                V(box.X + box.Width, box.Y + box.Height) }
            .All(corner => (corner - V(x, y)).Length() <= radius + 0.5);

    [Test]
    public void ASquare_IsOneStretch_PaddedIn()
    {
        var spans = new List<(double Left, double Right)>();

        new TextShape([Square(10, 10, 100)], 5).Inside(20, 40, spans);

        Assert.That(spans, Is.EqualTo(new[] { (15.0, 105.0) }));
    }

    [Test]
    public void ARing_LeavesItsHoleOut()
    {
        var spans = new List<(double Left, double Right)>();
        var ring = new TextShape([Square(0, 0, 100), Square(40, 40, 20)]);

        ring.Inside(45, 55, spans);

        Assert.That(spans, Is.EqualTo(new[] { (0.0, 40.0), (60.0, 100.0) }));
    }

    [Test]
    public void ARowPastTheShape_HasNoStretch()
    {
        var spans = new List<(double Left, double Right)>();

        new TextShape([Square(0, 0, 100)]).Inside(90, 110, spans);

        Assert.That(spans, Is.Empty);
    }

    [Test]
    public void WhatAShapeCovers_IsAllItReachesDownTheRow()
    {
        var spans = new List<(double Left, double Right)>();
        Vector2<double>[] triangle = [V(50, 0), V(100, 100), V(0, 100)];

        new TextShape([triangle], 2).Covered(40, 60, spans);

        // The padding reaches down to 62, where the triangle is 19 to 81 across.
        Assert.That(spans.Single().Left, Is.EqualTo(17).Within(1e-9));
        Assert.That(spans.Single().Right, Is.EqualTo(83).Within(1e-9));
    }

    [Test]
    public void AThinSteepStroke_CoversAllItCrossesDownTheRow()
    {
        var spans = new List<(double Left, double Right)>();

        new TextShape([[V(0, 0), V(1, 0), V(101, 100), V(100, 100)]]).Covered(40, 60, spans);

        Assert.Multiple(() =>
        {
            Assert.That(spans, Has.Count.EqualTo(1), "one stretch, nothing open across the stroke");
            Assert.That(spans[0].Left, Is.LessThanOrEqualTo(40));
            Assert.That(spans[0].Right, Is.GreaterThanOrEqualTo(61));
        });
    }

    [Test]
    public void ABowTie_IsInsideOnlyWhereItIsAllTheWayDown()
    {
        var spans = new List<(double Left, double Right)>();
        var bowTie = new TextShape([[V(0, 0), V(10, 10), V(0, 10), V(10, 0)]]);

        bowTie.Inside(4, 6, spans);

        Assert.That(spans, Is.Empty, "it narrows to a point at 5");
    }

    [Test]
    public void ARowEndingOnTheShapesFlatBottom_IsInside()
    {
        var spans = new List<(double Left, double Right)>();

        new TextShape([Square(0, 0, 100)]).Inside(80, 100, spans);

        Assert.That(spans, Is.EqualTo(new[] { (0.0, 100.0) }));
    }

    [Test]
    public void ShortText_InAShapeFarBelowTheFrameTop_IsNotOverset()
    {
        var typeface = Typeface.LoadFont(Path.Combine(TestContext.CurrentContext.TestDirectory, "Fonts",
            "SourceSans3-Regular.ttf"));
        var layout = new TextLayout(typeface, typeface.Fonts[0])
        {
            Shape = new TextShape([Square(0, 350, 100)]),
            Frames = [new RectangleF(0, 0, 400, 500)],
        };
        layout.ProcessText("Hi", 14, new Size<double>(400, 500), TextWrapping.WrapByWords, TextTrimming.None,
            HorizontalTextAlignment.Left, VerticalTextAlignment.Top);

        Assert.Multiple(() =>
        {
            Assert.That(layout.OversetIndex, Is.EqualTo(2));
            Assert.That(layout.GetTextData()[0].Rect.Y, Is.GreaterThanOrEqualTo(350));
        });
    }

    [Test]
    public void TextInACircle_StaysInside_LinesLongestAcrossTheMiddle()
    {
        var layout = Layout(Words + Words, new TextShape([Circle(200, 200, 150)]));

        var glyphs = layout.GetTextData().Where(g => g.Rect.Width > 0).ToArray();
        var rows = glyphs.GroupBy(g => g.LineIndex).Select(row => row.Max(g => g.Rect.X + g.Rect.Width) - row.Min(g => g.Rect.X))
            .ToArray();

        Assert.Multiple(() =>
        {
            Assert.That(glyphs, Is.Not.Empty);
            Assert.That(glyphs, Has.All.Matches<GlyphWordData>(g => InCircle(g.Rect, 200, 200, 150)));
            Assert.That(rows.Max(), Is.GreaterThan(rows[0] + 50), "the middle rows are the long ones");
        });
    }

    [Test]
    public void TextInARing_LeavesTheHoleEmpty()
    {
        var layout = Layout(Words + Words + Words, new TextShape([Circle(200, 200, 180), Circle(200, 200, 60)]));

        var glyphs = layout.GetTextData().Where(g => g.Rect.Width > 0).ToArray();

        Assert.Multiple(() =>
        {
            Assert.That(glyphs, Is.Not.Empty);
            Assert.That(glyphs, Has.None.Matches<GlyphWordData>(g => InCircle(g.Rect, 200, 200, 60)));
        });
    }

    [Test]
    public void TextRoundACircle_KeepsOutOfIt()
    {
        var layout = Layout(Words + Words + Words, around: new TextShape([Circle(200, 120, 60)], 4));

        var glyphs = layout.GetTextData().Where(g => g.Rect.Width > 0).ToArray();
        bool Touches(RectangleF box) =>
            Math.Max(box.X, Math.Min(200, box.X + box.Width)) is var nx &&
            Math.Max(box.Y, Math.Min(120, box.Y + box.Height)) is var ny &&
            (V(nx, ny) - V(200, 120)).Length() < 60;

        Assert.Multiple(() =>
        {
            Assert.That(glyphs, Is.Not.Empty);
            Assert.That(glyphs, Has.None.Matches<GlyphWordData>(g => Touches(g.Rect)));
            Assert.That(glyphs.Any(g => g.Rect.Y > 60 && g.Rect.Y < 180 && g.Rect.X > 260), Is.True,
                "lines go on beside it");
        });
    }

    [Test]
    public void TextPastTheShape_IsOverset()
    {
        var layout = Layout(string.Concat(Enumerable.Repeat(Words, 10)), new TextShape([Square(0, 0, 120)]));

        Assert.That(layout.OversetIndex, Is.LessThan(Words.Length * 10));
    }
}
