using System;
using System.Collections.Generic;
using Adamantium.Mathematics;
using Adamantium.Mathematics.Triangulation;
using NUnit.Framework;

namespace Adamantium.MathTests
{
    /// <summary>
    /// Locks the triangulator's fill-rule behavior (truth table) and the convex fast-path,
    /// so perf work (convex fan, ear clipping later) can't silently change rendered results.
    /// </summary>
    public class FillRuleAndConvexTests
    {
        [Test]
        public void IsConvex_Classifies()
        {
            Assert.IsTrue(MathHelper.Polyline.IsConvex<double>(Rect(0, 0, 10, 10)), "square");
            Assert.IsTrue(MathHelper.Polyline.IsConvex<double>(new[] { V(0, 0), V(10, 0), V(5, 8) }), "triangle");
            Assert.IsTrue(MathHelper.Polyline.IsConvex<double>(Circle(32)), "circle");
            Assert.IsTrue(MathHelper.Polyline.IsConvex<double>(new[] { V(0, 0), V(5, 0), V(10, 0), V(10, 10), V(0, 10) }), "rect w/ collinear vertex");
            Assert.IsFalse(MathHelper.Polyline.IsConvex<double>(new[] { V(0, 0), V(10, 0), V(10, 10), V(5, 5), V(0, 10) }), "concave notch");
            Assert.IsFalse(MathHelper.Polyline.IsConvex<double>(Pentagram()), "pentagram (self-intersecting star)");
            Assert.IsFalse(MathHelper.Polyline.IsConvex<double>(new[] { V(0, 0), V(10, 0) }), "degenerate (2 pts)");
        }

        // EvenOdd = real even-odd. NonZero = real non-zero WINDING: a ring that runs the other way round cancels the
        // one enclosing it and becomes a hole; one running the same way does not.
        [Test]
        public void FillRule_TruthTable()
        {
            void Donut(bool oppWind, FillRule rule, bool centerFilled)
            {
                var p = new Polygon { FillRule = rule };
                p.AddContour(new MeshContour(Rect(0, 0, 100, 100)));
                p.AddContour(new MeshContour(oppWind ? RectCW(25, 25, 75, 75) : Rect(25, 25, 75, 75)));
                var tris = p.FillIndirect();
                Assert.AreEqual(centerFilled, Covered(tris, V(50, 50)), $"donut center rule={rule} opp={oppWind}");
                Assert.IsTrue(Covered(tris, V(10, 10)), $"donut ring rule={rule} opp={oppWind}");
            }

            Donut(false, FillRule.EvenOdd, false);
            Donut(true, FillRule.EvenOdd, false);
            Donut(false, FillRule.NonZero, true);          // same way round: the windings add up, so it stays solid
            Donut(true, FillRule.NonZero, false);          // the other way round: they cancel, which is the hole

            // Overlap of two same-wound squares: even-odd hollows it, NonZero fills it.
            void Overlap(FillRule rule, bool overlapFilled)
            {
                var p = new Polygon { FillRule = rule };
                p.AddContour(new MeshContour(Rect(0, 0, 60, 60)));
                p.AddContour(new MeshContour(Rect(40, 40, 100, 100)));
                var tris = p.FillIndirect();
                Assert.AreEqual(overlapFilled, Covered(tris, V(50, 50)), $"overlap rule={rule}");
                Assert.IsTrue(Covered(tris, V(10, 10)), $"A-only rule={rule}");
            }

            Overlap(FillRule.EvenOdd, false);
            Overlap(FillRule.NonZero, true);
        }

        [Test]
        public void Convex_FillsCorrectly_AndIsFan()
        {
            var sq = Fill(Rect(0, 0, 10, 10), FillRule.EvenOdd);
            Assert.IsTrue(Covered(sq, V(5, 5)), "square inside");
            Assert.IsFalse(Covered(sq, V(15, 5)), "square outside");
            Assert.AreEqual(2, sq.Count / 3, "square => n-2 = 2 triangles");

            var c = Fill(Circle(16), FillRule.EvenOdd);
            Assert.IsTrue(Covered(c, V(0, 0)), "circle center");
            Assert.IsFalse(Covered(c, V(200, 0)), "circle outside");
            Assert.AreEqual(14, c.Count / 3, "circle16 => n-2 = 14 triangles");
        }

