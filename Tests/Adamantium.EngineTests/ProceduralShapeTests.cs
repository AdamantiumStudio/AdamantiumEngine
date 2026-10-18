using System;
using System.Collections.Generic;
using Adamantium.ECS;
using Adamantium.ECS.Components;
using Adamantium.Engine.Templates;
using Adamantium.Graphics.Core.Models;
using Adamantium.Mathematics;
using Adamantium.ProceduralGeometry.Shapes;
using NUnit.Framework;

namespace Adamantium.EngineTests;

[TestFixture]
public class ProceduralShapeTests
{
    private static readonly Vector3 Size3 = new(2, 3, 4);

    private static IEnumerable<TestCaseData> SolidsFillingTheirSize()
    {
        yield return new TestCaseData(new CubeShape { Size = Size3 }).SetName("Cube");
        yield return new TestCaseData(new SphereShape { Size = Size3 }).SetName("UV sphere");
        yield return new TestCaseData(new SphereShape { Size = Size3, Type = SphereType.GeoSphere, Tessellation = 4 }).SetName("Geosphere");
        yield return new TestCaseData(new SphereShape { Size = Size3, Type = SphereType.CubeSphere, Tessellation = 8 }).SetName("Cube sphere");
        yield return new TestCaseData(new CylinderShape { Size = Size3 }).SetName("Cylinder");
        yield return new TestCaseData(new ConeShape { Size = Size3 }).SetName("Cone");
        yield return new TestCaseData(new CapsuleShape { Size = Size3 }).SetName("Capsule");
        yield return new TestCaseData(new TorusShape { Size = Size3, Thickness = 0.4 }).SetName("Torus");
        yield return new TestCaseData(new TubeShape { Size = Size3 }).SetName("Tube");
    }

    [TestCaseSource(nameof(SolidsFillingTheirSize))]
    public void ASolid_FillsItsSize_AlongEachAxis(ProceduralShape shape)
    {
        var size = shape.Build().Bounds.Size;

        Assert.Multiple(() =>
        {
            Assert.That(size.X, Is.EqualTo(Size3.X).Within(0.05));
            Assert.That(size.Y, Is.EqualTo(Size3.Y).Within(0.05));
            Assert.That(size.Z, Is.EqualTo(Size3.Z).Within(0.05));
        });
    }

    private static IEnumerable<TestCaseData> FlatShapes()
    {
        yield return new TestCaseData(new RectangleShape { Size = new Vector2(2, 3) }).SetName("Rectangle");
        yield return new TestCaseData(new EllipseShape { Size = new Vector2(2, 3) }).SetName("Ellipse");
        yield return new TestCaseData(new ArcShape { Size = new Vector2(2, 3), StopAngle = 360 }).SetName("Arc");
    }

    [TestCaseSource(nameof(FlatShapes))]
    public void AFlatShape_FillsItsTwoAxes_AndHasNoDepth(ProceduralShape shape)
    {
        var size = shape.Build().Bounds.Size;

        Assert.Multiple(() =>
        {
            Assert.That(size.X, Is.EqualTo(2).Within(0.05));
            Assert.That(size.Y, Is.EqualTo(3).Within(0.05));
            Assert.That(size.Z, Is.EqualTo(0).Within(1e-6));
        });
    }

    [Test]
    public void APlane_LiesOnTheGround_ItsSizeAlongXAndZ()
    {
        var size = new PlaneShape { Size = new Vector2(2, 3) }.Build().Bounds.Size;

        Assert.Multiple(() =>
        {
            Assert.That(size.X, Is.EqualTo(2).Within(1e-3));
            Assert.That(size.Y, Is.EqualTo(0).Within(1e-6));
            Assert.That(size.Z, Is.EqualTo(3).Within(1e-3));
        });
    }

    [Test]
    public void AStretchedSphere_HasTheNormalsOfAnEllipsoid()
    {
        var mesh = new SphereShape { Size = Size3, Tessellation = 16 }.Build();
        var radius = Size3 / 2;

        for (int i = 0; i < mesh.Points.Length; i++)
        {
            var p = mesh.Points[i];
            var expected = Vector3.Normalize(new Vector3(p.X / (radius.X * radius.X), p.Y / (radius.Y * radius.Y), p.Z / (radius.Z * radius.Z)));
            var normal = (Vector3)mesh.Normals[i];
            Assert.That(Vector3.Dot(expected, normal), Is.EqualTo(1).Within(1e-4), $"vertex {i} at {p}");
        }
    }

