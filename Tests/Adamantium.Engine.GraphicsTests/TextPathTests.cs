using System;
using System.IO;
using System.Linq;
using Adamantium.Fonts;
using Adamantium.Graphics.Fonts;
using Adamantium.Mathematics;
using NUnit.Framework;

namespace Adamantium.Engine.GraphicsTests;

// A line of text along a path, all of it in one place: where the letters stand, what is done with text longer than the
// path, a closed path gone round across its seam, the effects, bounds and hits.
[TestFixture]
public class TextPathTests
{
    private const string Text = "abcdefghij";

    private static Vector2<double> V(double x, double y) => new(x, y);

    private static TextLayout Layout(string text = Text)
    {
        var typeface = Typeface.LoadFont(Path.Combine(TestContext.CurrentContext.TestDirectory, "Fonts",
            "SourceSans3-Regular.ttf"));
        var layout = new TextLayout(typeface, typeface.Fonts[0]);
        layout.ProcessText(text, 20, new Size<double>(double.PositiveInfinity, double.PositiveInfinity),
            TextWrapping.NoWrap, TextTrimming.None, HorizontalTextAlignment.Left, VerticalTextAlignment.Top);
        return layout;
    }

    private static TextPath On(TextLayout layout, double length, bool leftward = false)
    {
        var path = new TextPath(layout);
        path.SetPath(leftward ? [V(10 + length, 50), V(10, 50)] : [V(10, 50), V(10 + length, 50)], false);
        return path;
    }

    [Test]
    public void TextLongerThanThePath_IsClipped_ItsLettersOnThePathDrawn()
    {
        var layout = Layout();
        var path = On(layout, layout.CalculatedLayoutSize.Width / 2);

        var placed = path.Place(new TextPathOptions());

        Assert.Multiple(() =>
        {
            Assert.That(placed, Is.True);
            Assert.That(path.Glyphs.Count(g => g.IsShown), Is.GreaterThan(2).And.LessThan(Text.Length));
            Assert.That(path.Glyphs[0].IsShown, Is.True, "from the start");
            Assert.That(path.Glyphs[^1].IsShown, Is.False);
        });
    }

    [Test]
    public void TextLongerThanThePath_Extended_GoesOnPastItsEnd()
    {
        var layout = Layout();
        var path = On(layout, layout.CalculatedLayoutSize.Width / 2);

        path.Place(new TextPathOptions { Overflow = TextPathOverflow.Extend });

        Assert.Multiple(() =>
        {
            Assert.That(path.Glyphs, Has.All.Matches<TextPathGlyph>(g => g.IsShown));
            Assert.That(path.Glyphs[^1].Point.X, Is.GreaterThan(10 + path.Length));
            Assert.That(path.Glyphs[^1].Point.Y, Is.EqualTo(50).Within(1e-9), "straight on, the way the end runs");
        });
    }

    [Test]
    public void TextLongerThanThePath_HiddenWhole_IsNotDrawn()
    {
        var layout = Layout();
        var path = On(layout, layout.CalculatedLayoutSize.Width / 2);

        Assert.That(path.Place(new TextPathOptions { Overflow = TextPathOverflow.Hide }), Is.False);
    }

    [Test]
    public void TextLongerThanThePath_Shrunk_FitsWhole()
    {
        var layout = Layout();
        var path = On(layout, layout.CalculatedLayoutSize.Width / 2);

        path.Place(new TextPathOptions { Overflow = TextPathOverflow.Shrink });

        Assert.Multiple(() =>
        {
            Assert.That(path.Scale, Is.EqualTo(0.5).Within(1e-9));
            Assert.That(path.Glyphs, Has.All.Matches<TextPathGlyph>(g => g.IsShown));
        });
    }

