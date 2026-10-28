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
    public void PickRay_ThroughTheMiddle_RunsAlongTheView()
    {
        var viewport = new Viewport<double>(0, 0, 800, 600, 0, 1);
        var view = Matrix4x4.CreateLookAtLH(Vector3.Create(0.0, 0.0, -10.0), Vector3D.Zero, Vector3D.UnitY);
        var projection = Matrix4x4.CreatePerspectiveFovLH(Math.PI / 3, 800.0 / 600, 0.1, 100.0);

        var ray = Ray.CreateFromViewport(400.0, 300.0, viewport, view * projection);

        Assert.That(Vector3D.NearEqual(ray.Direction, Vector3D.UnitZ), Is.True);
        Assert.That(ray.Position.Z, Is.EqualTo(-9.9).Within(1e-9));
    }
}
