using System;
using Adamantium.Mathematics;
using NUnit.Framework;
using Vector3D = Adamantium.Mathematics.Vector3<double>;

namespace Adamantium.MathTests;

[TestFixture]
public class TypedOrientedBoxTests
{
    private const int Count = OrientedBoundingBox<double>.CornerCount;

    [Test]
    public void Corners_AreTheLocalBoxTurnedAndMoved_AlongTheTurnedAxes()
    {
        var turn = Quaternion.CreateFromAxisAngle(Vector3D.UnitZ, Math.PI / 2);
        var box = OrientedBoundingBox.Create(Vector3.Create(10.0, 0, 0), Vector3.Create(2.0, 1, 3), turn);
        Span<Vector3D> corners = stackalloc Vector3D[Count];
        box.GetCorners(corners);

        Assert.That(Vector3D.NearEqual(corners[1], Vector3.Create(10.0 - 1, 2, 3)), Is.True, $"{corners[1]}");
        Assert.That(Vector3D.NearEqual(corners[7], Vector3.Create(10.0 + 1, -2, -3)), Is.True, $"{corners[7]}");
        Assert.That(box.Contains(Vector3.Create(10.5, 1.9, 0)), Is.EqualTo(ContainmentType.Contains));
        Assert.That(box.Contains(Vector3.Create(11.5, 0, 0)), Is.EqualTo(ContainmentType.Disjoint));
        var bounds = box.GetBoundingBox();
        Assert.That(Vector3D.NearEqual(bounds.Minimum, Vector3.Create(9.0, -2, -3)) && Vector3D.NearEqual(bounds.Maximum, Vector3.Create(11.0, 2, 3)), Is.True);
        Assert.That(Vector3D.NearEqual(box.Support(Vector3.Create(1.0, 1, 1)), Vector3.Create(11.0, 2, 3)), Is.True);
    }

    [Test]
    public void ATurnAndAMove_CarryEveryCorner()
    {
        var random = new Random(31);
        Span<Vector3D> before = stackalloc Vector3D[Count];
        Span<Vector3D> after = stackalloc Vector3D[Count];
        for (var n = 0; n < 500; n++)
        {
            var box = RandomBox(random);
            var (rotation, translation, scale) = (RandomTurn(random), RandomPoint(random), random.NextDouble() * 4 - 2);
            box.GetCorners(before);

            box.Transform(rotation, translation).GetCorners(after);
            for (var i = 0; i < Count; i++)
            {
                Assert.That(Vector3D.NearEqual(after[i], Vector3D.Transform(before[i], rotation) + translation), Is.True, $"corner {i}");
            }

            box.Transform(scale, rotation, translation).GetCorners(after);
            Assert.That(SameCorners(after, before, point => Vector3D.Transform(point * scale, rotation) + translation), Is.True, "scaled");
        }
    }

    [Test]
    public void AMatrix_KeepsTheBoxExactWhenItKeepsItSquare_AndHoldsItWhenItShears()
    {
        var random = new Random(32);
        Span<Vector3D> before = stackalloc Vector3D[Count];
        Span<Vector3D> after = stackalloc Vector3D[Count];
        for (var n = 0; n < 500; n++)
        {
            var box = RandomBox(random);
            box.GetCorners(before);
            var rigid = Matrix4x4.CreateFromQuaternion(RandomTurn(random)) * Matrix4x4.CreateTranslation(RandomPoint(random));
            var square = Matrix4x4.CreateFromQuaternion(Quaternion<double>.Conjugate(box.Orientation))
                         * Matrix4x4.CreateScale(Vector3.Create(0.5 + random.NextDouble(), 2.0, 3.0))
                         * Matrix4x4.CreateFromQuaternion(box.Orientation) * rigid;
            foreach (var matrix in new[] { rigid, square, Matrix4x4.CreateScale(-1.0, 1, 1) * rigid })
            {
                box.Transform(matrix).GetCorners(after);
                Assert.That(SameCorners(after, before, point => Vector3D.TransformCoordinate(point, matrix)), Is.True, $"{matrix}");
            }

            var shear = new Matrix4x4<double>(Row(random), Row(random), Row(random), new Vector4<double>(RandomPoint(random), 1));
            var carried = box.Transform(shear);
            var widened = new OrientedBoundingBox<double>(carried.Center, carried.HalfExtent * (1 + 1e-9) + new Vector3D(1e-9), carried.Orientation);
            for (var i = 0; i < Count; i++)
            {
                Assert.That(widened.Contains(Vector3D.TransformCoordinate(before[i], shear)), Is.EqualTo(ContainmentType.Contains), $"corner {i}");
            }

            var stretched = box.Transform(Vector3.Create(2.0, 0.5, 3), RandomTurn(random), RandomPoint(random));
            Assert.That(stretched.HalfExtent.X >= 0 && stretched.HalfExtent.Y >= 0 && stretched.HalfExtent.Z >= 0, Is.True);
        }
    }