        [Test]
        public void ConcaveSimple_EarClippingFillsCorrectly()
        {
            // L-shape: bottom bar [0,20]x[0,10] + right bar [10,20]x[10,20]; top-left is a notch.
            var l = new[] { V(0, 0), V(20, 0), V(20, 20), V(10, 20), V(10, 10), V(0, 10) };
            Assert.IsFalse(MathHelper.Polyline.IsConvex<double>(l), "L is concave");

            var tris = Fill(l, FillRule.EvenOdd);
            Assert.IsTrue(Covered(tris, V(5, 5)), "bottom bar filled");
            Assert.IsTrue(Covered(tris, V(15, 15)), "right bar filled");
            Assert.IsFalse(Covered(tris, V(5, 15)), "notch empty");
            Assert.AreEqual(4, tris.Count / 3, "L (6 verts) => n-2 = 4 triangles (ear clipping)");
        }

        [Test]
        public void MultiContour_HoleViaEarClipping()
        {
            // Donut (outer 0..100, inner 25..75): clean nesting -> ear clipping with holes path.
            // EvenOdd: ring filled, center hole; quad + quad-hole ear-clips to 8 triangles.
            var even = FillDonut(FillRule.EvenOdd);
            Assert.IsTrue(Covered(even, V(10, 10)), "ring filled");
            Assert.IsFalse(Covered(even, V(50, 50)), "center hole");
            Assert.AreEqual(8, even.Count / 3, "quad with quad hole => 8 triangles");

            // NonZero (drops inner contour today): solid; outer quad => 2 triangles.
            var nz = FillDonut(FillRule.NonZero);
            Assert.IsTrue(Covered(nz, V(50, 50)), "hole filled solid");
            Assert.AreEqual(2, nz.Count / 3, "solid quad => 2 triangles");
        }

        // A hole must survive a neighbor that crosses the outline: the fill rule applies to the faces of the cut-up contours.
        [Test]
        public void NonZero_KeepsHoles_WhenSomethingCrossesTheOutline()
        {
            var p = new Polygon { FillRule = FillRule.NonZero };

            p.AddContour(new MeshContour(Rect(0, 0, 100, 100)));          // the body
            p.AddContour(new MeshContour(RectCW(25, 25, 45, 45)));        // a cell, cut by running the other way
            p.AddContour(new MeshContour(new[] { V(90, 60), V(130, 60), V(90, 90) }));   // sticks out through the side

            var tris = p.FillIndirect();

            Assert.IsTrue(Covered(tris, V(10, 10)), "the body is not filled");
            Assert.IsTrue(Covered(tris, V(110, 65)), "what sticks out is not filled");
            Assert.IsFalse(Covered(tris, V(35, 35)), "the cell was filled in");
        }

