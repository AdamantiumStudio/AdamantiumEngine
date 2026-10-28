using System;
using Adamantium.Mathematics;
using Adamantium.Mathematics.Typed;
using NUnit.Framework;
using BoundingBox = Adamantium.Mathematics.Typed.BoundingBox;
using BoundingSphere = Adamantium.Mathematics.Typed.BoundingSphere;
using Collision = Adamantium.Mathematics.Typed.Collision;
using Matrix4x4 = Adamantium.Mathematics.Typed.Matrix4x4;
using Plane = Adamantium.Mathematics.Typed.Plane;
using Ray = Adamantium.Mathematics.Typed.Ray;
using Vector3 = Adamantium.Mathematics.Typed.Vector3;
using Vector3D = Adamantium.Mathematics.Typed.Vector3<double>;

namespace Adamantium.MathTests;

[TestFixture]
public class TypedVolumeTests
{
    private static readonly BoundingBox<double> UnitBox = BoundingBox.Create(Vector3.Create(-1.0, -1.0, -1.0), Vector3.Create(1.0, 1.0, 1.0));

    [Test]
    public void RayFromOutside_HitsTheNearFace_FromInside_AtZero()
    {
        var outside = Ray.Create(Vector3.Create(-5.0, 0.0, 0.0), Vector3D.UnitX);
        var inside = Ray.Create(Vector3D.Zero, Vector3D.UnitY);
        var away = Ray.Create(Vector3.Create(-5.0, 0.0, 0.0), -Vector3D.UnitX);

        Assert.That(outside.Intersects(UnitBox, out double distance) && distance == 4, Is.True);
        Assert.That(inside.Intersects(UnitBox, out distance) && distance == 0, Is.True);
        Assert.That(away.Intersects(UnitBox), Is.False);
    }

    [Test]
    public void RayAndSphere_MeetAtTheSurface()
    {
        var sphere = BoundingSphere.Create(Vector3.Create(0.0, 0.0, 10.0), 2.0);

        Assert.That(Ray.Create(Vector3D.Zero, Vector3D.UnitZ).Intersects(sphere, out Vector3D point), Is.True);
        Assert.That(point, Is.EqualTo(Vector3.Create(0.0, 0.0, 8.0)));
        Assert.That(Ray.Create(Vector3D.Zero, -Vector3D.UnitZ).Intersects(sphere), Is.False);
    }

    [Test]
    public void PlaneFromVertices_FacesTheCounterclockwiseSide_AndMeasuresDistances()
    {
        var floor = Plane.CreateFromVertices(Vector3.Create(0.0, 2.0, 0.0), Vector3.Create(0.0, 2.0, 1.0), Vector3.Create(1.0, 2.0, 0.0));

        Assert.That(Vector3D.NearEqual(floor.Normal, Vector3D.UnitY), Is.True);
        Assert.That(Collision.DistancePlanePoint(floor, Vector3.Create(3.0, 5.0, -4.0)), Is.EqualTo(3.0).Within(1e-12));
        Assert.That(floor.Intersects(Vector3.Create(0.0, 1.0, 0.0)), Is.EqualTo(PlaneIntersectionType.Back));
        Assert.That(floor.Intersects(UnitBox), Is.EqualTo(PlaneIntersectionType.Back));
    }

    [Test]
    public void Reflection_MirrorsAcrossThePlane()
    {
        var mirror = Plane.CreateFromPointNormal(Vector3.Create(0.0, 0.0, 3.0), Vector3D.UnitZ);

        var reflected = Vector3D.TransformCoordinate(Vector3.Create(1.0, 2.0, 5.0), Matrix4x4.CreateReflection(mirror));

        Assert.That(Vector3D.NearEqual(reflected, Vector3.Create(1.0, 2.0, 1.0)), Is.True);
    }

    [Test]
    public void Shadow_FlattensPointsOntoThePlane_AlongTheLight()
    {
        var ground = Plane.CreateFromPointNormal(Vector3D.Zero, Vector3D.UnitY);
        var sunlight = new Vector4<double>(Vector3D.Normalize(Vector3.Create(1.0, 1.0, 0.0)), 0);

        var shadow = Vector3D.TransformCoordinate(Vector3.Create(0.0, 4.0, 0.0), Matrix4x4.CreateShadow(sunlight, ground));

        Assert.That(Vector3D.NearEqual(shadow, Vector3.Create(-4.0, 0.0, 0.0)), Is.True);
    }

