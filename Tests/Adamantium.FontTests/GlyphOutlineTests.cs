using System;
using System.Linq;
using System.Threading;
using Adamantium.Fonts;
using Adamantium.Fonts.Common;
using Adamantium.Mathematics;
using NUnit.Framework;

namespace Adamantium.FontTests;

public class GlyphOutlineTests
{
    private const int Threads = 8;

    [TestCase("TTFFonts/SourceSans3-Regular.ttf")]
    [TestCase("OTFFonts/SourceSans3-Regular.otf")]
    [TestCase("OTFFonts/CFF2/AdobeVFPrototype.otf")]
    [TestCase("WoffFonts/Sarabun-Regular.woff")]
    [TestCase("WoffFonts/Sarabun-Regular.woff2")]
    [TestCase("OTFFonts/Quicksand-Bold.otf")]
    [TestCase("source-sans-3v028R/VAR/SourceSans3VF-Roman.otf")]
    public void EveryGlyphBuildsOutlinesInsideItsBounds(string path)
    {
        var typeface = Typeface.LoadFont(path, 3);

        foreach (var glyph in typeface.Glyphs)
        {
            if (glyph.IsInvalid)
            {
                Assert.Fail($"glyph {glyph.Index} could not be read");
            }

            var bounds = glyph.BoundingRectangle;
            var minX = Math.Min(bounds.Left, bounds.Right) - 1;
            var maxX = Math.Max(bounds.Left, bounds.Right) + 1;
            var minY = Math.Min(bounds.Top, bounds.Bottom) - 1;
            var maxY = Math.Max(bounds.Top, bounds.Bottom) + 1;
            foreach (var point in glyph.TransformBasicOutlines(Matrix3x2<double>.Identity).SelectMany(x => x.Points))
            {
                if (!point.IsControl && (point.X < minX || point.X > maxX || point.Y < minY || point.Y > maxY))
                {
                    Assert.Fail($"glyph {glyph.Index} has a point at {point.X},{point.Y} outside its bounds " +
                                $"{bounds.Left},{bounds.Top},{bounds.Right},{bounds.Bottom}");
                }
            }
        }
    }

    [TestCase("TTFFonts/SourceSans3-Regular.ttf")]
    [TestCase("WoffFonts/Sarabun-Regular.woff2")]
    public void CompositeGlyphHoldsEveryOutlineOfItsComponents(string path)
    {
        var typeface = Typeface.LoadFont(path, 3);
        var glyphs = typeface.Glyphs.ToArray();

        foreach (var glyph in glyphs.Where(x => x.IsComposite))
        {
            var expected = glyph.CompositeGlyphComponents
                .Sum(x => glyphs[x.SimpleGlyphIndex].TransformBasicOutlines(Matrix3x2<double>.Identity).Count);
            var actual = glyph.TransformBasicOutlines(Matrix3x2<double>.Identity).Count;
            if (actual != expected)
            {
                Assert.Fail($"composite glyph {glyph.Index} ({glyph.Name}) has {actual} outlines, its components {expected}");
            }
        }
    }

    [TestCase("WoffFonts/Sarabun-Regular.woff", "WoffFonts/Sarabun-Regular.woff2")]
    [TestCase("TTFFonts/SourceSans3-Regular.ttf", "source-sans-3v028R/WOFF2/TTF/SourceSans3-Regular.ttf.woff2")]
    public void Woff2DecodesToTheSameGlyphsAsTheFontItPacks(string original, string packed)
    {
        var expectedGlyphs = Typeface.LoadFont(original, 3).Glyphs.ToArray();
        var woff2 = Typeface.LoadFont(packed, 3).Glyphs.ToArray();

        Assert.That(woff2, Has.Length.EqualTo(expectedGlyphs.Length));
        for (var i = 0; i < expectedGlyphs.Length; i++)
        {
            var expected = $"{expectedGlyphs[i].BoundingRectangle}, {Describe(expectedGlyphs[i])}, " +
                           $"instructions {Convert.ToHexString(expectedGlyphs[i].Instructions ?? [])}";
            var actual = $"{woff2[i].BoundingRectangle}, {Describe(woff2[i])}, " +
                         $"instructions {Convert.ToHexString(woff2[i].Instructions ?? [])}";
            if (actual != expected)
            {
                Assert.Fail($"glyph {expectedGlyphs[i].Index} ({expectedGlyphs[i].Name}) decodes from WOFF2 as {actual}, " +
                            $"from {original} as {expected}");
            }
        }
    }

    [Test]
    public void OutlinesAskedFromManyThreadsAreBuiltOnce()
    {
        const string path = "OTFFonts/SourceSans3-Regular.otf";
        var expected = Typeface.LoadFont(path, 3).Glyphs.Select(Describe).ToArray();
        var glyphs = Typeface.LoadFont(path, 3).Glyphs.ToArray();

        using var start = new Barrier(Threads);
        var workers = Enumerable.Range(0, Threads).Select(t => new Thread(() =>
        {
            start.SignalAndWait();
            foreach (var glyph in glyphs)
            {
                _ = glyph.HasOutlines;
            }
        })).ToList();
        workers.ForEach(x => x.Start());
        workers.ForEach(x => x.Join());

        for (var i = 0; i < glyphs.Length; i++)
        {
            var actual = Describe(glyphs[i]);
            if (actual != expected[i])
            {
                Assert.Fail($"glyph {glyphs[i].Index} was built as {actual}, alone it is {expected[i]}");
            }
        }
    }

