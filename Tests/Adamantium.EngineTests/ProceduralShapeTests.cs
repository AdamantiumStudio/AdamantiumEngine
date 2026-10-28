using System;
using System.Collections.Generic;
using Adamantium.ECS;
using Adamantium.ECS.Components;
using Adamantium.Engine.Templates;
using Adamantium.Graphics.Core.Models;
using Adamantium.Mathematics;
using Adamantium.ProceduralGeometry;
using Adamantium.ProceduralGeometry.Shapes;
using NUnit.Framework;

namespace Adamantium.EngineTests;

[TestFixture]
public class ProceduralShapeTests
{
    private static readonly Vector3<double> Size3 = new(2, 3, 4);

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

    // The seam's column is the first one again, for the texture: a band stitched from it to the first column made a
    // ring of slivers whose normals darkened the seam.
    [Test]
    public void ARoundSolid_HasNoSliversAtItsSeam()
    {
        Mesh[] meshes =
        [
            Shapes.Capsule.GenerateGeometry(GeometryType.Solid, 2.0, 1.0, 12),
            Shapes.Sphere.GenerateGeometry(GeometryType.Solid, SphereType.UVSphere, 1.0, 12)
        ];

        foreach (var mesh in meshes)
        {
            for (int i = 0; i + 2 < mesh.Indices.Length; i += 3)
            {
                Vector3<double>[] corners = [mesh.Points[mesh.Indices[i]], mesh.Points[mesh.Indices[i + 1]], mesh.Points[mesh.Indices[i + 2]]];
                for (int k = 0; k < 3; k++)
                {
                    var a = corners[k];
                    var b = corners[(k + 1) % 3];
                    var onAxis = a.X * a.X + a.Z * a.Z < 1e-12;
                    Assert.That(onAxis || (a - b).Length() > 1e-6, Is.True, $"triangle {i / 3} has two corners in one place");
                }
            }
        }
    }

    // Where a patch closes to a point the tangents vanish; rounding there once passed for a surface at some sizes, and
    // the normals on the teapot's axis lay flat.
    [TestCase(1.0, 8)]
    [TestCase(5.0, 13)]
    [TestCase(100.0, 13)]
    public void TheTeapotsNormals_OnItsAxis_PointAlongIt(double size, int tessellation)
    {
        var mesh = Shapes.Teapot.GenerateGeometry(GeometryType.Solid, size, tessellation);

        for (int i = 0; i < mesh.Points.Length; i++)
        {
            var point = mesh.Points[i];
            if (point.X * point.X + point.Z * point.Z < 1e-10 * size * size)
            {
                Assert.That(Math.Abs(mesh.Normals[i].Y), Is.GreaterThan(0.9f), $"point {i} at {point}");
            }
        }
    }

    // Averaged on each side of the seam on its own, the normals of a capsule broke at it and the seam showed.
    [Test]
    public void ACapsulesSeam_HasOneNormal()
    {
        var mesh = Shapes.Capsule.GenerateGeometry(GeometryType.Solid, 2.0, 1.0, 8);

        for (int i = 0; i < mesh.Points.Length; i++)
        {
            for (int j = i + 1; j < mesh.Points.Length; j++)
            {
                if ((mesh.Points[i] - mesh.Points[j]).Length() < 1e-5)
                {
                    Assert.That((mesh.Normals[i] - mesh.Normals[j]).Length(), Is.LessThan(1e-4f), $"points {i} and {j}");
                }
            }
        }
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
        yield return new TestCaseData(new RectangleShape { Size = new Vector2<double>(2, 3) }).SetName("Rectangle");
        yield return new TestCaseData(new EllipseShape { Size = new Vector2<double>(2, 3) }).SetName("Ellipse");
        yield return new TestCaseData(new ArcShape { Size = new Vector2<double>(2, 3), StopAngle = 360 }).SetName("Arc");
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
        var size = new PlaneShape { Size = new Vector2<double>(2, 3) }.Build().Bounds.Size;

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
            var expected = Vector3<double>.Normalize(new Vector3<double>(p.X / (radius.X * radius.X), p.Y / (radius.Y * radius.Y), p.Z / (radius.Z * radius.Z)));
            var normal = mesh.Normals[i].As<double>();
            Assert.That(Vector3<double>.Dot(expected, normal), Is.EqualTo(1).Within(1e-4), $"vertex {i} at {p}");
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
            var normal = mesh.Normals[i].As<double>();
            Assert.That(Math.Abs(Vector3<double>.Dot(Vector3<double>.Normalize(along), normal)), Is.LessThan(1e-4), $"pair {i / 2}");
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
        var size = new PolygonShape { Size = new Vector2<double>(2, 3), Tessellation = 6 }.Build().Bounds.Size;

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