    [Test]
    public void TwoBoxes_MeetAsGjkFindsThem_AndOneHoldsTheOtherAsItsCornersSay()
    {
        var random = new Random(33);
        Span<Vector3D> corners = stackalloc Vector3D[Count];
        var (met, apart, compared) = (0, 0, 0);
        for (var n = 0; n < 4000; n++)
        {
            var (first, second) = (RandomBox(random, 0.4), RandomBox(random, 0.4));
            var verdict = first.Contains(second);
            var grown = Gjk.Intersects(Scaled(first, 1 + 1e-3).As<float>(), Scaled(second, 1 + 1e-3).As<float>());
            var shrunk = Gjk.Intersects(Scaled(first, 1 - 1e-3).As<float>(), Scaled(second, 1 - 1e-3).As<float>());
            if (grown == shrunk)
            {
                compared++;
                Assert.That(verdict != ContainmentType.Disjoint, Is.EqualTo(grown), $"{first} / {second}");
                _ = grown ? met++ : apart++;
            }

            second.GetCorners(corners);
            var inside = 0;
            foreach (var corner in corners)
            {
                inside += Scaled(first, 1 + 1e-9).Contains(corner) == ContainmentType.Contains ? 1 : 0;
            }

            if (verdict == ContainmentType.Contains)
            {
                Assert.That(inside, Is.EqualTo(Count));
            }
            else if (inside == Count && Scaled(first, 1 - 1e-9).Contains(second) != ContainmentType.Contains)
            {
                continue;
            }
            else
            {
                Assert.That(inside, Is.LessThan(Count), $"{first} / {second}");
            }
        }

        Assert.That(met > 500 && apart > 500 && compared > 3500, Is.True, $"{met} met, {apart} apart, {compared} compared");
    }

    [Test]
    public void ASegment_IsInsideCrossingOrOutside_AsPointsAlongItAre()
    {
        var random = new Random(34);
        for (var n = 0; n < 3000; n++)
        {
            var box = RandomBox(random);
            var (start, end) = (RandomPoint(random) * 1.5, RandomPoint(random) * 1.5);
            var inside = 0;
            for (var k = 0; k <= 2000; k++)
            {
                inside += box.Contains(Vector3D.Lerp(start, end, k / 2000.0)) == ContainmentType.Contains ? 1 : 0;
            }

            var verdict = box.Contains(start, end);
            if (inside == 2001)
            {
                Assert.That(verdict, Is.EqualTo(ContainmentType.Contains));
            }
            else if (inside > 0)
            {
                Assert.That(verdict, Is.EqualTo(ContainmentType.Intersects));
            }
            else if (verdict != ContainmentType.Disjoint)
            {
                var grazing = Scaled(box, 1 - 1e-3).Contains(start, end);
                Assert.That(grazing, Is.EqualTo(ContainmentType.Disjoint), $"{box}: {start} - {end}");
            }
        }
    }

    [Test]
    public void ARay_MeetsTheTurnedBox_AtItsSurface()
    {
        var random = new Random(35);
        var hits = 0;
        for (var n = 0; n < 3000; n++)
        {
            var box = RandomBox(random);
            var start = RandomPoint(random) * 3;
            var ray = Ray.Create(start, box.Center + RandomPoint(random) * 0.5 - start);
            if (!box.Intersects(ray, out double distance))
            {
                Assert.That(Scaled(box, 1 - 1e-6).Intersects(ray), Is.False);
                continue;
            }

            hits++;
            Assert.That(box.Intersects(ray, out Vector3D point) && Vector3D.NearEqual(point, ray.Position + ray.Direction * distance), Is.True);
            Assert.That(Scaled(box, 1 + 1e-9).Contains(point), Is.EqualTo(ContainmentType.Contains));
            if (distance > 1e-6)
            {
                Assert.That(Scaled(box, 1 - 1e-9).Contains(ray.Position + ray.Direction * (distance * (1 - 1e-6))), Is.EqualTo(ContainmentType.Disjoint));
            }
        }

        Assert.That(hits, Is.GreaterThan(300));
    }