    [Test]
    public void TextLongerThanThePath_WithAnEllipsis_IsCutToIt()
    {
        var layout = Layout();
        var path = On(layout, layout.CalculatedLayoutSize.Width / 2);

        path.Place(new TextPathOptions { Overflow = TextPathOverflow.Ellipsis });

        Assert.Multiple(() =>
        {
            Assert.That(path.Layout, Is.Not.SameAs(layout));
            Assert.That(path.Layout.CalculatedLayoutSize.Width, Is.LessThanOrEqualTo(path.Length + 1e-6));
            Assert.That(path.Glyphs, Has.All.Matches<TextPathGlyph>(g => g.IsShown));
        });
    }

    [Test]
    public void TextPastThePathsStart_WithAnEllipsis_StartsWithIt()
    {
        var layout = Layout();
        var path = On(layout, layout.CalculatedLayoutSize.Width / 2);

        path.Place(new TextPathOptions { Overflow = TextPathOverflow.Ellipsis, Alignment = TextPathAlignment.End });

        Assert.Multiple(() =>
        {
            Assert.That(path.Layout.Text, Does.StartWith("…").And.EndWith("j"));
            Assert.That(path.Layout.CalculatedLayoutSize.Width, Is.LessThanOrEqualTo(path.Length + 1e-6));
        });
    }

    [Test]
    public void TextShorterThanThePath_MovedPastItsEnd_WithAnEllipsis_EndsWithIt()
    {
        var layout = Layout();
        var path = On(layout, layout.CalculatedLayoutSize.Width * 2);

        path.Place(new TextPathOptions
        {
            Overflow = TextPathOverflow.Ellipsis, StartOffset = layout.CalculatedLayoutSize.Width * 1.5,
        });

        Assert.Multiple(() =>
        {
            Assert.That(path.Layout.Text, Does.StartWith("a").And.EndWith("…"));
            Assert.That(path.Glyphs[^1].Point.X, Is.LessThanOrEqualTo(10 + path.Length));
        });
    }

    [Test]
    public void TextPastBothEnds_WithAnEllipsis_HasOneAtEach()
    {
        var layout = Layout();
        var path = On(layout, layout.CalculatedLayoutSize.Width / 2);

        path.Place(new TextPathOptions { Overflow = TextPathOverflow.Ellipsis, Alignment = TextPathAlignment.Center });

        Assert.That(path.Layout.Text, Does.StartWith("…").And.EndWith("…"));
    }

    [Test]
    public void APathEndingWhereItStarts_IsClosed_AndTheTextGoesAcrossItsSeam()
    {
        var layout = Layout();
        var path = new TextPath(layout);
        var circle = Enumerable.Range(0, 65).Select(i => i * Math.PI * 2 / 64)
            .Select(a => V(100 + 60 * Math.Sin(a), 100 + 60 * Math.Cos(a))).ToArray();
        circle[^1] += V(0.01, 0);
        path.SetPath(circle, false);

        foreach (var offset in new[] { -200.0, -50, 0, 50, 300 })
        {
            Assert.That(path.Place(new TextPathOptions { StartOffset = offset }), Is.True, $"along {offset}");
            Assert.That(path.Glyphs, Has.All.Matches<TextPathGlyph>(g => g.IsShown), $"along {offset}");
        }

        Assert.That(path.IsClosed, Is.True);
    }

    [Test]
    public void APathRunningLeft_PushedOnPastItsEnds_IsStillReadLeftToRight()
    {
        var layout = Layout();
        var path = new TextPath(layout);
        path.SetPath([V(0, 0), V(-50, 50), V(-10, 50)], false);

        path.Place(new TextPathOptions { Overflow = TextPathOverflow.Extend, Alignment = TextPathAlignment.End });

        var shown = path.Glyphs.Where(g => g.IsShown).ToArray();
        Assert.That(shown[0].Point.X, Is.LessThan(shown[^1].Point.X));
    }

    [Test]
    public void ATinyClosedPath_WithLongText_IsPlacedAtOnce()
    {
        var layout = Layout();
        var path = new TextPath(layout);
        path.SetPath([V(0, 0), V(1e-6, 0), V(1e-6, 1e-6), V(0, 1e-6)], true);

        var watch = System.Diagnostics.Stopwatch.StartNew();
        path.Place(new TextPathOptions());

        Assert.That(watch.ElapsedMilliseconds, Is.LessThan(500));
    }

