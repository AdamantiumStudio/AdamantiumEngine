using System;
using System.Linq;
using Adamantium.ECS;
using Adamantium.ECS.Components;
using Adamantium.Graphics.Core;
using Adamantium.Graphics.Core.Models;
using Adamantium.Engine.Templates.GeometricPrimitives;
using Adamantium.Mathematics;
using Adamantium.ProceduralGeometry;
using Adamantium.ProceduralGeometry.Shapes;
using NUnit.Framework;

namespace Adamantium.EngineTests;

[TestFixture]
public class PrimitiveTemplateTests
{
    [Test]
    public void Ellipse_RunsFromItsStartAngleToItsStopAngle()
    {
        var template = new EllipseTemplate(GeometryType.Outlined, EllipseType.EdgeToEdge, new Vector2(2), 90, 180);

        var entity = template.BuildEntity(new Entity(null, "Arc")).GetAwaiter().GetResult();

        var meshData = entity.GetComponent<MeshData>();
        var points = meshData.Mesh.Contours[0].Points;
        Assert.That(points[0].X, Is.EqualTo(0).Within(1e-3));
        Assert.That(points[0].Y, Is.EqualTo(1).Within(1e-3));
        Assert.That(points[^1].X, Is.EqualTo(-1).Within(1e-3));
        Assert.That(points[^1].Y, Is.EqualTo(0).Within(1e-3));
    }

    [Test]
    public void ASolidArcShortOfACircle_EndsWhereItsAngleDoes_WithoutATriangleAcrossTheGap()
    {
        var mesh = Shapes.Arc.GenerateGeometry(GeometryType.Solid, new Vector2(2), 0.2, 0, 270, true, 36);

        Assert.Multiple(() =>
        {
            Assert.That(mesh.Indices.Length, Is.EqualTo(mesh.Points.Length));
            Assert.That(mesh.Indices[^1], Is.EqualTo(mesh.Points.Length - 1), "the strip's last triangle closes no gap");
        });
    }

    // Up is -Y: the top of a shape has the smaller y.
    [TestCase(GeometryType.Solid)]
    [TestCase(GeometryType.Outlined)]
    public void AConesPoint_IsAtItsTop_UpBeingMinusY(GeometryType type)
    {
        var pointUp = Shapes.Cone.GenerateGeometry(type, 2, 0, 1, 24);
        var pointDown = Shapes.Cone.GenerateGeometry(type, 2, 1, 0, 24);

        Assert.Multiple(() =>
        {
            Assert.That(Widest(pointUp), Is.EqualTo(1).Within(1e-3), "the rim stands at the bottom");
            Assert.That(Widest(pointDown), Is.EqualTo(-1).Within(1e-3), "the rim stands at the top");
        });
    }

    [Test]
    public void ATeapotsLid_IsAtItsTop_UpBeingMinusY()
    {
        var mesh = Shapes.Teapot.GenerateGeometry(GeometryType.Solid, 1, 4);
        var lid = mesh.Points.MinBy(p => p.Y);

        Assert.That(Math.Abs(lid.X) + Math.Abs(lid.Z), Is.LessThan(0.2), "the highest point is the lid's knob, over the middle");
    }

    [Test]
    public void AnOutlinedCube_IsItsTwelveEdges_EachAPairOfCorners()
    {
        var mesh = Shapes.Cube.GenerateGeometry(GeometryType.Outlined, 2, 2, 2);

        Assert.Multiple(() =>
        {
            Assert.That(mesh.MeshTopology, Is.EqualTo(PrimitiveType.LineList));
            Assert.That(mesh.Indices.Length, Is.EqualTo(24));
            Assert.That(mesh.Indices, Is.All.InRange(0, mesh.Points.Length - 1), "no restart, which a list has no use for");
            for (int i = 0; i < mesh.Indices.Length; i += 2)
            {
                var edge = mesh.Points[mesh.Indices[i + 1]] - mesh.Points[mesh.Indices[i]];
                Assert.That(edge.Length(), Is.EqualTo(2).Within(1e-6), "an edge runs between neighboring corners");
            }
        });
    }

    // A camera looks along +Z: a flat shape faces it when its triangles and normals face -Z.
    [TestCase("Rectangle")]
    [TestCase("Ellipse")]
    [TestCase("Polygon")]
    [TestCase("Arc")]
    [TestCase("Line")]
    public void AFlatShape_FacesTheEye_LookingAlongPlusZ(string shape)
    {
        var mesh = shape switch
        {
            "Rectangle" => Shapes.Rectangle.GenerateGeometry(GeometryType.Solid, 2, 1, new CornerRadius(0)),
            "Ellipse" => Shapes.Ellipse.GenerateGeometry(GeometryType.Solid, EllipseType.EdgeToEdge, new Vector2(2)),
            "Polygon" => Shapes.Polygon.GenerateGeometry(GeometryType.Solid, new Vector2(2), 6),
            "Arc" => Shapes.Arc.GenerateGeometry(GeometryType.Solid, new Vector2(2), 0.2, 0, 270),
            _ => Shapes.Line.GenerateGeometry(GeometryType.Solid, new Vector3(-1, 0, 0), new Vector3(1, 0, 0), 0.1)
        };

        var strip = mesh.MeshTopology == PrimitiveType.TriangleStrip;
        Assert.Multiple(() =>
        {
            Assert.That(mesh.Indices, Is.All.GreaterThanOrEqualTo(0), "no restart, which not every draw turns on");
            for (int i = 0; i + 2 < mesh.Indices.Length; i += strip ? 1 : 3)
            {
                // A strip's every other triangle runs its first two points the other way round.
                var (b, c) = strip && i % 2 == 1 ? (i + 2, i + 1) : (i + 1, i + 2);
                var a = mesh.Points[mesh.Indices[i]];
                var facing = Vector3.Cross(mesh.Points[mesh.Indices[b]] - a, mesh.Points[mesh.Indices[c]] - a);
                Assert.That(facing.Z, Is.LessThanOrEqualTo(1e-9), $"triangle {i}");
            }

            Assert.That((mesh.Normals ?? []).Select(n => n.Z), Is.All.EqualTo(-1).Within(1e-4), "normals, where it has them");
        });
    }

    // The y of the points farthest from the axis.
    private static double Widest(Mesh mesh)
    {
        var widest = mesh.Points.MaxBy(p => p.X * p.X + p.Z * p.Z);
        return widest.Y;
    }
}