    [Test]
    public void APlane_ASphere_AndAFrustum_GoByTheTurnedBox()
    {
        var turn = Quaternion.CreateFromAxisAngle(Vector3D.UnitZ, Math.PI / 4);
        var box = OrientedBoundingBox.Create(Vector3D.Zero, Vector3.Create(1.0, 1, 1), turn);
        var reach = Math.Sqrt(2);

        Assert.That(box.Intersects(Plane.Create(Vector3D.UnitX, -reach * 0.99)), Is.EqualTo(PlaneIntersectionType.Intersecting));
        Assert.That(box.Intersects(Plane.Create(Vector3D.UnitX, -reach * 1.01)), Is.EqualTo(PlaneIntersectionType.Back));
        Assert.That(box.Contains(BoundingSphere.Create(Vector3.Create(reach * 1.2, 0, 0), 0.3)), Is.EqualTo(ContainmentType.Intersects));
        Assert.That(box.Contains(BoundingSphere.Create(Vector3.Create(reach * 1.2, 0, 0), 0.2)), Is.EqualTo(ContainmentType.Disjoint));
        Assert.That(BoundingSphere.Create(Vector3D.Zero, Math.Sqrt(3) * 1.001).Contains(box), Is.EqualTo(ContainmentType.Contains));
        Assert.That(BoundingBox.Create(Vector3.Create(-reach * 1.01, -reach * 1.01, -1.01), Vector3.Create(reach * 1.01, reach * 1.01, 1.01)).Contains(box),
            Is.EqualTo(ContainmentType.Contains));

        var camera = BoundingFrustum.Create(Matrix4x4.CreatePerspectiveFovY(Math.PI / 2, 1, 1.0, 100), true);
        var ahead = OrientedBoundingBox.Create(Vector3.Create(0.0, 0, 50), Vector3.Create(1.0, 1, 1), turn);
        var beside = OrientedBoundingBox.Create(Vector3.Create(70.0, 0, 50), Vector3.Create(5.0, 5, 5), turn);
        var around = OrientedBoundingBox.Create(Vector3.Create(0.0, 0, 50), Vector3.Create(200.0, 200, 200), turn);
        Assert.That(camera.Contains(ahead), Is.EqualTo(ContainmentType.Contains));
        Assert.That(camera.Contains(beside), Is.EqualTo(ContainmentType.Disjoint));
        Assert.That(around.Contains(camera), Is.EqualTo(ContainmentType.Contains));
        Assert.That(ahead.Contains(camera), Is.EqualTo(ContainmentType.Intersects));
        Assert.That(beside.Intersects(camera), Is.False);
    }

    [Test]
    public void ItsFrustum_HasItsCorners()
    {
        var random = new Random(36);
        Span<Vector3D> corners = stackalloc Vector3D[Count];
        Span<Vector3D> frustumCorners = stackalloc Vector3D[Count];
        for (var n = 0; n < 200; n++)
        {
            var box = RandomBox(random);
            box.GetCorners(corners);
            box.ToFrustum().GetCorners(frustumCorners);
            Assert.That(SameCorners(frustumCorners, corners, point => point), Is.True, $"{box}");
        }
    }

    [Test]
    public void AnInsideOutBox_AndANaN_HoldNothing()
    {
        var inverted = OrientedBoundingBox.CreateFromBox(BoundingBox.Create(Vector3.Create(1.0, 1, 1), Vector3.Create(-1.0, -1, -1)));
        var unit = OrientedBoundingBox.CreateFromBox(BoundingBox.Create(Vector3.Create(-1.0, -1, -1), Vector3.Create(1.0, 1, 1)));
        var nan = OrientedBoundingBox.Create(Vector3D.Zero, Vector3.Create(1.0, double.NaN, 1), Quaternion<double>.Identity);

        Assert.That(inverted.Contains(Vector3D.Zero), Is.EqualTo(ContainmentType.Disjoint));
        Assert.That(unit.Intersects(inverted), Is.False);
        Assert.That(inverted.Intersects(BoundingSphere.Create(Vector3D.Zero, 5.0)), Is.False);
        Assert.That(nan.Contains(Vector3D.Zero), Is.EqualTo(ContainmentType.Disjoint));
        Assert.That(unit.Contains(Vector3.Create(double.NaN, 0, 0)), Is.EqualTo(ContainmentType.Disjoint));
        Assert.That(unit.Intersects(Ray.Create(Vector3.Create(double.NaN, 0, 0), Vector3D.UnitX)), Is.False);
    }

