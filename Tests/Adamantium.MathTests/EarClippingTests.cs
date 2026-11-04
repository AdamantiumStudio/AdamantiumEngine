using System;
using System.Collections.Generic;
using System.Linq;
using Adamantium.Mathematics;
using Adamantium.Mathematics.Triangulation;
using NUnit.Framework;

namespace Adamantium.MathTests;

/// <summary>A polygon cut into triangles by clipping ears covers what it covers: the triangles' areas add up to the
/// outer ring's less its holes', none of them turned the wrong way, whichever way round the rings go - and rings that
/// cross, touch or repeat points still end in triangles rather than a loop or a throw.</summary>
[TestFixture]
public class EarClippingTests
{
    private static List<int> Cut(Vector2<double>[] points, params int[] holeStarts)
    {
        var triangles = new List<int>();
        EarClipping.Triangulate(points, holeStarts, triangles);
        return triangles;
    }

    // Every spot of a grid over the polygon is covered once where the rings hold it - an odd number of them - and not
    // at all elsewhere.
    private static void AssertCoversExactly(Vector2<double>[] points, int[] holeStarts, List<int> triangles)
    {
        double left = points.Min(p => p.X), right = points.Max(p => p.X);
        double top = points.Min(p => p.Y), bottom = points.Max(p => p.Y);
        var bad = 0;
        const int steps = 97;
        for (var i = 0; i < steps; i++)
        {
            for (var j = 0; j < steps; j++)
            {
                var spot = new Vector2<double>(left + (right - left) * (i + 0.37) / steps,
                    top + (bottom - top) * (j + 0.61) / steps);
                var covered = 0;
                var onASide = false;
                for (var t = 0; t + 2 < triangles.Count; t += 3)
                {
                    switch (Holds(points[triangles[t]], points[triangles[t + 1]], points[triangles[t + 2]], spot))
                    {
                        case > 0:
                            covered++;
                            break;
                        case 0:
                            onASide = true;
                            break;
                    }
                }

                if (!onASide && covered != (InsideRings(points, holeStarts, spot) ? 1 : 0))
                {
                    bad++;
                }
            }
        }

        Assert.That(bad, Is.Zero, "spots covered other than once inside and never outside");
    }

    // 1 inside the triangle, 0 on a side of it, -1 outside.
    private static int Holds(Vector2<double> a, Vector2<double> b, Vector2<double> c, Vector2<double> p)
    {
        static double Side(Vector2<double> u, Vector2<double> v, Vector2<double> w) =>
            (v.X - u.X) * (w.Y - u.Y) - (v.Y - u.Y) * (w.X - u.X);

        var d1 = Side(a, b, p);
        var d2 = Side(b, c, p);
        var d3 = Side(c, a, p);
        if (d1 > 0 && d2 > 0 && d3 > 0 || d1 < 0 && d2 < 0 && d3 < 0)
        {
            return 1;
        }

        return (d1 == 0 || d2 == 0 || d3 == 0) && (d1 >= 0 && d2 >= 0 && d3 >= 0 || d1 <= 0 && d2 <= 0 && d3 <= 0)
            ? 0
            : -1;
    }

    private static bool InsideRings(Vector2<double>[] points, int[] holeStarts, Vector2<double> p)
    {
        var inside = false;
        var starts = new[] { 0 }.Concat(holeStarts).Append(points.Length).ToArray();
        for (var r = 0; r + 1 < starts.Length; r++)
        {
            for (int i = starts[r], j = starts[r + 1] - 1; i < starts[r + 1]; j = i++)
            {
                if (points[i].Y > p.Y != points[j].Y > p.Y &&
                    p.X < (points[j].X - points[i].X) * (p.Y - points[i].Y) / (points[j].Y - points[i].Y) + points[i].X)
                {
                    inside = !inside;
                }
            }
        }

        return inside;
    }

    private static Vector2<double>[] Square(double left, double top, double side) =>
        [new(left, top), new(left + side, top), new(left + side, top + side), new(left, top + side)];

    [Test]
    public void HolesTouchingAtAPoint_AreLeftOut()
    {
        var outer = Square(0, 0, 100);
        var points = outer.Concat(Square(56, 55, 3)).Concat(Square(59, 58, 3)).ToArray();
        int[] holes = [4, 8];
        var triangles = new List<int>();

        var clean = EarClipping.Triangulate(points, holes, triangles);

        Assert.That(clean, Is.True);
        AssertCoversExactly(points, holes, triangles);
    }

    [Test]
    public void HolesSharingASide_AreLeftOut()
    {
        var points = Square(0, 0, 10).Concat(Square(2, 2, 3)).Concat(Square(5, 2, 3)).ToArray();
        int[] holes = [4, 8];

        var triangles = Cut(points, holes);

        AssertCoversExactly(points, holes, triangles);
    }

