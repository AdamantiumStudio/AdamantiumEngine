using System;
using Adamantium.Mathematics;
using NUnit.Framework;

namespace Adamantium.MathTests;

[TestFixture]
public class GjkTests
{
    private const float Margin = 1e-3f;

    [Test]
    public void SpherePairs_AgreeWithTheDistanceBetweenCenters()
    {
        var random = new Random(11);
        var compared = 0;
        for (int i = 0; i < 3000; i++)
        {
            var first = new BoundingSphere<float>(RandomPoint(random, 3), 0.2f + (float)random.NextDouble());
            var second = new BoundingSphere<float>(RandomPoint(random, 3), 0.2f + (float)random.NextDouble());
            var gap = Vector3F.Distance(first.Center, second.Center) - first.Radius - second.Radius;
            if (Math.Abs(gap) < Margin)
            {
                continue;
            }

            compared++;
            Assert.That(Gjk.Intersects(first, second), Is.EqualTo(gap < 0), $"pair {i}: {first} and {second}");
        }

        Assert.That(compared, Is.GreaterThan(2500));
    }

    [Test]
    public void CapsulePairs_AgreeWithTheDistanceBetweenSegments()
    {
        var random = new Random(12);
        for (int i = 0; i < 3000; i++)
        {
            var first = RandomCapsule(random);
            var second = RandomCapsule(random);
            var gap = (float)Math.Sqrt(Collision.DistanceSquaredSegmentSegment(first.Start, first.End, second.Start, second.End))
                      - first.Radius - second.Radius;
            if (Math.Abs(gap) < Margin)
            {
                continue;
            }

            Assert.That(Gjk.Intersects(first, second), Is.EqualTo(gap < 0), $"pair {i}: {first} and {second}");
            Assert.That(first.Intersects(second), Is.EqualTo(gap < 0), $"pair {i}: {first} and {second}");
        }
    }

    [Test]
    public void CapsuleAndSphere_AgreeWithTheDistanceToTheSegment()
    {
        var random = new Random(13);
        for (int i = 0; i < 3000; i++)
        {
            var capsule = RandomCapsule(random);
            var sphere = new BoundingSphere<float>(RandomPoint(random, 3), 0.2f + (float)random.NextDouble());
            var closest = Collision.ClosestPointSegmentPoint(capsule.Start, capsule.End, sphere.Center);
            var gap = Vector3F.Distance(closest, sphere.Center) - capsule.Radius - sphere.Radius;
            if (Math.Abs(gap) < Margin)
            {
                continue;
            }

            Assert.That(Gjk.Intersects(capsule, sphere), Is.EqualTo(gap < 0), $"pair {i}: {capsule} and {sphere}");
        }
    }

    [Test]
    public void BoxPairs_AgreeWithSeparatingAxes()
    {
        var random = new Random(14);
        for (int i = 0; i < 3000; i++)
        {
            var first = RandomBox(random);
            var second = RandomBox(random);
            var shrunk = Scaled(first, 1 - Margin);
            var grown = Scaled(first, 1 + Margin);
            var expected = shrunk.Intersects(second);
            if (expected != grown.Intersects(second))
            {
                continue;
            }

            Assert.That(Gjk.Intersects(first, second), Is.EqualTo(expected), $"pair {i}: {first} and {second}");
        }
    }

    [Test]
    public void BoxAndSphere_AgreeWithTheClosestPointOfTheBox()
    {
        var random = new Random(15);
        for (int i = 0; i < 3000; i++)
        {
            var box = RandomBox(random);
            var center = RandomPoint(random, 3);
            var radius = 0.2f + (float)random.NextDouble();
            var shrunk = new BoundingSphere<float>(center, radius - Margin);
            var grown = new BoundingSphere<float>(center, radius + Margin);
            var expected = box.Intersects(shrunk);
            if (expected != box.Intersects(grown))
            {
                continue;
            }

            Assert.That(Gjk.Intersects(box, new BoundingSphere<float>(center, radius)), Is.EqualTo(expected), $"pair {i}");
        }
    }

    [Test]
    public void AShapeInsideAnother_Intersects()
    {
        var box = new OrientedBoundingBox<float>(Vector3F.Zero, new Vector3F(5), QuaternionF.Identity);
        var capsule = new BoundingCapsule(new Vector3F(-1, 0, 0), new Vector3F(1, 0, 0), 0.5f);

        Assert.That(Gjk.Intersects(box, capsule), Is.True);
        Assert.That(Gjk.Intersects(capsule, box), Is.True);
    }

    private static Vector3F RandomPoint(Random random, float spread)
    {
        return new Vector3F(Next(random), Next(random), Next(random)) * spread;
    }

    private static BoundingCapsule RandomCapsule(Random random)
    {
        var start = RandomPoint(random, 3);
        return new BoundingCapsule(start, start + RandomPoint(random, 2), 0.1f + (float)random.NextDouble() * 0.8f);
    }

    private static OrientedBoundingBox<float> RandomBox(Random random)
    {
        var half = new Vector3F(0.2f + (float)random.NextDouble(), 0.2f + (float)random.NextDouble(), 0.2f + (float)random.NextDouble());
        var axis = Vector3F.Normalize(RandomPoint(random, 1) + new Vector3F(1e-3f));
        var turn = random.Next(4) == 0 ? QuaternionF.Identity : Quaternion.CreateFromAxisAngle<float>(axis, Next(random) * (float)Math.PI);
        return new OrientedBoundingBox<float>(RandomPoint(random, 3), half, turn);
    }

    private static OrientedBoundingBox<float> Scaled(OrientedBoundingBox<float> box, float scale)
    {
        return new OrientedBoundingBox<float>(box.Center, box.HalfExtent * scale, box.Orientation);
    }

    private static float Next(Random random)
    {
        return (float)(random.NextDouble() * 2 - 1);
    }
}