    [Test]
    public void AStretchedCone_HasItsSideNormalsAcrossItsSide()
    {
        var mesh = new ConeShape { Size = Size3, TopRatio = 0.5, Tessellation = 16 }.Build();

        // The side's vertices come in pairs, top then bottom, sharing a normal; the line between them lies in the side.
        for (int i = 0; i + 1 < 2 * 17; i += 2)
        {
            var along = mesh.Points[i + 1] - mesh.Points[i];
            var normal = (Vector3)mesh.Normals[i];
            Assert.That(Math.Abs(Vector3.Dot(Vector3.Normalize(along), normal)), Is.LessThan(1e-4), $"pair {i / 2}");
        }
    }

    [Test]
    public void AChangedParameter_MakesTheMeshAgainInPlace_OnceAndOnlyThen()
    {
        var shape = new CubeShape();
        var entity = new ShapeTemplate(shape).BuildEntity(new Entity(null, "Cube")).GetAwaiter().GetResult();
        var mesh = entity.GetComponent<MeshData>().Mesh;
        var untouched = mesh.Points;

        shape.RebuildWhenChanged();
        var keptWhileUnchanged = ReferenceEquals(mesh.Points, untouched);
        var before = mesh.Version;
        shape.Size = Size3;
        shape.RebuildWhenChanged();
        var made = mesh.Points;
        var after = mesh.Version;
        shape.RebuildWhenChanged();

        Assert.Multiple(() =>
        {
            Assert.That(keptWhileUnchanged, Is.True, "nothing changed, nothing made");
            Assert.That(entity.GetComponent<MeshData>().Mesh, Is.SameAs(mesh), "the same mesh, so its buffers are uploaded anew");
            Assert.That(mesh.Bounds.Size.Z, Is.EqualTo(Size3.Z).Within(1e-3));
            Assert.That(after, Is.Not.EqualTo(before), "every copy of it - the scene's buffers, the overlay's - sees a new version");
            Assert.That(mesh.Version, Is.EqualTo(after));
            Assert.That(mesh.Points, Is.SameAs(made), "made once for one change");
            Assert.That(entity.GetComponent<Collider>().Bounds.Size.Z, Is.EqualTo(Size3.Z).Within(1e-3), "its collider follows");
        });
    }

    // A hexagon's corners lie on the circle its size is the diameter of, as the other shapes fill theirs.
    [Test]
    public void APolygon_StaysInItsSize()
    {
        var size = new PolygonShape { Size = new Vector2(2, 3), Tessellation = 6 }.Build().Bounds.Size;

        Assert.Multiple(() =>
        {
            Assert.That(size.X, Is.EqualTo(2).Within(0.05));
            Assert.That(size.Y, Is.LessThanOrEqualTo(3.0001));
        });
    }

    [Test]
    public void ACapsule_IsHeldByACapsule_StandingAsItDoes()
    {
        var entity = new ShapeTemplate(new CapsuleShape()).BuildEntity(new Entity(null, "Capsule")).GetAwaiter().GetResult();

        var collider = entity.GetComponent<Collider>();

        Assert.That(collider, Is.TypeOf<CapsuleCollider>());
        var capsule = (CapsuleCollider)collider;
        Assert.Multiple(() =>
        {
            Assert.That(capsule.Direction, Is.EqualTo(CapsuleDirection.Y));
            Assert.That(capsule.Radius, Is.EqualTo(0.5f).Within(1e-3));
            Assert.That(capsule.Height, Is.EqualTo(2).Within(1e-3));
        });
    }

    [Test]
    public void ACubeAndASphere_KeepTheirBox()
    {
        var cube = new ShapeTemplate(new CubeShape()).BuildEntity(new Entity(null, "Cube")).GetAwaiter().GetResult();
        var sphere = new ShapeTemplate(new SphereShape()).BuildEntity(new Entity(null, "Sphere")).GetAwaiter().GetResult();

        Assert.Multiple(() =>
        {
            Assert.That(cube.GetComponent<Collider>(), Is.TypeOf<BoxCollider>());
            Assert.That(sphere.GetComponent<Collider>(), Is.TypeOf<BoxCollider>());
        });
    }

    [Test]
    public void ANewSphere_IsMadeOfRings()
    {
        Assert.That(new SphereShape().Type, Is.EqualTo(SphereType.UVSphere));
    }
}