    [Test]
    public void RandomHolesTouchingOrNot_AreLeftOut()
    {
        var random = new Random(7);
        for (var run = 0; run < 300; run++)
        {
            var points = new List<Vector2<double>>(Square(0, 0, 40));
            var holes = new List<int>();
            var taken = new HashSet<(int, int)>();
            for (var h = 0; h < random.Next(1, 6); h++)
            {
                var x = random.Next(1, 12) * 3;
                var y = random.Next(1, 12) * 3;
                if (!taken.Add((x, y)) || taken.Contains((x - 3, y)) && random.Next(2) == 0)
                {
                    continue;
                }

                holes.Add(points.Count);
                points.AddRange(Square(x, y, 3));
            }

            var all = points.ToArray();
            var starts = holes.ToArray();
            try
            {
                AssertCoversExactly(all, starts, Cut(all, starts));
            }
            catch (AssertionException)
            {
                TestContext.Out.WriteLine($"run {run}: holes at " + string.Join(" ", starts.Select(s => all[s])));
                throw;
            }
        }
    }

    [Test]
    public void AHoleOutsideTheRing_IsDroppedAndSaid()
    {
        var points = Square(10, 0, 10).Concat(Square(0, 2, 4)).ToArray();
        var triangles = new List<int>();

        var clean = EarClipping.Triangulate(points, [4], triangles);

        Assert.That(clean, Is.False);
        AssertCovers(points, triangles, 100);
    }

    [Test]
    public void HoleStartsOutOfOrder_AreRefused()
    {
        var points = Square(0, 0, 10).Concat(Square(2, 2, 2)).ToArray();

        Assert.Multiple(() =>
        {
            Assert.Throws<ArgumentOutOfRangeException>(() => EarClipping.Triangulate(points, [9], []));
            Assert.Throws<ArgumentOutOfRangeException>(() => EarClipping.Triangulate(points, [6, 4], []));
        });
    }

    private static double Area(ReadOnlySpan<Vector2<double>> ring)
    {
        var twice = 0.0;
        for (int i = 0, j = ring.Length - 1; i < ring.Length; j = i++)
        {
            twice += ring[j].X * ring[i].Y - ring[i].X * ring[j].Y;
        }

        return twice / 2;
    }

    private static double Signed(Vector2<double>[] points, List<int> triangles, int t)
    {
        var a = points[triangles[t]];
        var b = points[triangles[t + 1]];
        var c = points[triangles[t + 2]];
        return ((b.X - a.X) * (c.Y - a.Y) - (b.Y - a.Y) * (c.X - a.X)) / 2;
    }

    private static void AssertCovers(Vector2<double>[] points, List<int> triangles, double area)
    {
        var sum = 0.0;
        var way = Math.Sign(area);
        for (var t = 0; t + 2 < triangles.Count; t += 3)
        {
            var signed = Signed(points, triangles, t);
            Assert.That(Math.Sign(signed), Is.EqualTo(way), $"triangle {t / 3} turns the outer ring's way");
            sum += signed;
        }

        Assert.That(sum, Is.EqualTo(area).Within(Math.Abs(area) * 1e-9 + 1e-9));
    }

    private static Vector2<double>[] Circle(int n, double radius, double cx = 0, double cy = 0, bool clockwise = false)
    {
        var points = new Vector2<double>[n];
        for (var i = 0; i < n; i++)
        {
            var angle = (clockwise ? -1 : 1) * 2 * Math.PI * i / n;
            points[i] = new Vector2<double>(cx + radius * Math.Cos(angle), cy + radius * Math.Sin(angle));
        }

        return points;
    }

    private static Vector2<double>[] Star(int tips, double outer, double inner, double cx = 0, double cy = 0)
    {
        var points = new Vector2<double>[tips * 2];
        for (var i = 0; i < points.Length; i++)
        {
            var radius = i % 2 == 0 ? outer : inner;
            var angle = Math.PI * i / tips;
            points[i] = new Vector2<double>(cx + radius * Math.Cos(angle), cy + radius * Math.Sin(angle));
        }

        return points;
    }

    [Test]
    public void ASquare_IsTwoTriangles()
    {
        Vector2<double>[] square = [new(0, 0), new(10, 0), new(10, 10), new(0, 10)];

        var triangles = Cut(square);

        Assert.That(triangles.Count, Is.EqualTo(6));
        AssertCovers(square, triangles, 100);
    }

    [Test]
    public void AnL_IsCutWithinItself()
    {
        Vector2<double>[] l = [new(0, 0), new(4, 0), new(4, 1), new(1, 1), new(1, 4), new(0, 4)];

        var triangles = Cut(l);

        Assert.That(triangles.Count / 3, Is.EqualTo(4));
        AssertCovers(l, triangles, Area(l));
    }

    [Test]
    public void AClockwiseRing_GivesClockwiseTriangles()
    {
        var ring = Circle(9, 5, clockwise: true);

        var triangles = Cut(ring);

        Assert.That(triangles.Count / 3, Is.EqualTo(7));
        AssertCovers(ring, triangles, Area(ring));
    }

