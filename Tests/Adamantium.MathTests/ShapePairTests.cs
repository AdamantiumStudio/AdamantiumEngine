using System;
using System.Linq;
using Adamantium.Mathematics;
using NUnit.Framework;

namespace Adamantium.MathTests;

[TestFixture]
public class ShapePairTests
{
    private const float Margin = 1e-3f;

    private static readonly string[] Kinds = ["box", "sphere", "oriented", "capsule", "hull"];

    [Test]
    public void EveryPair_Intersects_BothWays_AsGjkSays([ValueSource(nameof(Kinds))] string first, [ValueSource(nameof(Kinds))] string second)
    {
        var random = new Random(Array.IndexOf(Kinds, first) * Kinds.Length + Array.IndexOf(Kinds, second));
        var compared = 0;
        for (int i = 0; i < 400; i++)
        {
            var a = Make(first, random);
            var b = Make(second, random);
            var expected = Gjk.Intersects((IConvexShape<float>)Scaled(a, 1 - Margin), (IConvexShape<float>)b);
            if (expected != Gjk.Intersects((IConvexShape<float>)Scaled(a, 1 + Margin), (IConvexShape<float>)b))
            {
                continue;
            }

            compared++;
            Assert.That(Intersects(a, b), Is.EqualTo(expected), $"{first} with {second}, pair {i}");
            Assert.That(Intersects(b, a), Is.EqualTo(expected), $"{second} with {first}, pair {i}");
        }

        Assert.That(compared, Is.GreaterThan(300));
    }

    [Test]
    public void AFrustum_NeverLosesAShapeThatReachesIntoIt([ValueSource(nameof(Kinds))] string kind)
    {
        var random = new Random(100 + Array.IndexOf(Kinds, kind));
        var frustum = new BoundingFrustum<float>(Matrix4x4.CreatePerspectiveFovY<float>((MathF.PI / 2), 1, 1, 6), true);
        for (int i = 0; i < 400; i++)
        {
            var shape = Make(kind, random);
            var moved = Moved(shape, new Vector3F(0, 0, 3));
            if (Gjk.Intersects(frustum, (IConvexShape<float>)Scaled(moved, 1 - Margin)))
            {
                Assert.That(Reaches(frustum, moved), Is.True, $"{kind} {i}");
            }
        }
    }

    private static bool Intersects(object first, object second)
    {
        switch (first)
        {
            case BoundingBox<float> box:
                return second switch
                {
                    BoundingBox<float> other => box.Intersects(other),
                    BoundingSphere<float> sphere => box.Intersects(sphere),
                    OrientedBoundingBox<float> oriented => box.Intersects(oriented),
                    BoundingCapsule capsule => capsule.Intersects(box),
                    ConvexHull hull => hull.Intersects(box),
                    _ => throw new ArgumentException(nameof(second))
                };
            case BoundingSphere<float> sphere:
                return second switch
                {
                    BoundingBox<float> box => sphere.Intersects(box),
                    BoundingSphere<float> other => sphere.Intersects(other),
                    OrientedBoundingBox<float> oriented => sphere.Intersects(oriented),
                    BoundingCapsule capsule => capsule.Intersects(sphere),
                    ConvexHull hull => hull.Intersects(sphere),
                    _ => throw new ArgumentException(nameof(second))
                };
            case OrientedBoundingBox<float> oriented:
                return second switch
                {
                    BoundingBox<float> box => oriented.Intersects(box),
                    BoundingSphere<float> sphere => oriented.Intersects(sphere),
                    OrientedBoundingBox<float> other => oriented.Intersects(other),
                    BoundingCapsule capsule => capsule.Intersects(oriented),
                    ConvexHull hull => hull.Intersects(oriented),
                    _ => throw new ArgumentException(nameof(second))
                };
            case BoundingCapsule capsule:
                return second switch
                {
                    BoundingBox<float> box => capsule.Intersects(box),
                    BoundingSphere<float> sphere => capsule.Intersects(sphere),
                    OrientedBoundingBox<float> oriented => capsule.Intersects(oriented),
                    BoundingCapsule other => capsule.Intersects(other),
                    ConvexHull hull => capsule.Intersects(hull),
                    _ => throw new ArgumentException(nameof(second))
                };
            case ConvexHull hull:
                return second switch
                {
                    BoundingBox<float> box => hull.Intersects(box),
                    BoundingSphere<float> sphere => hull.Intersects(sphere),
                    OrientedBoundingBox<float> oriented => hull.Intersects(oriented),
                    BoundingCapsule capsule => hull.Intersects(capsule),
                    ConvexHull other => hull.Intersects(other),
                    _ => throw new ArgumentException(nameof(second))
                };
            default:
                throw new ArgumentException(nameof(first));
        }
    }