    [Test]
    public void PlaneTransform_CarriesItsPoints()
    {
        var plane = Plane.CreateFromPointNormal(Vector3.Create(0.0, 1.0, 0.0), Vector3D.UnitY);
        var move = Matrix4x4.CreateRotationZ(0.5) * Matrix4x4.CreateTranslation(3.0, -2.0, 1.0);

        var carried = Plane<double>.Transform(plane, move);
        var point = Vector3D.TransformCoordinate(Vector3.Create(7.0, 1.0, -3.0), move);

        Assert.That(Collision.DistancePlanePoint(carried, point), Is.EqualTo(0).Within(1e-12));
    }

    [Test]
    public void TwoPlanes_MeetAlongALine()
    {
        var floor = Plane.CreateFromPointNormal(Vector3D.Zero, Vector3D.UnitY);
        var wall = Plane.CreateFromPointNormal(Vector3.Create(2.0, 0.0, 0.0), Vector3D.UnitX);

        Assert.That(floor.Intersects(wall, out Ray<double> line), Is.True);
        Assert.That(Collision.DistancePlanePoint(floor, line.Position), Is.EqualTo(0).Within(1e-12));
        Assert.That(Collision.DistancePlanePoint(wall, line.Position), Is.EqualTo(0).Within(1e-12));
        Assert.That(Math.Abs(line.Direction.Z), Is.EqualTo(1).Within(1e-12));
    }

    [Test]
    public void Spheres_MergeAroundBoth_AndTransformByTheLongestAxis()
    {
        var a = BoundingSphere.Create(Vector3.Create(-3.0, 0.0, 0.0), 1.0);
        var b = BoundingSphere.Create(Vector3.Create(4.0, 0.0, 0.0), 2.0);

        var merged = BoundingSphere.CreateMerged(a, b);
        var scaled = a.Transform(Matrix4x4.CreateScale(1.0, 3.0, 2.0) * Matrix4x4.CreateTranslation(1.0, 1.0, 1.0));

        Assert.That(merged.Contains(a), Is.EqualTo(ContainmentType.Contains));
        Assert.That(merged.Contains(b), Is.EqualTo(ContainmentType.Contains));
        Assert.That(merged.Radius, Is.EqualTo(5.0).Within(1e-12));
        Assert.That(scaled, Is.EqualTo(BoundingSphere.Create(Vector3.Create(-2.0, 1.0, 1.0), 3.0)));
    }

    [Test]
    public void BoxFromPoints_HoldsThem_AndItsCornersComeInOrder()
    {
        var box = BoundingBox.CreateFromPoints<double>([Vector3.Create(1.0, 5.0, -2.0), Vector3.Create(-3.0, 0.0, 4.0)]);
        Span<Vector3D> corners = stackalloc Vector3D[8];

        box.GetCorners(corners);

        Assert.That(box, Is.EqualTo(BoundingBox.Create(Vector3.Create(-3.0, 0.0, -2.0), Vector3.Create(1.0, 5.0, 4.0))));
        Assert.That(corners[0], Is.EqualTo(Vector3.Create(-3.0, 5.0, 4.0)));
        Assert.That(corners[7], Is.EqualTo(box.Minimum));
        Assert.That(BoundingSphere.CreateFromBox(box).Contains(box), Is.EqualTo(ContainmentType.Contains));
    }

    [Test]
    public void FarFromTheOrigin_DoubleBoxesStayExact()
    {
        var far = Vector3.Create(1e9, 2e9, -3e9);
        var box = BoundingBox.Create(far, far + Vector3.Create(0.001, 0.001, 0.001));

        Assert.That(box.Contains(far + Vector3.Create(0.0005, 0.0005, 0.0005)), Is.EqualTo(ContainmentType.Contains));
        Assert.That(box.As<float>().Size.X, Is.EqualTo(0f), "float cannot tell the corners apart there");
    }

