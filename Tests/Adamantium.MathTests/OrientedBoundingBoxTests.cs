using System;
using System.Collections.Generic;
using Adamantium.Mathematics;
using NUnit.Framework;

namespace Adamantium.MathTests;

[TestFixture]
public class OrientedBoundingBoxTests
{
    [Test]
    public void Merged_TakesInBothBoxes()
    {
        var first = new OrientedBoundingBox<float>(Vector3F.Zero, Vector3F.One, QuaternionF.Identity);
        var second = new OrientedBoundingBox<float>(new Vector3F(4, 0, 0), Vector3F.One, QuaternionF.Identity);

        var merged = OrientedBoundingBox.CreateMerged(first, second);

        Assert.That(merged.Contains(first), Is.EqualTo(ContainmentType.Contains));
        Assert.That(merged.Contains(second), Is.EqualTo(ContainmentType.Contains));
    }

    [Test]
    public void BoxesTurnedAlike_ThatOverlap_Intersect()
    {
        var first = new OrientedBoundingBox<float>(Vector3F.Zero, Vector3F.One, QuaternionF.Identity);
        var second = new OrientedBoundingBox<float>(new Vector3F(1.5f, 0.5f, 0), Vector3F.One, QuaternionF.Identity);

        Assert.That(first.Intersects(second), Is.True);
        Assert.That(first.Contains(second), Is.EqualTo(ContainmentType.Intersects));
    }

    [Test]
    public void TurnedBoxes_AgreeWithSeparatingAxesOverTheirCorners()
    {
        var random = new Random(3);
        for (int i = 0; i < 2000; i++)
        {
            var first = RandomBox(random);
            var second = RandomBox(random);

            Assert.That(first.Intersects(second), Is.EqualTo(Overlap(first, second)), $"pair {i}: {first} and {second}");
        }
    }

    [Test]
    public void ARayAlongAFace_ThroughTheBox_Hits()
    {
        var box = new OrientedBoundingBox<float>(Vector3F.Zero, Vector3F.One, QuaternionF.Identity);
        var ray = new Ray<float>(new Vector3F(0.5f, 0.5f, -5), Vector3F.UnitZ);

        Assert.That(box.Intersects(ray, out float distance), Is.True);
        Assert.That(distance, Is.EqualTo(4f).Within(1e-5f));
    }

    [Test]
    public void ARayFromInside_HitsAtItsOrigin()
    {
        var box = new OrientedBoundingBox<float>(Vector3F.Zero, Vector3F.One, QuaternionF.Identity);
        var ray = new Ray<float>(new Vector3F(0.2f, 0.3f, 0.1f), Vector3F.Normalize(new Vector3F(1, 2, 3)));

        Assert.That(box.Intersects(ray, out float distance), Is.True);
        Assert.That(distance, Is.EqualTo(0f));
    }

    [Test]
    public void ASegmentThroughTheBox_WithBothEndsOutside_Intersects()
    {
        var box = new OrientedBoundingBox<float>(Vector3F.Zero, Vector3F.One, Quaternion.CreateFromAxisAngle<float>(Vector3F.UnitZ, 0.3f));
        var start = new Vector3F(-5, 0, 0);
        var end = new Vector3F(5, 0, 0);

        Assert.That(box.Contains(start, end), Is.EqualTo(ContainmentType.Intersects));
    }

    [Test]
    public void ASegmentBesideTheBox_IsDisjoint()
    {
        var box = new OrientedBoundingBox<float>(Vector3F.Zero, Vector3F.One, QuaternionF.Identity);
        var start = new Vector3F(-5, 3, 0);
        var end = new Vector3F(5, 3, 0);

        Assert.That(box.Contains(start, end), Is.EqualTo(ContainmentType.Disjoint));
    }

    [Test]
    public void AFrustum_FarAway_IsNotIntersected()
    {
        var box = new OrientedBoundingBox<float>(new Vector3F(0, 0, -50), Vector3F.One, QuaternionF.Identity);
        var frustum = new BoundingFrustum<float>(Matrix4x4.CreatePerspectiveFovY<float>((MathF.PI / 2), 1, 1, 100), true);

        Assert.That(box.Intersects(frustum), Is.False);
    }

    [Test]
    public void AFrustum_TheBoxHolds_IsContained()
    {
        var box = new OrientedBoundingBox<float>(new Vector3F(0, 0, 5), new Vector3F(20), QuaternionF.Identity);
        var frustum = new BoundingFrustum<float>(Matrix4x4.CreatePerspectiveFovY<float>((MathF.PI / 2), 1, 1, 10), true);

        Assert.That(box.Contains(frustum), Is.EqualTo(ContainmentType.Contains));
    }

    private static OrientedBoundingBox<float> RandomBox(Random random)
    {
        var center = new Vector3F(Next(random), Next(random), Next(random)) * 3;
        var half = new Vector3F(0.2f + (float)random.NextDouble(), 0.2f + (float)random.NextDouble(), 0.2f + (float)random.NextDouble());
        var axis = Vector3F.Normalize(new Vector3F(Next(random), Next(random), Next(random)) + new Vector3F(1e-3f));
        var turn = random.Next(4) == 0 ? QuaternionF.Identity : Quaternion.CreateFromAxisAngle<float>(axis, Next(random) * MathF.PI);
        return new OrientedBoundingBox<float>(center, half, turn);
    }

    private static float Next(Random random)
    {
        return (float)(random.NextDouble() * 2 - 1);
    }

    private static bool Overlap(OrientedBoundingBox<float> first, OrientedBoundingBox<float> second)
    {
        var a = Axes(first);
        var b = Axes(second);
        List<Vector3F> axes = [.. a, .. b];
        foreach (var u in a)
        {
            foreach (var v in b)
            {
                var cross = Vector3F.Cross(u, v);
                if (cross.LengthSquared() > 1e-8f)
                {
                    axes.Add(Vector3F.Normalize(cross));
                }
            }
        }

        var firstCorners = new Vector3F[OrientedBoundingBox<float>.CornerCount];
        var secondCorners = new Vector3F[OrientedBoundingBox<float>.CornerCount];
        first.GetCorners(firstCorners);
        second.GetCorners(secondCorners);
        foreach (var axis in axes)
        {
            Project(firstCorners, axis, out var firstMin, out var firstMax);
            Project(secondCorners, axis, out var secondMin, out var secondMax);
            if (firstMax < secondMin || secondMax < firstMin)
            {
                return false;
            }
        }

        return true;
    }

    private static Vector3F[] Axes(OrientedBoundingBox<float> box)
    {
        var rotation = Matrix4x4.CreateFromQuaternion<float>(box.Orientation);
        return [Vector3F.Normalize(rotation.Right), Vector3F.Normalize(rotation.Up), Vector3F.Normalize(rotation.Forward)];
    }

    private static void Project(Vector3F[] corners, Vector3F axis, out float min, out float max)
    {
        min = float.MaxValue;
        max = float.MinValue;
        foreach (var corner in corners)
        {
            var along = Vector3F.Dot(corner, axis);
            min = Math.Min(min, along);
            max = Math.Max(max, along);
        }
    }
}