    private static bool Reaches(BoundingFrustum<float> frustum, object shape)
    {
        switch (shape)
        {
            case BoundingBox<float> box:
                return frustum.Intersects(box);
            case BoundingSphere<float> sphere:
                return frustum.Intersects(sphere);
            case OrientedBoundingBox<float> oriented:
                return frustum.Intersects(oriented);
            case BoundingCapsule capsule:
                return capsule.Within(frustum) != ContainmentType.Disjoint;
            case ConvexHull hull:
                return frustum.Contains([.. hull.Vertices]) != ContainmentType.Disjoint;
            default:
                throw new ArgumentException(nameof(shape));
        }
    }

    private static object Make(string kind, Random random)
    {
        var center = Point(random, 2.5f);
        switch (kind)
        {
            case "box":
                var half = Size(random);
                return new BoundingBox<float>(center - half, center + half);
            case "sphere":
                return new BoundingSphere<float>(center, 0.2f + (float)random.NextDouble());
            case "oriented":
                var axis = Vector3F.Normalize(Point(random, 1) + new Vector3F(1e-3f));
                return new OrientedBoundingBox<float>(center, Size(random), Quaternion.CreateFromAxisAngle<float>(axis, Next(random) * (float)Math.PI));
            case "capsule":
                var along = Point(random, 1);
                return new BoundingCapsule(center - along, center + along, 0.1f + (float)random.NextDouble() * 0.6f);
            default:
                var points = Enumerable.Range(0, 6 + random.Next(15)).Select(_ => center + Point(random, 1.2f)).ToArray();
                return ConvexHull.FromPoints(points);
        }
    }

    private static object Scaled(object shape, float factor)
    {
        switch (shape)
        {
            case BoundingBox<float> box:
                var center = (box.Minimum + box.Maximum) * 0.5f;
                var half = (box.Maximum - box.Minimum) * 0.5f * factor;
                return new BoundingBox<float>(center - half, center + half);
            case BoundingSphere<float> sphere:
                return new BoundingSphere<float>(sphere.Center, sphere.Radius * factor);
            case OrientedBoundingBox<float> oriented:
                return new OrientedBoundingBox<float>(oriented.Center, oriented.HalfExtent * factor, oriented.Orientation);
            case BoundingCapsule capsule:
                var middle = capsule.Center;
                return new BoundingCapsule(middle + (capsule.Start - middle) * factor, middle + (capsule.End - middle) * factor, capsule.Radius * factor);
            default:
                var hull = (ConvexHull)shape;
                var centroid = hull.Vertices.Aggregate(Vector3F.Zero, (sum, vertex) => sum + vertex) / hull.Vertices.Count;
                return hull.Transform(Matrix4x4.CreateTranslation<float>(-centroid) * Matrix4x4.CreateScale<float>(factor) * Matrix4x4.CreateTranslation<float>(centroid));
        }
    }

    private static object Moved(object shape, Vector3F offset)
    {
        switch (shape)
        {
            case BoundingBox<float> box:
                return new BoundingBox<float>(box.Minimum + offset, box.Maximum + offset);
            case BoundingSphere<float> sphere:
                return new BoundingSphere<float>(sphere.Center + offset, sphere.Radius);
            case OrientedBoundingBox<float> oriented:
                return new OrientedBoundingBox<float>(oriented.Center + offset, oriented.HalfExtent, oriented.Orientation);
            case BoundingCapsule capsule:
                return new BoundingCapsule(capsule.Start + offset, capsule.End + offset, capsule.Radius);
            default:
                return ((ConvexHull)shape).Transform(Matrix4x4.CreateTranslation<float>(offset));
        }
    }

    private static Vector3F Size(Random random)
    {
        return new Vector3F(0.2f + (float)random.NextDouble(), 0.2f + (float)random.NextDouble(), 0.2f + (float)random.NextDouble());
    }

    private static Vector3F Point(Random random, float spread)
    {
        return new Vector3F(Next(random), Next(random), Next(random)) * spread;
    }

    private static float Next(Random random)
    {
        return (float)(random.NextDouble() * 2 - 1);
    }
}
