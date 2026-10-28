using System;
using System.Collections.Generic;
using System.Linq;
using Adamantium.Mathematics;
using NUnit.Framework;
using Vector2D = Adamantium.Mathematics.Vector2<double>;
using Vector3D = Adamantium.Mathematics.Vector3<double>;

namespace Adamantium.MathTests;

[TestFixture]
public class TypedHelperTests
{
    [Test]
    public void Angles_ComeFromTheArctangent_SignedOrAFullTurn()
    {
        var (x, y) = (new Vector2D(1, 0), new Vector2D(0, 1));

        Assert.That(MathHelper.Angle.Between(x, y), Is.EqualTo(Math.PI / 2).Within(1e-15));
        Assert.That(MathHelper.Angle.Between(y, x), Is.EqualTo(-Math.PI / 2).Within(1e-15));
        Assert.That(MathHelper.Angle.Turn(y, x), Is.EqualTo(3 * Math.PI / 2).Within(1e-15));
        Assert.That(MathHelper.Angle.Turn(x, x), Is.Zero);
        Assert.That(MathHelper.Angle.Between(new Vector2D(-1, 0), new Vector2D(1, 0)), Is.EqualTo(Math.PI), "π, never -π");
        Assert.That(MathHelper.Angle.Between(new Vector2D(0, 1), new Vector2D(0, -1)), Is.EqualTo(Math.PI));
        Assert.That(double.IsNaN(MathHelper.Angle.Turn(new Vector2D(double.NaN, 0), x)), Is.True);
        Assert.That(MathHelper.Angle.Between(new Vector2D(1, 1e-9), new Vector2D(1, -1e-9)), Is.EqualTo(-2e-9).Within(1e-22),
            "the arccosine of the cosine would give 0 here");
        Assert.That(MathHelper.Angle.Between(new Vector3D(1, 0, 0), new Vector3D(0, 0, -3)), Is.EqualTo(Math.PI / 2).Within(1e-15));
        Assert.That(MathHelper.Angle.Between(new Vector3D(1, 0, 0), new Vector3D(0, 1, 0), new Vector3D(0, 0, 5)), Is.EqualTo(Math.PI / 2).Within(1e-15));
        Assert.That(MathHelper.Angle.Between(new Vector3D(1, 0, 0), new Vector3D(0, 1, 0), new Vector3D(0, 0, -5)), Is.EqualTo(-Math.PI / 2).Within(1e-15));
    }

    [Test]
    public void PolylineLength_IsItsLegs_AndResampleStepsEvenlyAlongIt()
    {
        var random = new Random(9);
        for (var i = 0; i < 200; i++)
        {
            var points = Enumerable.Range(0, random.Next(2, 30)).Select(_ => new Vector2D(random.NextDouble() * 100, random.NextDouble() * 100)).ToArray();
            var count = random.Next(2, 50);
            var resampled = new Vector2D[count];
            var legs = points.Zip(points.Skip(1)).Sum(p => Vector2D.Distance(p.First, p.Second));

            MathHelper.Polyline.Resample(points, resampled);

            Assert.That(MathHelper.Polyline.Length<double>(points), Is.EqualTo(legs).Within(1e-12 * legs));
            for (var k = 0; k < count; k++)
            {
                var along = PointAlong(points, legs * k / (count - 1));
                Assert.That(Vector2D.Distance(resampled[k], along), Is.LessThan(1e-9), $"case {i}, point {k}");
            }
        }
    }

    [Test]
    public void Resample_SpacesEvenly_AndAStillPolylineStaysPut()
    {
        Vector2D[] bent = [new(0, 0), new(1, 0), new(1, 3)];
        var resampled = new Vector2D[5];

        MathHelper.Polyline.Resample(bent, resampled);

        Assert.That(resampled, Is.EqualTo(new[] { new Vector2D(0, 0), new Vector2D(1, 0), new Vector2D(1, 1), new Vector2D(1, 2), new Vector2D(1, 3) }));
        MathHelper.Polyline.Resample([new Vector2D(2, 2), new Vector2D(2, 2)], resampled);
        Assert.That(resampled.All(p => p == new Vector2D(2, 2)), Is.True);
    }