        // A hole touching its outer ring at a point - at a corner of the ring, on the middle of its side, starting at the
        // point it touches - and holes touching each other: the ground between them filled, the holes empty, as the rule
        // says of every point.
        [TestCase(FillRule.EvenOdd)]
        [TestCase(FillRule.NonZero)]
        public void HolesTouchingAtAPoint_AreCutOut(FillRule rule)
        {
            var outer = Rect(0, 0, 100, 100);
            var cases = new (string Name, Vector2<double>[][] Holes)[]
            {
                ("at the outer ring's corner", new[] { new[] { V(0, 0), V(30, 60), V(60, 30) } }),
                ("on the middle of its side", new[] { new[] { V(50, 0), V(70, 40), V(30, 40) } }),
                ("starting at the point it touches", new[] { new[] { V(100, 50), V(60, 70), V(60, 30) } }),
                ("starting away from it", new[] { new[] { V(60, 70), V(60, 30), V(100, 50) } }),
                ("two holes touching each other", new[] { new[] { V(20, 20), V(50, 50), V(20, 50) }, new[] { V(50, 50), V(80, 80), V(80, 50) } }),
                ("a hole touching the ring twice", new[] { new[] { V(0, 50), V(50, 20), V(100, 50), V(50, 80) } }),
            };

            var wrong = new List<string>();
            foreach (var (name, holes) in cases)
            {
                var p = new Polygon { FillRule = rule };
                p.AddContour(new MeshContour(outer));
                // Under non-zero a hole is a ring running the other way round from the outer one.
                foreach (var hole in holes)
                    p.AddContour(new MeshContour(rule == FillRule.NonZero && SignedArea(hole) * SignedArea(outer) > 0 ? Reversed(hole) : hole));
                var tris = p.FillIndirect();
                var misses = 0;
                for (var y = 1.3; y < 100; y += 2.1)
                {
                    for (var x = 1.7; x < 100; x += 2.1)
                    {
                        var pt = V(x, y);
                        var inHole = false;
                        foreach (var hole in holes) inHole |= Inside(hole, pt);
                        if (NearAnEdge(outer, pt) || Array.Exists(holes, hole => NearAnEdge(hole, pt))) continue;
                        if (Covered(tris, pt) == inHole) misses++;
                    }
                }

                if (misses > 0) wrong.Add($"{name}: {misses} points");
            }

            Assert.That(wrong, Is.Empty);
        }

        // The face that came out of a combination with nothing filled: a ring and a hole beginning at the point they share.
        [Test]
        public void AHoleBeginningWhereItTouchesItsRing_IsCutOut()
        {
            var ring = new[] { V(200.0 / 3, 40), V(560.0 / 9, 80.0 / 3), V(80, 0), V(100, 100), V(40, 80), V(40, 60), V(160.0 / 3, 40) };
            var hole = new[] { V(200.0 / 3, 40), V(80, 80), V(80, 40) };
            var p = new Polygon { FillRule = FillRule.EvenOdd };
            p.AddContour(new MeshContour(ring));
            p.AddContour(new MeshContour(hole));
            var tris = p.FillIndirect();
            Assert.Multiple(() =>
            {
                Assert.That(tris, Is.Not.Empty);
                Assert.That(Covered(tris, V(85, 60)), Is.True, "the ring beside the hole");
                Assert.That(Covered(tris, V(77, 50)), Is.False, "the hole");
                Assert.That(Covered(tris, V(60, 75)), Is.True, "the ring away from it");
            });
        }

