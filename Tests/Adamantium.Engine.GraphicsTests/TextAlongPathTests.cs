using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using Adamantium.Fonts;
using Adamantium.Graphics.Fonts;
using Adamantium.Mathematics;
using NUnit.Framework;

namespace Adamantium.Engine.GraphicsTests;

// Text laid along a polyline: the clusters of a layout, each stood on the path where its share of the text falls and
// turned the way the path runs there, and the effect that draws it there.
[TestFixture]
public class TextAlongPathTests
{
    private static Vector2<double> V(double x, double y) => new(x, y);

    private static GlyphWordData Glyph(int position, double advance, float x, float inkWidth) =>
        new(null, ' ', new Rectangle<float>(x, 0, inkWidth, 10), position, 0) { Advance = advance };

    private static (int, int)[] Ranges(GlyphCluster[] clusters) => clusters.Select(c => (c.Start, c.Length)).ToArray();

    [Test]
    public void ALigature_IsOneClusterOfItsCharacters()
    {
        // "fix": "fi" one glyph.
        var clusters = GlyphCluster.Of([Glyph(0, 10, 0, 10), Glyph(2, 5, 10, 5)], 3);

        Assert.That(Ranges(clusters), Is.EqualTo(new[] { (0, 2), (2, 1) }));
    }

    [Test]
    public void AMarkWithInk_GoesWithItsBase()
    {
        var clusters = GlyphCluster.Of([Glyph(0, 10, 0, 10), Glyph(1, 0, 3, 4), Glyph(2, 8, 10, 8)], 3);

        Assert.Multiple(() =>
        {
            Assert.That(Ranges(clusters), Is.EqualTo(new[] { (0, 2), (2, 1) }));
            Assert.That(clusters[0].X, Is.EqualTo(5));
        });
    }

    [Test]
    public void AVowelDrawnInTwoParts_IsOneCluster()
    {
        var clusters = GlyphCluster.Of([Glyph(0, 6, 0, 6), Glyph(1, 8, 6, 8), Glyph(0, 6, 14, 6)], 2);

        Assert.That(Ranges(clusters), Is.EqualTo(new[] { (0, 1), (1, 1) }));
    }

    [Test]
    public void RightToLeftText_StartingWithAnInvisibleMark_HasEveryCharacterInACluster()
    {
        // A right-to-left mark, then three letters, laid out from the last letter.
        var clusters = GlyphCluster.Of(
            [Glyph(3, 8, 0, 8), Glyph(2, 8, 8, 8), Glyph(1, 8, 16, 8), Glyph(0, 0, 24, 0)], 4);

        Assert.That(Ranges(clusters), Is.EqualTo(new[] { (3, 1), (2, 1), (0, 2) }));
    }

    [Test]
    public void ALaidOutText_IsAClusterACharacter_InOrder()
    {
        var typeface = Typeface.LoadFont(Path.Combine(TestContext.CurrentContext.TestDirectory, "Fonts",
            "SourceSans3-Regular.ttf"));
        var layout = new TextLayout(typeface, typeface.Fonts[0]);
        layout.ProcessText("abc", 40, new Size<double>(double.NaN, double.NaN), TextWrapping.NoWrap, TextTrimming.None,
            HorizontalTextAlignment.Left, VerticalTextAlignment.Top);

        var clusters = layout.GetClusters();

        Assert.Multiple(() =>
        {
            Assert.That(Ranges(clusters), Is.EqualTo(new[] { (0, 1), (1, 1), (2, 1) }));
            Assert.That(clusters[0].X, Is.LessThan(clusters[1].X));
            Assert.That(clusters[1].X, Is.LessThan(clusters[2].X));
        });
    }

    // Three clusters 10 apart in a text 30 wide.
    private static readonly GlyphCluster[] Three = [new(0, 1, 0, 5), new(1, 1, 0, 15), new(2, 1, 0, 25)];

    [Test]
    public void TextOfTwoLines_IsNotPlaced()
    {
        var placed = new List<GlyphOnPath>();
        GlyphCluster[] twoLines = [new(0, 1, 0, 5), new(1, 1, 1, 5)];

        Assert.That(TextAlongPath.TryPlace(twoLines, 10, [V(0, 0), V(100, 0)], 50, 1, Math.PI, true, placed),
            Is.False);
    }

    [TestCase(double.NaN, 1, Math.PI)]
    [TestCase(50, double.NaN, Math.PI)]
    [TestCase(50, -1, Math.PI)]
    [TestCase(50, 1, double.NaN)]
    [TestCase(50, 1, -0.1)]
    public void WhatIsNotANumberOrBelowNothing_PlacesNothing(double middle, double scale, double maxTurn)
    {
        var placed = new List<GlyphOnPath>();

        Assert.That(TextAlongPath.TryPlace(Three, 30, [V(0, 0), V(100, 0)], middle, scale, maxTurn, true, placed),
            Is.False);
    }