    [Test]
    public void HugeAndEndlessBoxes_HoldWhatTheySpan()
    {
        var turn = Quaternion.CreateFromAxisAngle(Vector3.Create(1f, 1, 0) / MathF.Sqrt(2), 0.7f);
        var unit = OrientedBoundingBox.Create(Vector3<float>.Zero, Vector3.Create(1f, 1, 1), turn);
        var world = BoundingBox.Create(new Vector3<float>(-float.MaxValue), new Vector3<float>(float.MaxValue));
        var endless = BoundingBox.Create(new Vector3<double>(double.NegativeInfinity), new Vector3<double>(double.PositiveInfinity));
        var half = BoundingBox.Create(new Vector3<Half>((Half)(-40000)), new Vector3<Half>((Half)40000));

        Assert.That(world.Contains(unit), Is.EqualTo(ContainmentType.Contains));
        Assert.That(unit.Intersects(world), Is.True);
        Assert.That(endless.Contains(unit.As<double>()), Is.EqualTo(ContainmentType.Contains));
        Assert.That(OrientedBoundingBox.CreateFromBox(endless).Center, Is.EqualTo(Vector3D.Zero));
        Assert.That(half.Contains(unit.As<Half>()), Is.EqualTo(ContainmentType.Contains));
    }

    [Test]
    public void ABoxThatHoldsNothing_StaysSo_ThroughEveryTransformAndMerge()
    {
        var empty = OrientedBoundingBox.Create(Vector3D.Zero, Vector3.Create(-1.0, 1, 1), Quaternion<double>.Identity);
        var box = OrientedBoundingBox.Create(Vector3.Create(5.0, 0, 0), Vector3.Create(1.0, 1, 1), Quaternion<double>.Identity);
        var turn = Quaternion.CreateFromAxisAngle(Vector3D.UnitY, 0.4);

        Assert.That(empty.Transform(Matrix4x4<double>.Identity).Contains(Vector3D.Zero), Is.EqualTo(ContainmentType.Disjoint));
        Assert.That(empty.Transform(Vector3.Create(2.0, 1, 1), turn, Vector3D.UnitX).Contains(Vector3D.UnitX), Is.EqualTo(ContainmentType.Disjoint));
        Assert.That(empty.Transform(2.0, turn, Vector3D.UnitX).Contains(Vector3D.UnitX), Is.EqualTo(ContainmentType.Disjoint));
        Assert.That(empty.ToFrustum().Contains(Vector3D.Zero), Is.EqualTo(ContainmentType.Disjoint));
        Assert.That(OrientedBoundingBox.CreateMerged(empty, box), Is.EqualTo(box));
        Assert.That(OrientedBoundingBox.CreateMerged(box, empty), Is.EqualTo(box));
        Assert.That(empty.Intersects(Plane.Create(Vector3D.UnitX, 0.5)), Is.EqualTo(PlaneIntersectionType.Front));
    }

    [Test]
    public void ATurnedBox_HoldsItself()
    {
        var turn = Quaternion<double>.Normalize(new Quaternion<double>(0.3, -0.5, 0.2, 0.7));
        var box = OrientedBoundingBox.Create(Vector3.Create(1.3, -2.1, 0.7), Vector3.Create(1.7, 0.9, 2.3), turn);

        Assert.That(box.Contains(box), Is.EqualTo(ContainmentType.Contains));
        Assert.That(box.As<float>().Contains(box.As<float>()), Is.EqualTo(ContainmentType.Contains));
        Assert.That(box.As<Half>().Contains(box.As<Half>()), Is.EqualTo(ContainmentType.Contains));
    }

    [Test]
    public void AThinSlab_AcrossAFrustum_Crosses()
    {
        var camera = BoundingFrustum.Create(Matrix4x4.CreatePerspectiveFovY(MathF.PI / 2, 1, 1f, 100), true);
        var slab = OrientedBoundingBox.Create(Vector3.Create(0f, 0, 50), Vector3.Create(200f, 200, 1e-39f), Quaternion<float>.Identity);
        var halfCamera = BoundingFrustum.Create(Matrix4x4.CreatePerspectiveFovY(Math.PI / 2, 1, 1.0, 100).As<Half>(), true);
        var halfSlab = OrientedBoundingBox.Create(Vector3.Create((Half)0, (Half)0, (Half)50), Vector3.Create((Half)200, (Half)200, (Half)5e-6),
            Quaternion<Half>.Identity);

        Assert.That(slab.Contains(camera), Is.EqualTo(ContainmentType.Intersects));
        Assert.That(camera.Contains(slab), Is.EqualTo(ContainmentType.Intersects));
        Assert.That(halfSlab.Contains(halfCamera), Is.EqualTo(ContainmentType.Intersects));
    }