    [Test]
    public void GlyphSampledFromManyThreadsAtOnceIsSampledOnce()
    {
        const string path = "OTFFonts/SourceSans3-Regular.otf";
        const int sampledGlyphs = 40;
        var expected = Typeface.LoadFont(path, 3).Glyphs.Take(sampledGlyphs).Select(x => x.Sample(3).Length).ToArray();
        var glyphs = Typeface.LoadFont(path, 3).Glyphs.Take(sampledGlyphs).ToArray();
        Exception failure = null;

        using var start = new Barrier(Threads);
        var workers = Enumerable.Range(0, Threads).Select(t => new Thread(() =>
        {
            start.SignalAndWait();
            foreach (var glyph in glyphs)
            {
                try
                {
                    glyph.Sample(3);
                }
                catch (Exception e)
                {
                    Interlocked.CompareExchange(ref failure, e, null);
                }
            }
        })).ToList();
        workers.ForEach(x => x.Start());
        workers.ForEach(x => x.Join());

        Assert.That(failure, Is.Null);
        Assert.That(glyphs.Select(x => x.Sample(3).Length), Is.EqualTo(expected));
    }

    // A long curve cut into a fixed number of chords sagged inside it: a quarter arc of radius 480 by some 4 units.
    // Quadratic points are rounded to whole units, which the bound allows for.
    [TestCase("TTFFonts/SourceSans3-Black.ttf", 0.75)]
    [TestCase("OTFFonts/SourceSans3-Regular.otf", 0.0)]
    public void SampledCurves_StayWithinAThousandthOfTheGlyph(string path, double rounding)
    {
        var typeface = Typeface.LoadFont(path, 3);
        var font = typeface.GetFont(0);

        foreach (var character in "oOCGQS@&%")
        {
            var glyph = font.GetGlyphByCharacter(character);
            glyph.Sample(5);
            var chords = glyph.GenerateOutlines(5).SelectMany(x => x.Segments ?? []).ToArray();
            var bound = Math.Max(Math.Max(glyph.BoundingRectangle.Width, glyph.BoundingRectangle.Height) / 1024.0, 0.25)
                        + rounding + 1e-6;
            var worst = 0.0;
            foreach (var curve in glyph.Outlines.SelectMany(x => x.Segments).Where(x => x.Points.Count > 2))
            {
                for (var step = 1; step < 32; step++)
                {
                    var point = Point(curve.Points, step / 32.0);
                    worst = Math.Max(worst, chords.Min(x => GlyphSegmentsMath.GetDistanceToSegment(x, point)));
                }
            }

            Assert.That(worst, Is.LessThanOrEqualTo(bound), $"'{character}'");
        }

        static Vector2<double> Point(System.Collections.Generic.List<Vector2<double>> p, double t) => p.Count == 3
            ? (1 - t) * (1 - t) * p[0] + 2 * t * (1 - t) * p[1] + t * t * p[2]
            : Math.Pow(1 - t, 3) * p[0] + 3 * t * (1 - t) * (1 - t) * p[1] + 3 * t * t * (1 - t) * p[2] +
              Math.Pow(t, 3) * p[3];
    }

    [TestCase("TTFFonts/CascadiaCode-Regular.ttf")]
    [TestCase("source-sans-3v028R/VAR/SourceSans3VF-Roman.otf")]
    [TestCase("OTFFonts/SourceSans3-Regular.otf")]
    public void MergedOutlineFillsWhatTheContoursFill(string path)
    {
        const int grid = 12;
        var typeface = Typeface.LoadFont(path, 3);

        foreach (var glyph in typeface.Glyphs.Where(x => !x.IsEmpty && !x.IsInvalid))
        {
            glyph.Sample(3);
            var contours = glyph.GenerateOutlines(3).SelectMany(x => x.Segments ?? []).ToArray();
            var merged = glyph.GetMergedOutlineSegments().ToArray();
            if (contours.Length == 0)
            {
                continue;
            }

            var minX = contours.Min(x => Math.Min(x.Start.X, x.End.X));
            var maxX = contours.Max(x => Math.Max(x.Start.X, x.End.X));
            var minY = contours.Min(x => Math.Min(x.Start.Y, x.End.Y));
            var maxY = contours.Max(x => Math.Max(x.Start.Y, x.End.Y));
            for (var row = 0; row < grid; row++)
            {
                for (var column = 0; column < grid; column++)
                {
                    var point = new Vector2<double>(minX + (maxX - minX) * (column + 0.5) / grid,
                        minY + (maxY - minY) * (row + 0.5) / grid);
                    if (contours.Any(x => GlyphSegmentsMath.GetDistanceToSegment(x, point) < 1))
                    {
                        continue;
                    }

                    var expected = IsFilled(contours, point);
                    if (IsFilled(merged, point) != expected)
                    {
                        Assert.Fail($"glyph {glyph.Index} ({glyph.Name}) at {point.X},{point.Y} is " +
                                    $"{(expected ? "filled" : "empty")} by its contours, not by its merged outline");
                    }
                }
            }
        }
    }

    private static bool IsFilled(LineSegment2D[] segments, Vector2<double> point)
    {
        var winding = 0;
        foreach (var segment in segments)
        {
            var side = (segment.End.X - segment.Start.X) * (point.Y - segment.Start.Y) -
                       (segment.End.Y - segment.Start.Y) * (point.X - segment.Start.X);
            if (segment.Start.Y <= point.Y && segment.End.Y > point.Y && side > 0)
            {
                winding++;
            }
            else if (segment.End.Y <= point.Y && segment.Start.Y > point.Y && side < 0)
            {
                winding--;
            }
        }

        return winding != 0;
    }

    private static string Describe(Glyph glyph)
    {
        var outlines = glyph.TransformBasicOutlines(Matrix3x2<double>.Identity);
        return $"{outlines.Count} outlines, {outlines.Sum(x => x.Points.Count)} points";
    }
}