    [Test]
    public void WholeOrNothing_RoundAClosedPathShorterThanTheText_IsNothing()
    {
        var layout = Layout();
        var path = new TextPath(layout);
        var side = layout.CalculatedLayoutSize.Width / 8;
        path.SetPath([V(0, 0), V(side, 0), V(side, side), V(0, side)], true);

        Assert.That(path.Place(new TextPathOptions { Overflow = TextPathOverflow.Hide }), Is.False);
    }

    [TestCase(TextPathAlignment.Start)]
    [TestCase(TextPathAlignment.Center)]
    [TestCase(TextPathAlignment.End)]
    public void WholeOrNothing_TextJustFittingThePath_IsDrawn(TextPathAlignment alignment)
    {
        var layout = Layout();
        foreach (var leftward in new[] { false, true })
        {
            var path = On(layout, layout.CalculatedLayoutSize.Width * 1.0000000001, leftward);

            Assert.That(path.Place(new TextPathOptions { Overflow = TextPathOverflow.Hide, Alignment = alignment }),
                Is.True, $"leftward {leftward}");
        }
    }

    [Test]
    public void TextCutWithAnEllipsis_KeepsTheLayoutsLook()
    {
        var layout = Layout();
        layout.GlyphOutline = new GlyphOutline(Colors.Black, 2);
        layout.LineHeight = 40;
        var path = On(layout, layout.CalculatedLayoutSize.Width / 2);

        path.Place(new TextPathOptions { Overflow = TextPathOverflow.Ellipsis });

        Assert.Multiple(() =>
        {
            Assert.That(path.Layout.GlyphOutline, Is.EqualTo(layout.GlyphOutline));
            Assert.That(path.Layout.LineHeight, Is.EqualTo(40));
        });
    }

    [Test]
    public void APathRunningLeft_IsReadLeftToRight()
    {
        var layout = Layout();
        var path = On(layout, 300, leftward: true);

        path.Place(new TextPathOptions());

        Assert.Multiple(() =>
        {
            Assert.That(path.Glyphs[0].Point.X, Is.LessThan(path.Glyphs[^1].Point.X));
            Assert.That(path.Glyphs.Select(g => g.Angle), Is.All.EqualTo(0).Within(1e-9));
        });
    }

    [Test]
    public void AtTheEnd_TheTextEndsWhereThePathDoes()
    {
        var layout = Layout();
        var path = On(layout, 300);

        path.Place(new TextPathOptions { Alignment = TextPathAlignment.End });

        Assert.That(path.Glyphs[^1].Point.X, Is.GreaterThan(310 - 20).And.LessThan(310));
    }

    [Test]
    public void ALetterNotDrawn_IsDrawnAsNothing()
    {
        var layout = Layout();
        var path = On(layout, layout.CalculatedLayoutSize.Width / 2);
        path.Place(new TextPathOptions());

        var effects = path.Effects();

        var hidden = effects[^1].Transform;
        Assert.Multiple(() =>
        {
            Assert.That(effects, Has.Length.EqualTo(path.Glyphs.Count));
            Assert.That((hidden.M11, hidden.M12, hidden.M21, hidden.M22), Is.EqualTo((0f, 0f, 0f, 0f)));
        });
    }

    [Test]
    public void AClickOnALetter_Hits_AndTheBoundsHoldTheLetters()
    {
        var layout = Layout();
        var path = On(layout, 300);
        path.Place(new TextPathOptions());

        var hasBounds = path.TryGetBounds(out var min, out var max);

        Assert.Multiple(() =>
        {
            Assert.That(path.HitTest(path.Glyphs[3].Point + V(0, -6)), Is.True, "on a letter, above the path");
            Assert.That(path.HitTest(V(160, 120)), Is.False, "far under the path");
            Assert.That(hasBounds, Is.True);
            Assert.That(min.Y, Is.LessThan(50 - 10));
            Assert.That(max.X, Is.GreaterThan(path.Glyphs[^1].Point.X));
        });
    }
}