    [Test]
    public void NearlyParallelSegments_MeasureTheirTrueGap()
    {
        var (start, end) = (Vector3D.Zero, Vector3.Create(1000.0, 0.0, 0.0));
        var (otherStart, otherEnd) = (Vector3.Create(0.0, 1.0, 0.0), Vector3.Create(1000.0, 0.7, 0.0));

        var gap = Collision.RayIntersectsLineSegment(Ray.Create(start, Vector3D.UnitX), otherStart, otherEnd, out var point);

        Assert.That(Collision.DistanceSquaredSegmentSegment(start, end, otherStart, otherEnd), Is.EqualTo(0.49).Within(1e-9));
        Assert.That(gap, Is.EqualTo(0.7).Within(1e-9));
        Assert.That(Vector3D.NearEqual(point, otherEnd), Is.True);
    }

    [Test]
    public void CrossingRays_Meet_InFloat_WhereverTheyCross([Values(4f, 1e-3f)] float spread)
    {
        var random = new Random(7);
        var misses = 0;
        for (var i = 0; i < 10000; i++)
        {
            var crossing = new Vector3<float>(Next(random, spread), Next(random, spread), Next(random, spread));
            var (start1, start2) = (new Vector3<float>(Next(random, 4), Next(random, 4), Next(random, 4)),
                new Vector3<float>(Next(random, 4), Next(random, 4), Next(random, 4)));
            var (ray1, ray2) = (Ray.Create(start1, crossing - start1), Ray.Create(start2, crossing - start2));
            if (Vector3<float>.Cross(ray1.Direction, ray2.Direction).Length() < 1e-3f * ray1.Direction.Length() * ray2.Direction.Length())
            {
                continue;
            }

            misses += Collision.RayIntersectsRay(ray1, ray2, out _) ? 0 : 1;
        }

        Assert.That(misses, Is.Zero);
    }

    [Test]
    public void RayAndSphere_AnswerAsExactArithmetic_ForAnyLengthOfDirection()
    {
        var random = new Random(11);
        for (var i = 0; i < 10000; i++)
        {
            var ray = Ray.Create(NextPoint(random), NextPoint(random) * Math.Pow(10, Next(random, 3)));
            var sphere = BoundingSphere.Create(NextPoint(random), Math.Abs(NextPoint(random).X) / 2);

            var (hits, expected) = ExactRaySphere(ray, sphere);

            Assert.That(ray.Intersects(sphere, out double distance), Is.EqualTo(hits), $"case {i}");
            if (hits)
            {
                Assert.That(distance, Is.EqualTo(expected).Within(1e-9 * Math.Max(1, expected)), $"case {i}");
            }
        }

        Assert.That(Ray.Create(Vector3D.Zero, Vector3.Create(0.1, 0.0, 0.0)).Intersects(BoundingSphere.Create(Vector3.Create(10.0, 0.9, 0.0), 1.0)),
            Is.True);
        Assert.That(Ray.Create(Vector3D.Zero, Vector3.Create(1e-3, 0.0, 0.0)).Intersects(Vector3.Create(5.0, 0.0, 0.0)), Is.True);
    }

    [Test]
    public void Parallelism_IsAMatterOfAngle_NotOfLength()
    {
        var tiny = Plane.Create(Vector3.Create(0.001, 0.0, 0.0), 0.0);
        var other = Plane.Create(Vector3.Create(0.0, 0.001, 0.0), 0.0);
        var tilted = new Plane<float>(new Vector3<float>(1, MathF.Tan(5e-4f), 0), 0);
        var slow = Ray.Create(Vector3.Create(0.0, 0.0, -1.0), Vector3.Create(0.0, 0.0, 1e-7));

        Assert.That(tiny.Intersects(other), Is.True);
        Assert.That(Collision.PlaneIntersectsPlane(new Plane<float>(Vector3<float>.UnitX, 0), tilted), Is.True);
        Assert.That(slow.Intersects(Plane.CreateFromPointNormal(Vector3D.Zero, Vector3D.UnitZ), out double toPlane), Is.True);
        Assert.That(toPlane, Is.EqualTo(1e7).Within(1e-3));
        Assert.That(slow.Intersects(BoundingBox.Create(Vector3.Create(-1.0, -1.0, 0.0), Vector3D.One), out double toBox), Is.True);
        Assert.That(toBox, Is.EqualTo(1e7).Within(1e-3));
    }