        static IEnumerable<TestCaseData> RingsToFill()
        {
            var square = Rect(0, 0, 100, 100);
            var corners = new[] { V(0, 0), V(100, 0), V(100, 100), V(0, 100) };
            for (var c = 0; c < corners.Length; c++)
            {
                var k = corners[c];
                var (sx, sy) = (k.X == 0 ? 1 : -1, k.Y == 0 ? 1 : -1);
                var hole = new[] { k, V(k.X + sx * 30, k.Y + sy * 60), V(k.X + sx * 60, k.Y + sy * 30) };
                foreach (var rule in new[] { FillRule.EvenOdd, FillRule.NonZero })
                    yield return new TestCaseData(rule, new[] { square, rule == FillRule.NonZero ? Opposite(square, hole) : hole })
                        .SetName($"A hole at corner {c} ({rule})");
            }

            foreach (var rule in new[] { FillRule.EvenOdd, FillRule.NonZero })
            {
                yield return new TestCaseData(rule, new[] { square, Rect(0, 0, 100, 100) }).SetName($"Coincident rings ({rule})");
                yield return new TestCaseData(rule, new[] { square, Reversed(Rect(0, 0, 100, 100)) })
                    .SetName($"Coincident rings running opposite ways ({rule})");
                // A triangle whose side runs through two corners of the square and nowhere else across it.
                yield return new TestCaseData(rule, new[] { square, new[] { V(-50, 150), V(150, -50), V(150, 150) } })
                    .SetName($"A ring crossing another only through its corners ({rule})");
                var cells = new List<Vector2<double>[]> { square };
                for (var y = 20; y < 80; y += 20)
                    for (var x = 20; x < 80; x += 20)
                        cells.Add(rule == FillRule.NonZero ? Reversed(Rect(x, y, x + 20, y + 20)) : Rect(x, y, x + 20, y + 20));
                yield return new TestCaseData(rule, cells.ToArray()).SetName($"Holes sharing sides in a grid ({rule})");
            }

            yield return new TestCaseData(FillRule.NonZero, new[] { square, Reversed(Rect(20, 20, 80, 80)), Rect(40, 40, 60, 60) })
                .SetName("An island in a hole (NonZero)");
            yield return new TestCaseData(FillRule.NonZero, new[] { square, Rect(10, 10, 90, 90), Reversed(Rect(20, 20, 80, 80)), Reversed(Rect(30, 30, 70, 70)) })
                .SetName("A hole two rings in (NonZero)");
            yield return new TestCaseData(FillRule.EvenOdd, new[] { square, Rect(20, 20, 80, 80), Rect(40, 40, 60, 60) })
                .SetName("An island in a hole (EvenOdd)");
            yield return new TestCaseData(FillRule.NonZero, new[] { square, Rect(20, 20, 80, 80), Reversed(Rect(40, 40, 60, 60)) })
                .SetName("A hole in a ring running the same way (NonZero)");
            yield return new TestCaseData(FillRule.NonZero, new[] { square, Reversed(Rect(20, 20, 80, 80)), Reversed(Rect(40, 40, 60, 60)) })
                .SetName("A ring in a hole running the same way as the hole (NonZero)");
            yield return new TestCaseData(FillRule.NonZero, new[] { square, Reversed(Rect(10, 10, 40, 40)), Rect(60, 60, 90, 90), Reversed(Rect(70, 70, 80, 80)) })
                .SetName("Rings side by side inside one (NonZero)");
        }

        // Rings that meet - at a corner, all along, through corners only - or nest, fill at every point as the rule says
        // of it.
        [TestCaseSource(nameof(RingsToFill))]
        public void Rings_FillAsTheRuleSays(FillRule rule, Vector2<double>[][] rings)
        {
            var p = new Polygon { FillRule = rule };
            foreach (var ring in rings) p.AddContour(new MeshContour(ring));
            var tris = p.FillIndirect();
            var wrong = new List<string>();
            for (var y = -49.3; y < 150; y += 2.1)
            {
                for (var x = -49.7; x < 150; x += 2.1)
                {
                    var pt = V(x, y);
                    if (Array.Exists(rings, ring => NearAnEdge(ring, pt))) continue;
                    var winding = 0;
                    foreach (var ring in rings) winding += Winding(ring, pt);
                    var filled = rule == FillRule.NonZero ? winding != 0 : (winding & 1) != 0;
                    if (Covered(tris, pt) != filled && wrong.Count < 5) wrong.Add($"({x:0.#}, {y:0.#}) should be {(filled ? "filled" : "empty")}");
                }
            }

            Assert.That(wrong, Is.Empty);
        }

        static Vector2<double>[] Opposite(Vector2<double>[] outer, Vector2<double>[] hole) =>
            SignedArea(hole) * SignedArea(outer) > 0 ? Reversed(hole) : hole;

        static int Winding(Vector2<double>[] ring, Vector2<double> p)
        {
            var winding = 0;
            for (int i = 0, j = ring.Length - 1; i < ring.Length; j = i++)
            {
                var (a, b) = (ring[j], ring[i]);
                var side = (b.X - a.X) * (p.Y - a.Y) - (p.X - a.X) * (b.Y - a.Y);
                if (a.Y <= p.Y && b.Y > p.Y && side > 0) winding++;
                else if (b.Y <= p.Y && a.Y > p.Y && side < 0) winding--;
            }

            return winding;
        }

