using System.Collections.Generic;
using Adamantium.Mathematics;
using Adamantium.Mathematics.Triangulation;
using NUnit.Framework;

namespace Adamantium.MathTests
{
    /// <summary>
    /// Coverage-based triangulation tests. They assert robust invariants (the fill covers interior points and not
    /// exterior ones, and the triangulation is non-empty) instead of an implementation-defined exact triangle
    /// count. Precise fill-rule behavior (even-odd vs outer-contour, holes, convex/concave) is in
    /// <see cref="FillRuleAndConvexTests"/>. (Replaces the old brittle count/coordinate goldens.)
    /// </summary>
    public class TriangulationTests
    {
        [Test]
        public void Star_NonZero_FillsSolidBody()
        {
            // A 5-point star is a single self-intersecting contour -> the scanline fallback resolves it.
            // NonZero fills the whole star solid, so the center is covered.
            var tris = Fill(Star5(), FillRule.NonZero);
            Assert.Greater(tris.Count, 0, "non-empty triangulation");
            Assert.IsTrue(Covered(tris, new Vector2<double>(0, 13)), "star body filled");
            Assert.IsFalse(Covered(tris, new Vector2<double>(500, 500)), "far exterior not filled");
        }

        [Test]
        public void Star_EvenOdd_ProducesBoundedFill()
        {
            var tris = Fill(Star5(), FillRule.EvenOdd);
            Assert.Greater(tris.Count, 0, "non-empty triangulation");
            Assert.IsFalse(Covered(tris, new Vector2<double>(500, 500)), "far exterior not filled");
        }

        [Test]
        public void Bowtie_SelfIntersecting_BothLobesFilled()
        {
            // Figure-8 / bowtie: the two diagonals cross at (10,10), splitting it into a left and a right lobe.
            var bowtie = new[] { new Vector2<double>(0, 0), new Vector2<double>(20, 20), new Vector2<double>(20, 0), new Vector2<double>(0, 20) };
            foreach (var rule in new[] { FillRule.EvenOdd, FillRule.NonZero })
            {
                var tris = Fill(bowtie, rule);
                Assert.Greater(tris.Count, 0, $"non-empty ({rule})");
                Assert.IsTrue(Covered(tris, new Vector2<double>(4, 10)), $"left lobe filled ({rule})");
                Assert.IsTrue(Covered(tris, new Vector2<double>(16, 10)), $"right lobe filled ({rule})");
                Assert.IsFalse(Covered(tris, new Vector2<double>(500, 500)), $"exterior empty ({rule})");
            }
        }

        static Vector2<double>[] Star5() => new[]
        {
            new Vector2<double>(-10, 0), new Vector2<double>(0, 30), new Vector2<double>(10, 0),
            new Vector2<double>(-15, 15), new Vector2<double>(15, 15)
        };

        static List<Vector3<double>> Fill(Vector2<double>[] pts, FillRule rule)
        {
            var polygon = new Polygon { FillRule = rule };
            polygon.AddContour(new MeshContour(pts));
            return polygon.FillIndirect();
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