    [TestCase(5)]
    [TestCase(17)]
    [TestCase(200)]
    public void AStar_IsCutWithinItself(int tips)
    {
        var star = Star(tips, 10, 3);

        var triangles = Cut(star);

        Assert.That(triangles.Count / 3, Is.EqualTo(star.Length - 2));
        AssertCovers(star, triangles, Area(star));
    }

    [Test]
    public void AHole_IsLeftOut()
    {
        var outer = Circle(32, 10);
        var hole = Circle(16, 4, clockwise: true);
        var points = outer.Concat(hole).ToArray();

        var triangles = Cut(points, outer.Length);

        Assert.That(triangles.Count / 3, Is.EqualTo(points.Length));
        AssertCovers(points, triangles, Area(outer) + Area(hole));
    }

    [Test]
    public void AHoleGoingTheSameWay_IsLeftOutAllTheSame()
    {
        var outer = Circle(12, 10);
        var hole = Circle(6, 3);
        var points = outer.Concat(hole).ToArray();

        var triangles = Cut(points, outer.Length);

        AssertCovers(points, triangles, Area(outer) - Area(hole));
    }

    [Test]
    public void ManyHoles_AreLeftOut()
    {
        var outer = new Vector2<double>[] { new(0, 0), new(100, 0), new(100, 100), new(0, 100) };
        var points = new List<Vector2<double>>(outer);
        var starts = new List<int>();
        var holesArea = 0.0;
        for (var row = 0; row < 6; row++)
        {
            for (var column = 0; column < 6; column++)
            {
                var hole = Star(5, 6, 2, 10 + column * 16, 10 + row * 16).Reverse().ToArray();
                starts.Add(points.Count);
                points.AddRange(hole);
                holesArea += Area(hole);
            }
        }

        var all = points.ToArray();
        var triangles = Cut(all, starts.ToArray());

        AssertCovers(all, triangles, Area(outer) + holesArea);
    }

    [Test]
    public void ALargeRing_IsCutThroughTheGrid()
    {
        var random = new Random(3);
        var ring = new Vector2<double>[5000];
        for (var i = 0; i < ring.Length; i++)
        {
            var angle = 2 * Math.PI * i / ring.Length;
            var radius = 50 + random.NextDouble() * 20;
            ring[i] = new Vector2<double>(radius * Math.Cos(angle), radius * Math.Sin(angle));
        }

        var triangles = Cut(ring);

        Assert.That(triangles.Count / 3, Is.EqualTo(ring.Length - 2));
        AssertCovers(ring, triangles, Area(ring));
    }

    [Test]
    public void RepeatedAndLinedUpPoints_AreDropped()
    {
        Vector2<double>[] ring =
        [
            new(0, 0), new(0, 0), new(5, 0), new(10, 0), new(10, 0), new(10, 5), new(10, 10), new(5, 10), new(0, 10),
            new(0, 5), new(0, 0),
        ];

        var triangles = Cut(ring);

        AssertCovers(ring, triangles, 100);
    }

    [Test]
    public void AHoleTouchingTheOuterRing_IsLeftOut()
    {
        Vector2<double>[] outer = [new(0, 0), new(10, 0), new(10, 10), new(0, 10)];
        Vector2<double>[] hole = [new(10, 5), new(5, 3), new(5, 7)];
        var points = outer.Concat(hole).ToArray();

        var triangles = Cut(points, outer.Length);

        AssertCovers(points, triangles, 100 - 10);
    }

    [Test]
    public void ABowTie_EndsInTriangles()
    {
        Vector2<double>[] bowTie = [new(0, 0), new(10, 10), new(10, 0), new(0, 2)];
        var triangles = new List<int>();

        var clean = EarClipping.Triangulate(bowTie, [], triangles);

        Assert.That(clean, Is.False, "rings that cross are said to be no polygon");
        Assert.That(triangles.Count % 3, Is.Zero);
        Assert.That(triangles, Has.All.InRange(0, bowTie.Length - 1));
    }

    [Test]
    public void RandomRings_EndInTriangles()
    {
        var random = new Random(11);
        for (var run = 0; run < 2000; run++)
        {
            var points = new Vector2<double>[random.Next(0, 40)];
            for (var i = 0; i < points.Length; i++)
            {
                points[i] = new Vector2<double>(random.Next(0, 8), random.Next(0, 8));
            }

            var holes = points.Length > 6 ? new[] { random.Next(3, points.Length) } : [];
            var triangles = Cut(points, holes);

            Assert.That(triangles.Count % 3, Is.Zero);
            Assert.That(triangles, Has.All.InRange(0, Math.Max(0, points.Length - 1)));
        }
    }

    [Test]
    public void FewerThanThreePoints_AreNoTriangles()
    {
        Assert.Multiple(() =>
        {
            Assert.That(Cut([]), Is.Empty);
            Assert.That(Cut([new Vector2<double>(1, 1), new(2, 2)]), Is.Empty);
            Assert.That(Cut([new Vector2<double>(0, 0), new(1, 1), new(2, 2)]), Is.Empty, "in a line");
        });
    }
}
