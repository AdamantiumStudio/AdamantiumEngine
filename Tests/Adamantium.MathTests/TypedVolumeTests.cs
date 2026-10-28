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
        Assert.That(Vector3D.NearEqual(point, Vector3.Create(0.0, 0.0, 8.0)), Is.True);
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
    public void SphereRadius_GrowsByTheLargestStretch_HoweverClose_AndAtAnyScale()
    {
        var random = new Random(5);
        for (var i = 0; i < 2000; i++)
        {
            var largest = Math.Pow(10, random.NextDouble() * 4 - 2);
            var close = largest * (1 - Math.Pow(10, -3 - random.NextDouble() * 9));
            var matrix = RandomRotation(random) * Matrix4x4.CreateScale(largest, close, largest * random.NextDouble()) * RandomRotation(random);

            Assert.That(BoundingSphere.Create(Vector3D.Zero, 1.0).Transform(matrix).Radius, Is.EqualTo(largest).Within(1e-14 * largest), $"case {i}");
        }

        foreach (var scale in new[] { 4e6f, 1e7f, 1e-8f, 6.378e6f, 1e-10f })
        {
            var shear = Matrix4x4<float>.Identity * scale;
            shear.M21 = scale;
            shear.M44 = 1;
            var stretched = Matrix4x4.CreateRotationZ(0.3f) * Matrix4x4.CreateScale(scale, scale / 3, scale / 7);

            Assert.That(BoundingSphere.Create(Vector3<float>.Zero, 1f).Transform(shear).Radius, Is.EqualTo(scale * 1.618034f).Within(1e-6f * scale));
            Assert.That(BoundingSphere.Create(Vector3<float>.Zero, 1f).Transform(stretched).Radius, Is.EqualTo(scale).Within(1e-6f * scale));
        }

        var unsheared = Matrix4x4.CreateScale(2f, 2f, 0.5f) * Matrix4x4.CreateFromYawPitchRoll(0.4f, 1.1f, -0.7f);
        Assert.That(BoundingSphere.Create(Vector3<float>.Zero, 1f).Transform(unsheared).Radius, Is.EqualTo(2f).Within(4e-7f));
    }

    [Test]
    public void ParallelRays_MeetOnlyOnOneLine_AndARayWithoutDirectionMeetsNothing()
    {
        var along = Ray.Create(Vector3D.Zero, Vector3D.UnitX);

        Assert.That(along.Intersects(Ray.Create(Vector3.Create(1e6, 0.5, 0.0), Vector3D.UnitX), out _), Is.False);
        Assert.That(along.Intersects(Ray.Create(Vector3.Create(100.0, 1e-4, 0.0), Vector3D.UnitX), out _), Is.False);
        Assert.That(along.Intersects(Ray.Create(Vector3.Create(100.0, 0.0, 0.0), -Vector3D.UnitX), out Vector3D met), Is.True);
        Assert.That(met, Is.EqualTo(Vector3.Create(100.0, 0.0, 0.0)));
        Assert.That(Ray.Create(Vector3D.Zero, Vector3D.Zero).Intersects(Ray.Create(Vector3.Create(5.0, 5.0, 5.0), Vector3D.UnitX), out _), Is.False);
        Assert.That(Ray.Create(Vector3.Create(5.0, 5.0, 5.0), Vector3D.UnitX).Intersects(Ray.Create(Vector3D.Zero, Vector3D.Zero), out _), Is.False);
    }

    [Test]
    public void SkewRays_Apart_DoNotMeet()
    {
        var random = new Random(3);
        for (var i = 0; i < 10000; i++)
        {
            var crossing = new Vector3<float>(Next(random, 4), Next(random, 4), Next(random, 4));
            var (start1, start2) = (new Vector3<float>(Next(random, 4), Next(random, 4), Next(random, 4)),
                new Vector3<float>(Next(random, 4), Next(random, 4), Next(random, 4)));
            var direction2 = crossing - start2;
            var normal = Vector3<float>.Normalize(Vector3<float>.Cross(crossing - start1, direction2));
            var ray2 = Ray.Create(start2 + normal * 0.01f, direction2);

            Assert.That(Collision.RayIntersectsRay(Ray.Create(start1, crossing - start1), ray2, out _), Is.False, $"case {i}");
        }
    }

    [Test]
    public void LineOfTwoPlanes_LiesOnBoth_WithADirectionOfLengthOne_WhateverTheirNormalsLength()
    {
        foreach (var length in new[] { 1e-12f, 1e-20f, 5e-4f, 1e10f, 1e20f })
        {
            var floor = new Plane<float>(new Vector3<float>(0, length, 0), -2 * length);
            var wall = new Plane<float>(new Vector3<float>(length, 0, length), -length);

            Assert.That(floor.Intersects(wall, out Ray<float> line), Is.True, $"normals of {length}");
            Assert.That(line.Direction.Length(), Is.EqualTo(1f).Within(1e-6f));
            Assert.That(line.Position.Y, Is.EqualTo(2f).Within(1e-5f));
            Assert.That(line.Position.X + line.Position.Z, Is.EqualTo(1f).Within(1e-5f));
        }
    }

    [Test]
    public void ANaN_IsNoHit()
    {
        var ray = Ray.Create(Vector3.Create(0.0, 0.0, -5.0), Vector3D.UnitZ);
        var floor = Plane.CreateFromPointNormal(Vector3D.Zero, Vector3D.UnitZ);

        Assert.That(ray.Intersects(new Plane<double>(Vector3D.UnitZ, double.NaN), out double _), Is.False);
        Assert.That(Ray.Create(Vector3.Create(double.NaN, 0.0, -5.0), Vector3D.UnitZ).Intersects(floor, out double _), Is.False);
        Assert.That(floor.Intersects(new Plane<double>(Vector3D.UnitX, double.NaN)), Is.False);
        Assert.That(floor.Intersects(new Plane<double>(Vector3D.UnitX, double.NaN), out Ray<double> _), Is.False);
        Assert.That(Ray.Create(Vector3D.Zero, Vector3.Create(double.NaN, double.NaN, double.NaN)).Intersects(UnitBox), Is.False);
        Assert.That(Ray.Create(Vector3.Create(double.NaN, 0.0, -5.0), Vector3D.UnitZ).Intersects(UnitBox), Is.False);
        Assert.That(Ray.Create(Vector3.Create(double.NaN, 0.0, -5.0), Vector3D.UnitZ)
            .Intersects(Vector3.Create(-1.0, -1.0, 0.0), Vector3.Create(1.0, -1.0, 0.0), Vector3.Create(0.0, 1.0, 0.0), out double _), Is.False);
    }

    [Test]
    public void TinyAndHugeLengths_AnswerAsAtOne()
    {
        var gap = Collision.DistanceSquaredSegmentSegment(new Vector3<float>(-1e-12f, 0, 0), new Vector3<float>(1e-12f, 0, 0),
            new Vector3<float>(0, -1e-12f, 1e-12f), new Vector3<float>(0, 1e-12f, 1e-12f));
        var toSegment = Collision.RayIntersectsLineSegment(Ray.Create(new Vector3<float>(0, -5, 1), new Vector3<float>(0, 1e20f, 0)),
            new Vector3<float>(-1, 0, 0), new Vector3<float>(1, 0, 0), out _);
        var still = Collision.RayIntersectsLineSegment(Ray.Create(Vector3.Create(5.0, 1.0, 0.0), Vector3D.Zero), Vector3D.Zero, Vector3D.UnitX, out _);
        var sphere = BoundingSphere.Create(new Vector3<float>(0, 0, 10), 1f);

        Assert.That(gap, Is.EqualTo(1e-24f).Within(1e-30f));
        Assert.That(toSegment, Is.EqualTo(1f).Within(1e-6f));
        Assert.That(still, Is.EqualTo(Math.Sqrt(17)).Within(1e-12));
        Assert.That(Ray.Create(Vector3<float>.Zero, new Vector3<float>(0, 0, 1e-25f)).Intersects(sphere, out float distance), Is.True);
        Assert.That(distance, Is.EqualTo(9e25f).Within(1e20f));
        Assert.That(Ray.Create(new Vector3<float>(0, 0, -3e19f), Vector3<float>.UnitZ).Intersects(sphere), Is.True);
        Assert.That(Ray.Create(Vector3<float>.Zero, new Vector3<float>(1e-25f, 0, 0)).Intersects(new Vector3<float>(5, 0, 0)), Is.True);
    }

    [Test]
    public void SmallShapesFarAway_AnswerAsNearBy()
    {
        static Vector3<float> V(float x, float y, float z) => new(x, y, z);
        static Vector3<Half> H(float x, float y, float z) => new((Half)x, (Half)y, (Half)z);

        Assert.That(Collision.DistanceSquaredSegmentSegment(V(-0.5f, 0, 0), V(0.5f, 0, 0), V(0, -0.5f, 1e12f), V(0, 0.5f, 1e12f)),
            Is.EqualTo(1e24f).Within(1e18f));
        Assert.That(Collision.DistanceSquaredSegmentSegment(V(-1e-5f, 0, 0), V(1e-5f, 0, 0), V(0, -1e-5f, 1e7f), V(0, 1e-5f, 1e7f)),
            Is.EqualTo(1e14f).Within(1e8f));
        Assert.That((float)Collision.DistanceSquaredSegmentSegment(H(-0.1f, 0, 0), H(0.1f, 0, 0), H(0, -0.1f, 100), H(0, 0.1f, 100)),
            Is.EqualTo(1e4f).Within(20f));

        var unit = (V(0, 0, 0), V(1, 0, 0), V(0, 1, 0));
        Assert.That(Ray.Create(V(0.2f, 0.2f, -1e12f), Vector3<float>.UnitZ).Intersects(unit.Item1, unit.Item2, unit.Item3, out float far), Is.True);
        Assert.That(far, Is.EqualTo(1e12f).Within(1e6f));
        Assert.That(Ray.Create(V(2e-4f, 2e-4f, -1e9f), Vector3<float>.UnitZ).Intersects(V(0, 0, 0), V(1e-3f, 0, 0), V(0, 1e-3f, 0), out float _),
            Is.True);
        Assert.That(Ray.Create(V(2e9f, 2e9f, -1e10f), Vector3<float>.UnitZ).Intersects(V(0, 0, 0), V(1e10f, 0, 0), V(0, 1e10f, 0), out float _),
            Is.True);
        Assert.That(Ray.Create(H(0.2f, 0.2f, -1000), Vector3<Half>.UnitZ).Intersects(H(0, 0, 0), H(1, 0, 0), H(0, 1, 0), out Half _), Is.True);

        Assert.That(Collision.RayIntersectsLineSegment(Ray.Create(V(0, 1, 0), Vector3<float>.Zero), V(-1e20f, 0, 0), V(1e20f, 0, 0), out var under),
            Is.EqualTo(1f));
        Assert.That(under, Is.EqualTo(Vector3<float>.Zero));
        Assert.That(Collision.RayIntersectsLineSegment(Ray.Create(Vector3<float>.Zero, V(1e-10f, 0, 0)), V(1e30f, -1, 0), V(1e30f, 1, 0), out _),
            Is.EqualTo(0f));
        Assert.That((float)Collision.RayIntersectsLineSegment(Ray.Create(Vector3<Half>.Zero, Vector3<Half>.UnitX), H(1000, -1, 0), H(1000, 1, 0), out _),
            Is.EqualTo(0f));
    }

    [Test]
    public void ExtremeRatios_InFloatAndHalf_KeepTheirAnswer()
    {
        static Vector3<Half> H(float x, float y, float z) => new((Half)x, (Half)y, (Half)z);

        Assert.That(Collision.RayIntersectsLineSegment(Ray.Create(Vector3<float>.Zero, new Vector3<float>(1e-20f, 0, 0)),
            new Vector3<float>(1e30f, -1, 0), new Vector3<float>(1e30f, 1, 0), out var point), Is.EqualTo(0f));
        Assert.That(point, Is.EqualTo(new Vector3<float>(1e30f, 0, 0)));
        Assert.That((float)Collision.RayIntersectsLineSegment(Ray.Create(Vector3<Half>.Zero, H(0.001f, 0, 0)), H(60000, -1, 0), H(60000, 1, 0), out _),
            Is.EqualTo(0f).Within(0.01f), "within Half's precision at 60000");
        Assert.That(Ray.Create(H(0.002f, 0.002f, 1000), -Vector3<Half>.UnitZ).Intersects(H(0, 0, 0), H(0.01f, 0, 0), H(0, 0.01f, 0), out Half far),
            Is.True);
        Assert.That((float)far, Is.EqualTo(1000f));
        Assert.That(Ray.Create(H(2e-4f, 2e-4f, 100), -Vector3<Half>.UnitZ).Intersects(H(0, 0, 0), H(1e-3f, 0, 0), H(0, 1e-3f, 0), out Half _),
            Is.True);
        Assert.That(Collision.RayIntersectsRay(Ray.Create(Vector3<Half>.Zero, Vector3<Half>.UnitX), Ray.Create(H(0, 1, 0), H(1, -2e-4f, 0)),
            out var meeting), Is.True);
        Assert.That((float)meeting.X, Is.EqualTo(5000f).Within(50f));
    }

    [Test]
    public void AHitBeyondTheTypesReach_IsNoHit()
    {
        var crawling = Ray.Create(Vector3<float>.Zero, new Vector3<float>(1e-30f, 0, 0));
        var wall = new Plane<float>(Vector3<float>.UnitX, -1e10f);
        var box = BoundingBox.Create(new Vector3<float>(1e10f, -1, -1), new Vector3<float>(2e10f, 1, 1));

        Assert.That(crawling.Intersects(wall, out float _), Is.False);
        Assert.That(crawling.Intersects(box, out float _), Is.False);
        Assert.That(crawling.Intersects(new Vector3<float>(1e10f, -1, -1), new Vector3<float>(1e10f, 1, -1), new Vector3<float>(1e10f, 0, 1), out float _),
            Is.False);
    }

    [Test]
    public void FarAndNearParallelRays_MeetOnlyOnOneLine()
    {
        var right = Ray.Create(new Vector3<float>(-1e30f, 0, 0), Vector3<float>.UnitX);
        var left = Ray.Create(new Vector3<float>(1e30f, 1e22f, 0), -Vector3<float>.UnitX);
        var near = Ray.Create(new Vector3<float>(1e-25f, 1e-25f, 0), Vector3<float>.UnitX);

        Assert.That(Collision.RayIntersectsRay(right, left, out _), Is.True);
        Assert.That(Collision.RayIntersectsRay(Ray.Create(Vector3<float>.Zero, Vector3<float>.UnitX), near, out _), Is.False);
    }

    [Test]
    public void HugeNormalsAndShortDirections_KeepTheirAnswer()
    {
        var plane = new Plane<float>(new Vector3<float>(0, 0, 1e30f), -1e30f);
        var sphere = BoundingSphere.Create(new Vector3<float>(1e10f, 0, 0), 1f);

        Assert.That(Ray.Create(Vector3<float>.Zero, new Vector3<float>(0, 0, 1e30f)).Intersects(plane, out float distance), Is.True);
        Assert.That(distance, Is.EqualTo(1e-30f).Within(1e-36f));
        Assert.That(Ray.Create(Vector3<float>.Zero, new Vector3<float>(1e-30f, 0, 0)).Intersects(sphere, out Vector3<float> _), Is.False);
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

    private static Matrix4x4<double> RandomRotation(Random random) =>
        Matrix4x4.CreateFromYawPitchRoll(random.NextDouble() * 7, random.NextDouble() * 7, random.NextDouble() * 7);

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