    [Test]
    public void AHairpinBetweenTwoClusters_IsATurn()
    {
        var placed = new List<GlyphOnPath>();
        // Right to 45, a spike up and back down between the first two clusters (at 40 and 50), then right again.
        Vector2<double>[] spike = [V(0, 0), V(45, 0), V(45, -1), V(45, 0), V(100, 0)];

        Assert.That(TextAlongPath.TryPlace(Three, 30, spike, 52, 1, Math.PI / 4, true, placed), Is.False);
    }

    [Test]
    public void OnAStraightPath_TheClustersStandAlongIt_AboutTheMiddle()
    {
        var placed = new List<GlyphOnPath>();

        var fits = TextAlongPath.TryPlace(Three, 30, [V(0, 50), V(100, 50)], 40, 2, Math.PI, true, placed);

        Assert.Multiple(() =>
        {
            Assert.That(fits, Is.True);
            Assert.That(placed.Select(p => p.Point), Is.EqualTo(new[] { V(20, 50), V(40, 50), V(60, 50) }));
            Assert.That(placed.Select(p => p.Angle), Is.All.EqualTo(0));
        });
    }

    [Test]
    public void APathRunningLeft_IsReadFromItsEnd_KeptUpright()
    {
        var placed = new List<GlyphOnPath>();

        TextAlongPath.TryPlace(Three, 30, [V(100, 50), V(0, 50)], 50, 1, Math.PI, true, placed);

        Assert.Multiple(() =>
        {
            Assert.That(placed.Select(p => p.Point), Is.EqualTo(new[] { V(40, 50), V(50, 50), V(60, 50) }));
            Assert.That(placed.Select(p => p.Angle), Is.All.EqualTo(0));
        });
    }

    [Test]
    public void APathRunningLeft_NotKeptUpright_TurnsTheTextOver()
    {
        var placed = new List<GlyphOnPath>();

        TextAlongPath.TryPlace(Three, 30, [V(100, 50), V(0, 50)], 50, 1, Math.PI, false, placed);

        Assert.Multiple(() =>
        {
            Assert.That(placed.Select(p => p.Point), Is.EqualTo(new[] { V(60, 50), V(50, 50), V(40, 50) }));
            Assert.That(placed.Select(p => Math.Abs(p.Angle)), Is.All.EqualTo(Math.PI));
        });
    }

    [TestCase(10)]
    [TestCase(90)]
    public void TextPastAnEnd_DoesNotFit(double middle)
    {
        var placed = new List<GlyphOnPath>();

        var fits = TextAlongPath.TryPlace(Three, 30, [V(0, 0), V(100, 0)], middle, 1, Math.PI, true, placed);

        Assert.Multiple(() =>
        {
            Assert.That(fits, Is.False);
            Assert.That(placed, Is.Empty);
        });
    }

    [Test]
    public void ATurnSharperThanAllowed_DoesNotFit()
    {
        var placed = new List<GlyphOnPath>();
        Vector2<double>[] corner = [V(0, 0), V(50, 0), V(50, 50)];

        var tight = TextAlongPath.TryPlace(Three, 30, corner, 50, 1, Math.PI / 4, true, placed);
        var loose = TextAlongPath.TryPlace(Three, 30, corner, 50, 1, Math.PI, true, placed);

        Assert.Multiple(() =>
        {
            Assert.That(tight, Is.False);
            Assert.That(loose, Is.True);
            Assert.That(placed[2].Angle, Is.EqualTo(Math.PI / 2).Within(1e-12));
        });
    }

    [Test]
    public void TheEffect_PutsTheClustersMiddleOnItsPoint_TurnedAndScaled()
    {
        var cluster = new GlyphCluster(1, 1, 0, 15);
        var placed = new GlyphOnPath(V(100, 200), Math.PI / 2);

        var effect = TextAlongPath.Effect(cluster, placed, 2, 6);

        var t = effect.Transform;
        Vector2<double> At(double x, double y) => V(x * t.M11 + y * t.M21 + t.M31, x * t.M12 + y * t.M22 + t.M32);
        Assert.Multiple(() =>
        {
            Assert.That((effect.Start, effect.Length), Is.EqualTo((1, 1)));
            Assert.That(At(15, 6).X, Is.EqualTo(100).Within(1e-3));
            Assert.That(At(15, 6).Y, Is.EqualTo(200).Within(1e-3));
            Assert.That(At(16, 6).X, Is.EqualTo(100).Within(1e-3), "along the text is down the path");
            Assert.That(At(16, 6).Y, Is.EqualTo(202).Within(1e-3));
        });
    }
}