    [Test]
    public void Convexity_TurnsOneWayOnce()
    {
        Vector2D[] square = [new(0, 0), new(1, 0), new(1, 1), new(0, 1)];
        Vector2D[] notch = [new(0, 0), new(2, 0), new(1, 0.5), new(2, 2), new(0, 2)];
        var star = Enumerable.Range(0, 5).Select(k => new Vector2D(Math.Cos(k * 4 * Math.PI / 5), Math.Sin(k * 4 * Math.PI / 5))).ToArray();
        Vector2D[] onASide = [new(0, 0), new(1, 0), new(2, 0), new(2, 2)];
        Vector2D[] flat = [new(0, 0), new(1, 0), new(2, 0)];

        Assert.That(MathHelper.Polyline.IsConvex<double>(square), Is.True);
        Assert.That(MathHelper.Polyline.IsConvex<double>(square.Reverse().ToArray()), Is.True);
        Assert.That(MathHelper.Polyline.IsConvex<double>(notch), Is.False);
        Assert.That(MathHelper.Polyline.IsConvex<double>(star), Is.False);
        Assert.That(MathHelper.Polyline.IsConvex<double>(onASide), Is.True);
        Assert.That(MathHelper.Polyline.IsConvex<double>(flat), Is.False, "the old took any three points as convex");
        Assert.That(MathHelper.Polyline.IsConvex<double>([new(0, 0), new(1, 0), new(1, 1), new(0, 1), new(0, 0)]), Is.True, "closed by repeating the first");
        Assert.That(MathHelper.Polyline.IsConvex<double>([new(0, 0), new(1, 0), new(1, 0), new(1, 1), new(0, 1)]), Is.True, "a corner doubled");
        Assert.That(MathHelper.Polyline.IsConvex<double>(
            [new(0, 0), new(0, 0), new(1, 0), new(1, 0), new(1, 1), new(1, 1), new(0, 1), new(0, 1), new(0, 0), new(1, 0), new(1, 1), new(0, 1)]),
            Is.False, "round twice");
        var circle = Enumerable.Range(0, 10000).Select(k => new Vector2<float>(100 * MathF.Cos(k * MathF.Tau / 10000), 100 * MathF.Sin(k * MathF.Tau / 10000))).ToArray();
        Assert.That(MathHelper.Polyline.IsConvex<float>(circle), Is.True, "float's sum of turns drifts");
        Assert.That(MathHelper.Polyline.IsClockwise(new Vector2D(0, 0), new Vector2D(1, 0), new Vector2D(0, 1)), Is.True);
        Assert.That(MathHelper.Polyline.DistanceToLine(new Vector2D(0, 0), new Vector2D(10, 0), new Vector2D(50, -3)), Is.EqualTo(3));
        Assert.That(MathHelper.Polyline.DistanceToLine(new Vector2D(1, 1), new Vector2D(1, 1), new Vector2D(4, 5)), Is.EqualTo(5));
    }

    [Test]
    public void Bezier_SamplesAtExactParameters_EndsExact()
    {
        var (start, c1, c2, end) = (new Vector2D(0, 0), new Vector2D(10, 30), new Vector2D(40, -20), new Vector2D(50, 10));
        var cubic = new List<Vector2D>();
        var general = new List<Vector2D>();
        var quadratic = new List<Vector2D>();
        var generalQuadratic = new List<Vector2D>();

        MathHelper.Curves.AppendCubicBezier(start, c1, c2, end, 256, cubic);
        MathHelper.Curves.AppendBezier<double>([start, c1, c2, end], 256, general);
        MathHelper.Curves.AppendQuadraticBezier(start, c1, end, 20, quadratic);
        MathHelper.Curves.AppendBezier<double>([start, c1, end], 20, generalQuadratic);

        Assert.That(cubic, Has.Count.EqualTo(257));
        Assert.That(cubic[0] == start && cubic[^1] == end, Is.True);
        for (var i = 0; i <= 256; i++)
        {
            var t = i / 256.0;
            var exact = Math.Pow(1 - t, 3) * start + 3 * t * (1 - t) * (1 - t) * c1 + 3 * t * t * (1 - t) * c2 + t * t * t * end;
            Assert.That(Vector2D.Distance(cubic[i], exact), Is.LessThan(1e-12));
            Assert.That(Vector2D.Distance(general[i], exact), Is.LessThan(1e-12));
        }

        Assert.That(quadratic, Has.Count.EqualTo(21));
        Assert.That(quadratic.Zip(generalQuadratic).All(p => Vector2D.Distance(p.First, p.Second) < 1e-12), Is.True);
    }

    [Test]
    public void QuadraticBSpline_IsTheUniformBSplineOfItsControls()
    {
        var random = new Random(4);
        foreach (var segments in new[] { 2, 4, 8, 256 })
        {
            var controls = Enumerable.Range(0, 7).Select(_ => new Vector2D(random.NextDouble() * 100, random.NextDouble() * 100)).ToArray();
            var knots = Enumerable.Range(0, controls.Length + 3).Select(i => (double)i).ToArray();
            var curve = new List<Vector2D>();

            MathHelper.Curves.AppendQuadraticBSpline<double>(controls, segments, curve);

            Assert.That(curve, Has.Count.EqualTo(5 * segments + 1));
            for (var k = 0; k < curve.Count; k++)
            {
                var expected = RationalPoint(controls, [], 2, knots, 2 + (double)k / segments);
                Assert.That(Vector2D.Distance(curve[k], expected), Is.LessThan(1e-12), $"{segments} segments, point {k}");
            }
        }
    }