    [Test]
    public void BoxesApartByAHair_AreApart_AndBoxesWithParallelEdgesThatMeet_Meet()
    {
        var cube = OrientedBoundingBox.Create(Vector3D.Zero, new Vector3D(1000), Quaternion<double>.Identity);
        var turned = OrientedBoundingBox.Create(Vector3.Create(1000 + 1000 * Math.Sqrt(2) + 0.001, 0, 0), new Vector3D(1000),
            Quaternion.CreateFromAxisAngle(Vector3D.UnitZ, Math.PI / 4));
        Assert.That(cube.Intersects(turned), Is.False);

        var random = new Random(38);
        for (var n = 0; n < 20000; n++)
        {
            var turn = RandomTurn(random);
            var nudge = Quaternion.CreateFromAxisAngle(Vector3D.UnitZ, (random.NextDouble() - 0.5) * 1e-7);
            var first = OrientedBoundingBox.Create(RandomPoint(random), Vector3.Create(0.5 + random.NextDouble(), 0.5, 0.5), turn);
            var second = OrientedBoundingBox.Create(first.Center + RandomPoint(random) * 0.2, Vector3.Create(0.3, 0.3 + random.NextDouble(), 0.3),
                nudge * turn);
            Assert.That(first.Intersects(second), Is.True, $"{first} / {second}");
            Assert.That(first.As<float>().Intersects(second.As<float>()), Is.True, $"float {first} / {second}");
            Assert.That(first.As<Half>().Intersects(second.As<Half>()), Is.True, $"Half {first} / {second}");
        }
    }

    [Test]
    public void FarAway_InFloat_ARayAnswersAsInDouble()
    {
        var random = new Random(37);
        var agreed = 0;
        for (var n = 0; n < 2000; n++)
        {
            var box = OrientedBoundingBox.Create(RandomPoint(random) * 1e30, Vector3D.Abs(RandomPoint(random)) * 1e30, RandomTurn(random)).As<float>();
            var ray = Ray.Create(RandomPoint(random) * 1e30, RandomPoint(random) * 1e30).As<float>();
            var single = box.Intersects(ray, out float distance);
            var exact = box.As<double>().Intersects(ray.As<double>(), out double exactDistance);
            if (Scaled(box.As<double>(), 1 + 1e-4).Intersects(ray.As<double>()) != Scaled(box.As<double>(), 1 - 1e-4).Intersects(ray.As<double>()))
            {
                continue;
            }

            agreed++;
            Assert.That(single, Is.EqualTo(exact), $"{box} / {ray}");
            if (exact)
            {
                Assert.That(Math.Abs(distance - exactDistance), Is.LessThan(1e-3 * Math.Max(1, exactDistance)));
            }
        }

        Assert.That(agreed, Is.GreaterThan(1500));
    }

    private static OrientedBoundingBox<T> Scaled<T>(OrientedBoundingBox<T> box, double scale) where T : unmanaged, System.Numerics.IFloatingPointIeee754<T> =>
        new(box.Center, box.HalfExtent * T.CreateTruncating(scale), box.Orientation);

    private static bool SameCorners(ReadOnlySpan<Vector3D> actual, ReadOnlySpan<Vector3D> original, Func<Vector3D, Vector3D> carry)
    {
        foreach (var point in original)
        {
            var expected = carry(point);
            var found = false;
            foreach (var corner in actual)
            {
                found |= (corner - expected).Length() <= 1e-9 * Math.Max(1, expected.Length());
            }

            if (!found)
            {
                return false;
            }
        }

        return true;
    }

    private static OrientedBoundingBox<double> RandomBox(Random random, double spread = 1) =>
        OrientedBoundingBox.Create(RandomPoint(random) * spread, Vector3.Create(0.1 + random.NextDouble(), 0.1 + random.NextDouble(), 0.1 + random.NextDouble()),
            RandomTurn(random));

    private static Quaternion<double> RandomTurn(Random random) =>
        Quaternion<double>.Normalize(new Quaternion<double>(random.NextDouble() - 0.5, random.NextDouble() - 0.5, random.NextDouble() - 0.5,
            random.NextDouble() - 0.5));

    private static Vector3D RandomPoint(Random random) =>
        Vector3.Create(random.NextDouble() * 4 - 2, random.NextDouble() * 4 - 2, random.NextDouble() * 4 - 2);

    private static Vector4<double> Row(Random random) =>
        new(random.NextDouble() * 4 - 2, random.NextDouble() * 4 - 2, random.NextDouble() * 4 - 2, 0);
}
