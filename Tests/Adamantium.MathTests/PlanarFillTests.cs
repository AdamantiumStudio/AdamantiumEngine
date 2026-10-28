using System;
using System.Collections.Generic;
using Adamantium.Mathematics;
using Adamantium.Mathematics.Triangulation;
using NUnit.Framework;

namespace Adamantium.MathTests
{
    /// <summary>THE FILL RULE ON CONTOURS THAT CROSS. Nesting is the easy half - a ring inside a ring - and it is
    /// resolved by containment. This is the other half: contours that genuinely overlap, where the answer is not about
    /// any one contour but about each REGION the crossings cut the plane into.</summary>
    [TestFixture]
    public class PlanarFillTests
    {
        private static Vector2<double> V(double x, double y) => new(x, y);

        private static Vector2<double>[] Rect(double x0, double y0, double x1, double y1) =>
            new[] { V(x0, y0), V(x1, y0), V(x1, y1), V(x0, y1) };

        private static Vector2<double>[] RectCW(double x0, double y0, double x1, double y1) =>
            new[] { V(x0, y0), V(x0, y1), V(x1, y1), V(x1, y0) };

        private static bool Covered(List<Vector3<double>> triangles, Vector2<double> point)
        {
            for (var i = 0; i + 2 < triangles.Count; i += 3)
            {
                var a = V(triangles[i].X, triangles[i].Y);
                var b = V(triangles[i + 1].X, triangles[i + 1].Y);
                var c = V(triangles[i + 2].X, triangles[i + 2].Y);

                var d1 = (point.X - b.X) * (a.Y - b.Y) - (a.X - b.X) * (point.Y - b.Y);
                var d2 = (point.X - c.X) * (b.Y - c.Y) - (b.X - c.X) * (point.Y - c.Y);
                var d3 = (point.X - a.X) * (c.Y - a.Y) - (c.X - a.X) * (point.Y - a.Y);

                if (!((d1 < 0 || d2 < 0 || d3 < 0) && (d1 > 0 || d2 > 0 || d3 > 0))) return true;
            }

            return false;
        }

        // TWO SQUARES THAT OVERLAP. Non-zero fills their union; even-odd hollows out the part they share.
        [Test]
        public void OverlapIsUnionUnderNonZero_AndSymmetricDifferenceUnderEvenOdd()
        {
            var rings = new List<Vector2<double>[]> { Rect(0, 0, 60, 60), Rect(40, 40, 100, 100) };

            var union = PlanarFill.Fill(rings, FillRule.NonZero);
            var xor = PlanarFill.Fill(rings, FillRule.EvenOdd);

            Assert.Multiple(() =>
            {
                Assert.That(Covered(union, V(10, 10)), Is.True, "non-zero: the first square");
                Assert.That(Covered(union, V(90, 90)), Is.True, "non-zero: the second square");
                Assert.That(Covered(union, V(50, 50)), Is.True, "non-zero: what they share");

                Assert.That(Covered(xor, V(10, 10)), Is.True, "even-odd: the first square");
                Assert.That(Covered(xor, V(90, 90)), Is.True, "even-odd: the second square");
                Assert.That(Covered(xor, V(50, 50)), Is.False, "even-odd: what they share is a hole");
            });
        }

        // A RING: the inner contour runs the other way round, which is how an exported drawing cuts a hole.
        [Test]
        public void AContraryInnerRingIsAHoleUnderNonZero()
        {
            var rings = new List<Vector2<double>[]> { Rect(0, 0, 100, 100), RectCW(25, 25, 75, 75) };
            var filled = PlanarFill.Fill(rings, FillRule.NonZero);

            Assert.Multiple(() =>
            {
                Assert.That(Covered(filled, V(10, 10)), Is.True, "the ring itself");
                Assert.That(Covered(filled, V(50, 50)), Is.False, "the hole");
            });
        }

        // ...and one that runs the SAME way does not cut anything: the windings add up.
        [Test]
        public void AnInnerRingTheSameWayRoundIsNotAHole()
        {
            var rings = new List<Vector2<double>[]> { Rect(0, 0, 100, 100), Rect(25, 25, 75, 75) };
            var filled = PlanarFill.Fill(rings, FillRule.NonZero);

            Assert.That(Covered(filled, V(50, 50)), Is.True);
        }

        // THE CASE THIS IS ALL FOR: a hole, and a third contour crossing the outline. Nothing about the hole changes
        // because something overlaps somewhere else - which is exactly what used to happen.
        [Test]
        public void AHoleSurvivesAContourThatCrossesTheOutline()
        {
            var rings = new List<Vector2<double>[]>
            {
                Rect(0, 0, 100, 100),
                RectCW(25, 25, 45, 45),
                new[] { V(90, 60), V(130, 60), V(90, 90) }
            };

            var filled = PlanarFill.Fill(rings, FillRule.NonZero);

            Assert.Multiple(() =>
            {
                Assert.That(Covered(filled, V(10, 10)), Is.True, "the body");
                Assert.That(Covered(filled, V(110, 65)), Is.True, "what sticks out");
                Assert.That(Covered(filled, V(35, 35)), Is.False, "the hole");
            });
        }

        // A SELF-CROSSING CONTOUR - the pentagram. Non-zero fills its middle, even-odd leaves it empty.
        [Test]
        public void AStarFillsItsMiddleUnderNonZeroOnly()
        {
            var star = new Vector2<double>[5];
            var outer = new Vector2<double>[5];

            for (var i = 0; i < 5; i++)
            {
                var a = 2 * Math.PI * i / 5 - Math.PI / 2;
                outer[i] = V(100 * Math.Cos(a), 100 * Math.Sin(a));
            }

            star[0] = outer[0];
            star[1] = outer[2];
            star[2] = outer[4];
            star[3] = outer[1];
            star[4] = outer[3];

            var rings = new List<Vector2<double>[]> { star };

            Assert.Multiple(() =>
            {
                Assert.That(Covered(PlanarFill.Fill(rings, FillRule.NonZero), V(0, 0)), Is.True, "non-zero fills it");
                Assert.That(Covered(PlanarFill.Fill(rings, FillRule.EvenOdd), V(0, 0)), Is.False, "even-odd hollows it");
            });
        }

        // NOTHING TO DO is not a failure: a single clean ring comes back as itself.
        [Test]
        public void OneCleanRingIsFilled()
        {
            var filled = PlanarFill.Fill(new List<Vector2<double>[]> { Rect(0, 0, 10, 10) }, FillRule.NonZero);

            Assert.That(Covered(filled, V(5, 5)), Is.True);
        }
    }
}