    [Test]
    public void Nurbs_FollowsCoxDeBoor_AndReachesItsEnd([Values(true, false)] bool uniform)
    {
        var random = new Random(uniform ? 1 : 2);
        for (var degree = 1; degree <= 4; degree++)
        {
            var controls = Enumerable.Range(0, 8).Select(_ => new Vector2D(random.NextDouble() * 100, random.NextDouble() * 100)).ToArray();
            var weights = Enumerable.Range(0, 8).Select(_ => 0.5 + random.NextDouble() * 2).ToArray();
            var last = controls.Length + degree;
            var knots = Enumerable.Range(0, last + 1).Select(i =>
                uniform ? (double)i / last : i <= degree ? 0 : i >= last - degree ? 1 : (double)(i - degree) / (last - 2 * degree)).ToArray();
            var curve = new List<Vector2D>();

            MathHelper.Curves.AppendNurbs<double>(controls, weights, degree, uniform, 256, curve);

            var expected = 0;
            foreach (var point in curve)
            {
                while (expected <= 256 && Vector2D.Distance(point, RationalPoint(controls, weights, degree, knots, expected / 256.0)) >= 1e-9)
                {
                    expected++;
                }

                Assert.That(expected, Is.LessThanOrEqualTo(256), $"degree {degree}: {point} is not on the curve in order");
            }

            Assert.That(curve[0], Is.EqualTo(controls[0]));
            Assert.That(curve[^1], Is.EqualTo(controls[^1]));
            if (!uniform)
            {
                Assert.That(curve, Has.Count.EqualTo(257));
            }
        }
    }

    [Test]
    public void Arc_KeepsItsRadius_StepsEvenly_AndTurnsTheWayAsked([Values(true, false)] bool clockwise)
    {
        var (start, end) = (new Vector2D(0, 0), new Vector2D(10, 0));
        var arc = new List<Vector2D>();

        MathHelper.Curves.AppendArc(start, end, 10.0, clockwise, 0.05, arc);

        var center = new Vector2D(5, (clockwise ? 1 : -1) * Math.Sqrt(75));
        var sweep = 2 * Math.Asin(0.5);
        Assert.That(arc[0] == start && arc[^1] == end, Is.True);
        Assert.That(arc, Has.Count.EqualTo((int)Math.Ceiling(sweep / 0.05) + 1));
        Assert.That(arc.All(p => Math.Abs(Vector2D.Distance(p, center) - 10) < 1e-12), Is.True);
        for (var i = 1; i < arc.Count - 1; i++)
        {
            Assert.That(MathHelper.Angle.Between(arc[i - 1] - center, arc[i] - center), Is.EqualTo((clockwise ? 1 : -1) * 0.05).Within(1e-12));
        }

        var whole = new List<Vector2D>();
        MathHelper.Curves.AppendArc(start, end, 5.0, clockwise, Math.PI / 61, whole);
        Assert.That(whole, Has.Count.EqualTo(62), "a half circle in 61 steps, no sliver at the end");
        Assert.That(Vector2D.Distance(whole[^2], whole[^1]), Is.GreaterThan(0.2));

        var half = new List<Vector2D>();
        MathHelper.Curves.AppendArc(start, end, 1.0, clockwise, 0.05, half);
        Assert.That(half.All(p => Math.Abs(Vector2D.Distance(p, new Vector2D(5, 0)) - 5) < 1e-12), Is.True, "a radius too short is half the chord");
    }

    private static Vector2D PointAlong(Vector2D[] points, double distance)
    {
        for (var i = 1; i < points.Length; i++)
        {
            var leg = Vector2D.Distance(points[i - 1], points[i]);
            if (distance <= leg && leg > 0)
            {
                return Vector2D.Lerp(points[i - 1], points[i], distance / leg);
            }

            distance -= leg;
        }

        return points[^1];
    }

    private static Vector2D RationalPoint(Vector2D[] controls, double[] weights, int degree, double[] knots, double t)
    {
        if (t == knots[0])
        {
            return controls[0];
        }

        if (t == knots[^1])
        {
            return controls[^1];
        }

        var (sum, total) = (Vector2D.Zero, 0.0);
        for (var i = 0; i < controls.Length; i++)
        {
            var pull = CoxDeBoor(i, degree, knots, t) * (i < weights.Length ? weights[i] : 1);
            sum += pull * controls[i];
            total += pull;
        }

        return sum / total;
    }

    private static double CoxDeBoor(int i, int degree, double[] knots, double t)
    {
        if (degree == 0)
        {
            return knots[i] <= t && t < knots[i + 1] ? 1 : 0;
        }

        var left = knots[i + degree] > knots[i] ? (t - knots[i]) / (knots[i + degree] - knots[i]) * CoxDeBoor(i, degree - 1, knots, t) : 0;
        var right = knots[i + degree + 1] > knots[i + 1]
            ? (knots[i + degree + 1] - t) / (knots[i + degree + 1] - knots[i + 1]) * CoxDeBoor(i + 1, degree - 1, knots, t)
            : 0;
        return left + right;
    }
}