    [Test]
    public void ShearedSphere_StillHoldsItsCarriedSurface()
    {
        var shear = Matrix4x4<double>.Identity;
        shear.M21 = 1;
        var sphere = BoundingSphere.Create(Vector3D.Zero, 1.0);

        var carried = sphere.Transform(shear);

        for (var angle = 0.0; angle < 2 * Math.PI; angle += 0.01)
        {
            var surface = Vector3D.TransformCoordinate(Vector3.Create(Math.Cos(angle), Math.Sin(angle), 0.0), shear);
            Assert.That(Vector3D.Distance(surface, carried.Center), Is.LessThanOrEqualTo(carried.Radius * (1 + 1e-12)));
        }

        Assert.That(carried.Radius, Is.EqualTo((1 + Math.Sqrt(5)) / 2).Within(1e-12));
    }

    [Test]
    public void ClosestPointOfASphere_LiesOnItsSurface_EvenNextToTheCenter()
    {
        var sphere = BoundingSphere.Create(Vector3D.Zero, 10.0);

        Assert.That(Vector3D.NearEqual(Collision.ClosestPointSpherePoint(sphere, Vector3.Create(5e-7, 0.0, 0.0)), Vector3.Create(10.0, 0.0, 0.0)),
            Is.True);
        Assert.That(Collision.ClosestPointSpherePoint(sphere, Vector3D.Zero).Length(), Is.EqualTo(10.0));
    }

    [Test]
    public void BoxFromPoints_WithANaN_IsTheSameWhereverTheNaNIs()
    {
        var first = BoundingBox.CreateFromPoints<double>([Vector3.Create(double.NaN, 0.0, 0.0), Vector3D.One, -Vector3D.One]);
        var middle = BoundingBox.CreateFromPoints<double>([Vector3D.One, Vector3.Create(double.NaN, 0.0, 0.0), -Vector3D.One]);

        Assert.That(middle.Equals(first), Is.True);
        Assert.That(double.IsNaN(middle.Maximum.X) && middle.Maximum.Y == 1, Is.True);
    }

    [Test]
    public void FlatBox_HoldsAPointSphereLyingInIt()
    {
        var flat = BoundingBox.Create(Vector3D.Zero, Vector3.Create(1.0, 1.0, 0.0));

        Assert.That(flat.Contains(BoundingSphere.Create(Vector3.Create(0.5, 0.5, 0.0), 0.0)), Is.EqualTo(ContainmentType.Contains));
    }

    [Test]
    public void PickRay_ThroughTheMiddle_RunsAlongTheView()
    {
        var viewport = new Viewport<double>(0, 0, 800, 600, 0, 1);
        var view = Matrix4x4.CreateLookAtLH(Vector3.Create(0.0, 0.0, -10.0), Vector3D.Zero, Vector3D.UnitY);
        var projection = Matrix4x4.CreatePerspectiveFovLH(Math.PI / 3, 800.0 / 600, 0.1, 100.0);

        var ray = Ray.CreateFromViewport(400.0, 300.0, viewport, view * projection);

        Assert.That(Vector3D.NearEqual(ray.Direction, Vector3D.UnitZ), Is.True);
        Assert.That(ray.Position.Z, Is.EqualTo(-9.9).Within(1e-9));
    }

    private static float Next(Random random, float spread) => (float)(random.NextDouble() * 2 - 1) * spread;

    private static Vector3D NextPoint(Random random) =>
        Vector3.Create(random.NextDouble() * 8 - 4, random.NextDouble() * 8 - 4, random.NextDouble() * 8 - 4);

    // From the point of the line nearest the center, back along it by the half chord.
    private static (bool Hits, double Distance) ExactRaySphere(Ray<double> ray, BoundingSphere<double> sphere)
    {
        var toCenter = sphere.Center - ray.Position;
        if (toCenter.LengthSquared() <= sphere.Radius * sphere.Radius)
        {
            return (true, 0);
        }

        var length = ray.Direction.Length();
        var nearest = Vector3D.Dot(toCenter, ray.Direction) / length;
        var missSquared = toCenter.LengthSquared() - nearest * nearest;
        if (nearest < 0 || missSquared > sphere.Radius * sphere.Radius)
        {
            return (false, 0);
        }

        return (true, (nearest - Math.Sqrt(sphere.Radius * sphere.Radius - missSquared)) / length);
    }
}
