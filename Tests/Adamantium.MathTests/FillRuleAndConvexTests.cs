using System;
using System.Collections.Generic;
using Adamantium.Mathematics;
using Adamantium.Mathematics.Triangulation;
using NUnit.Framework;

namespace Adamantium.MathTests
{
    /// <summary>
    /// Locks the triangulator's fill-rule behavior (truth table) and the convex fast-path,
    /// so perf work (convex fan, earcut later) can't silently change rendered results.
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
        public void ConcaveSimple_EarcutFillsCorrectly()
        {
            // L-shape: bottom bar [0,20]x[0,10] + right bar [10,20]x[10,20]; top-left is a notch.
            var l = new[] { V(0, 0), V(20, 0), V(20, 20), V(10, 20), V(10, 10), V(0, 10) };
            Assert.IsFalse(MathHelper.Polyline.IsConvex<double>(l), "L is concave");

            var tris = Fill(l, FillRule.EvenOdd);
            Assert.IsTrue(Covered(tris, V(5, 5)), "bottom bar filled");
            Assert.IsTrue(Covered(tris, V(15, 15)), "right bar filled");
            Assert.IsFalse(Covered(tris, V(5, 15)), "notch empty");
            Assert.AreEqual(4, tris.Count / 3, "L (6 verts) => n-2 = 4 triangles (earcut)");
        }

        [Test]
        public void MultiContour_HoleViaEarcut()
        {
            // Donut (outer 0..100, inner 25..75): clean nesting -> earcut-with-holes path.
            // EvenOdd: ring filled, center hole; quad + quad-hole earcuts to 8 triangles.
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