        static double SignedArea(Vector2<double>[] ring)
        {
            var area = 0.0;
            for (int i = 0, j = ring.Length - 1; i < ring.Length; j = i++) area += ring[j].X * ring[i].Y - ring[i].X * ring[j].Y;
            return area / 2;
        }

        static Vector2<double>[] Reversed(Vector2<double>[] ring)
        {
            var copy = (Vector2<double>[])ring.Clone();
            Array.Reverse(copy);
            return copy;
        }

        static bool Inside(Vector2<double>[] ring, Vector2<double> p)
        {
            var inside = false;
            for (int i = 0, j = ring.Length - 1; i < ring.Length; j = i++)
                if ((ring[i].Y > p.Y) != (ring[j].Y > p.Y) &&
                    p.X < (ring[j].X - ring[i].X) * (p.Y - ring[i].Y) / (ring[j].Y - ring[i].Y) + ring[i].X)
                    inside = !inside;
            return inside;
        }

        static bool NearAnEdge(Vector2<double>[] ring, Vector2<double> p)
        {
            for (int i = 0, j = ring.Length - 1; i < ring.Length; j = i++)
            {
                var (a, d) = (ring[j], ring[i] - ring[j]);
                var t = Math.Clamp(((p.X - a.X) * d.X + (p.Y - a.Y) * d.Y) / (d.X * d.X + d.Y * d.Y), 0, 1);
                var (cx, cy) = (a.X + d.X * t - p.X, a.Y + d.Y * t - p.Y);
                if (cx * cx + cy * cy < 0.25) return true;
            }

            return false;
        }

        static List<Vector3<double>> FillDonut(FillRule rule)
        {
            var p = new Polygon { FillRule = rule };
            p.AddContour(new MeshContour(Rect(0, 0, 100, 100)));
            p.AddContour(new MeshContour(Rect(25, 25, 75, 75)));
            return p.FillIndirect();
        }

        // ---- helpers ----
        static Vector2<double> V(double x, double y) => new Vector2<double>(x, y);
        static Vector2<double>[] Rect(double x0, double y0, double x1, double y1) => new[] { V(x0, y0), V(x1, y0), V(x1, y1), V(x0, y1) };
        static Vector2<double>[] RectCW(double x0, double y0, double x1, double y1) => new[] { V(x0, y0), V(x0, y1), V(x1, y1), V(x1, y0) };

        static Vector2<double>[] Circle(int n, double r = 100)
        {
            var p = new Vector2<double>[n];
            for (int i = 0; i < n; i++) { double a = 2 * Math.PI * i / n; p[i] = V(r * Math.Cos(a), r * Math.Sin(a)); }
            return p;
        }

        static Vector2<double>[] Pentagram()
        {
            var o = Circle(5, 100);
            return new[] { o[0], o[2], o[4], o[1], o[3] };   // star {5/2}
        }

        static List<Vector3<double>> Fill(Vector2<double>[] pts, FillRule rule)
        {
            var p = new Polygon { FillRule = rule };
            p.AddContour(new MeshContour(pts));
            return p.FillIndirect();
        }

        static bool Covered(List<Vector3<double>> tris, Vector2<double> pt)
        {
            for (int i = 0; i + 2 < tris.Count; i += 3)
                if (InTri(pt, (Vector2<double>)tris[i], (Vector2<double>)tris[i + 1], (Vector2<double>)tris[i + 2])) return true;
            return false;
        }

        static bool InTri(Vector2<double> p, Vector2<double> a, Vector2<double> b, Vector2<double> c)
        {
            double d1 = Sgn(p, a, b), d2 = Sgn(p, b, c), d3 = Sgn(p, c, a);
            bool neg = d1 < 0 || d2 < 0 || d3 < 0, pos = d1 > 0 || d2 > 0 || d3 > 0;
            return !(neg && pos);
        }

        static double Sgn(Vector2<double> p, Vector2<double> a, Vector2<double> b) => (p.X - b.X) * (a.Y - b.Y) - (a.X - b.X) * (p.Y - b.Y);
    }
}
