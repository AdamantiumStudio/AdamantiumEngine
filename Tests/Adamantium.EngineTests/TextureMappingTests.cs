using System;
using System.Collections.Generic;
using System.Linq;
using Adamantium.Graphics.Core;
using Adamantium.Graphics.Core.Models;
using Adamantium.Mathematics;
using Adamantium.ProceduralGeometry;
using Adamantium.ProceduralGeometry.Shapes;
using NUnit.Framework;

namespace Adamantium.EngineTests;

/// <summary>A picture laid on a generated shape reads as it does on screen: the way a rectangle facing the camera shows
/// it, u to the right and v down (+Y, up being -Y), seen from outside, and never mirrored.</summary>
[TestFixture]
public class TextureMappingTests
{
    public enum Outside { TowardTheEye, Up, FromTheCenter, FromTheRing, OffTheWall }

    private static IEnumerable<TestCaseData> Shaped()
    {
        yield return Case("Rectangle", Shapes.Rectangle.GenerateGeometry(GeometryType.Solid, 2, 1, new CornerRadius(0)), Outside.TowardTheEye);
        yield return Case("RoundedRectangle", Shapes.Rectangle.GenerateGeometry(GeometryType.Solid, 2, 1, new CornerRadius(0.2)), Outside.TowardTheEye);
        yield return Case("Ellipse", Shapes.Ellipse.GenerateGeometry(GeometryType.Solid, EllipseType.EdgeToEdge, new Vector2(2, 1)), Outside.TowardTheEye);
        yield return Case("Polygon", Shapes.Polygon.GenerateGeometry(GeometryType.Solid, new Vector2(2), 6), Outside.TowardTheEye);
        yield return Case("Plane", Shapes.Plane.GenerateGeometry(GeometryType.Solid, 2, 1, 2), Outside.Up, true);
        yield return Case("Cube", Shapes.Cube.GenerateGeometry(GeometryType.Solid, 2, 1, 3, 2), Outside.FromTheCenter, true);
        yield return Case("Cylinder", Shapes.Cylinder.GenerateGeometry(GeometryType.Solid, 2, 1, 16), Outside.FromTheCenter, true);
        yield return Case("Cone", Shapes.Cone.GenerateGeometry(GeometryType.Solid, 2, 0.5, 1, 16), Outside.FromTheCenter, true);
        yield return Case("Tube", Shapes.Tube.GenerateGeometry(GeometryType.Solid, 2, 1, 0.2, 16), Outside.OffTheWall, true);
        yield return Case("UVSphere", Shapes.Sphere.GenerateGeometry(GeometryType.Solid, SphereType.UVSphere, 2, 16), Outside.FromTheCenter);
        for (var level = 0; level <= 3; level++)
        {
            yield return Case($"GeoSphere{level}", Shapes.Sphere.GenerateGeometry(GeometryType.Solid, SphereType.GeoSphere, 2, level), Outside.FromTheCenter);
        }

        var stretched = new Vector3(2, 3, 1.5);
        yield return Case("StretchedUVSphere", Shapes.Sphere.GenerateGeometry(GeometryType.Solid, SphereType.UVSphere, stretched, 16), Outside.FromTheCenter);
        yield return Case("StretchedGeoSphere", Shapes.Sphere.GenerateGeometry(GeometryType.Solid, SphereType.GeoSphere, stretched, 3), Outside.FromTheCenter);
        yield return Case("StretchedCylinder", Shapes.Cylinder.GenerateGeometry(GeometryType.Solid, stretched, 16), Outside.FromTheCenter, true);
        yield return Case("StretchedCone", Shapes.Cone.GenerateGeometry(GeometryType.Solid, stretched, 0.5, 16), Outside.FromTheCenter, true);
        yield return Case("StretchedCapsule", Shapes.Capsule.GenerateGeometry(GeometryType.Solid, stretched, 16), Outside.FromTheCenter);
        yield return Case("Arc", Shapes.Arc.GenerateGeometry(GeometryType.Solid, new Vector2(2), 0.3, 0, 270), Outside.TowardTheEye);
        yield return Case("ArcCounterClockwise", Shapes.Arc.GenerateGeometry(GeometryType.Solid, new Vector2(2), 0.3, 0, 270, false), Outside.TowardTheEye);
        yield return Case("Line", Shapes.Line.GenerateGeometry(GeometryType.Solid, new Vector3(-1, 0, 0), new Vector3(1, 0.5, 0), 0.2), Outside.TowardTheEye);
        yield return Case("CubeSphere", Shapes.Sphere.GenerateGeometry(GeometryType.Solid, SphereType.CubeSphere, 2, 8), Outside.FromTheCenter);
        yield return Case("Capsule", Shapes.Capsule.GenerateGeometry(GeometryType.Solid, 1, 1, 16), Outside.FromTheCenter);
        yield return Case("Torus", Shapes.Torus.GenerateGeometry(GeometryType.Solid, 2, 0.5, 16), Outside.FromTheRing);
    }

    private static TestCaseData Case(string name, Mesh mesh, Outside outside, bool flatCaps = false) =>
        new TestCaseData(mesh, outside, flatCaps).SetArgDisplayNames(name);

    [TestCaseSource(nameof(Shaped))]
    public void APicture_ReadsAsOnScreen(Mesh mesh, Outside outside, bool flatCaps)
    {
        var points = mesh.Points;
        var uvs = mesh.UV0;
        var radii = points.Select(p => Math.Sqrt(p.X * p.X + p.Z * p.Z)).ToArray();
        var midRadius = (radii.Min() + radii.Max()) / 2;
        int mirrored = 0, upsideDown = 0, capTurned = 0, beyond = 0, collapsed = 0, triangles = 0;

        foreach (var uv in uvs)
        {
            if (uv.X < -1e-4f || uv.X > 1 + 1e-4f || uv.Y < -1e-4f || uv.Y > 1 + 1e-4f) beyond++;
        }

        foreach (var (a, b, c) in Triangles(mesh))
        {
            var e1 = points[b] - points[a];
            var e2 = points[c] - points[a];
            var du1 = uvs[b].X - uvs[a].X;
            var dv1 = uvs[b].Y - uvs[a].Y;
            var du2 = uvs[c].X - uvs[a].X;
            var dv2 = uvs[c].Y - uvs[a].Y;
            var det = du1 * dv2 - du2 * dv1;
            var face = Vector3.Cross(e1, e2);
            var longest = Math.Max(Math.Max(e1.LengthSquared(), e2.LengthSquared()), (e2 - e1).LengthSquared());
            // A sliver - a pole's points a rounding apart - has no way of its own to face.
            if (face.Length() < 1e-4 * longest) continue;

            triangles++;
            if (Math.Abs(det) < 1e-7)
            {
                collapsed++;
                continue;
            }

            var normal = Vector3.Normalize(face);
            var center = (points[a] + points[b] + points[c]) / 3;
            if (Vector3.Dot(normal, OutsideAt(center, normal, outside, midRadius)) < 0) normal = -normal;

            var alongU = (e1 * dv2 - e2 * dv1) / det;
            var alongV = (e2 * du1 - e1 * du2) / det;
            var down = Vector3.Normalize(alongV);

            if (Vector3.Dot(Vector3.Cross(alongU, alongV), normal) >= 0) mirrored++;

            // Round a ring's tube the picture runs down its outer wall and so, coming back, up its inner one.
            var innerWall = outside == Outside.FromTheRing && Vector3.Dot(normal, new Vector3(center.X, 0, center.Z)) < 0;
            if (Math.Abs(normal.Y) < 0.5 && !innerWall)
            {
                if (down.Y <= 0.8) upsideDown++;
            }
            else if (flatCaps && Math.Abs(normal.Y) > 0.99)
            {
                // Seen from above its top runs away from the default camera (+Z); seen from below, toward it.
                var wanted = normal.Y < 0 ? -Vector3.UnitZ : Vector3.UnitZ;
                if (Vector3.Dot(down, wanted) <= 0.5) capTurned++;
            }
        }

        Assert.Multiple(() =>
        {
            Assert.That(triangles, Is.GreaterThan(0), "triangles to check");
            Assert.That(beyond, Is.Zero, $"texture coordinates outside 0..1 of {uvs.Length}");
            Assert.That(collapsed, Is.Zero, $"triangles with the picture collapsed to a line or a point, of {triangles}");
            Assert.That(mirrored, Is.Zero, $"mirrored triangles of {triangles}");
            Assert.That(upsideDown, Is.Zero, $"upside-down side triangles of {triangles}");
            Assert.That(capTurned, Is.Zero, $"turned cap triangles of {triangles}");
        });
    }

    // The GPU takes a triangle clockwise on screen for its front, which, up being -Y, is the side its
    // cross(p1 - p0, p2 - p0) points away from; that front, and the normals, face out.
    [TestCaseSource(nameof(Shaped))]
    public void AShape_ShowsItsFrontOutside_WithItsNormalsOut(Mesh mesh, Outside outside, bool flatCaps)
    {
        var points = mesh.Points;
        var normals = mesh.Normals;
        var radii = points.Select(p => Math.Sqrt(p.X * p.X + p.Z * p.Z)).ToArray();
        var midRadius = (radii.Min() + radii.Max()) / 2;
        int inward = 0, normalsIn = 0, triangles = 0;

        foreach (var (a, b, c) in Triangles(mesh))
        {
            var e1 = points[b] - points[a];
            var e2 = points[c] - points[a];
            var face = Vector3.Cross(e1, e2);
            var longest = Math.Max(Math.Max(e1.LengthSquared(), e2.LengthSquared()), (e2 - e1).LengthSquared());
            if (face.Length() < 1e-4 * longest) continue;

            var center = (points[a] + points[b] + points[c]) / 3;
            var sides = Vector3.Normalize(face);
            var out_ = OutsideAt(center, sides, outside, midRadius);
            triangles++;
            if (Vector3.Dot(face, out_) > 0) inward++;
            if (normals.Length == points.Length && Vector3.Dot((Vector3)(normals[a] + normals[b] + normals[c]), out_) < 0)
            {
                normalsIn++;
            }
        }

        Assert.Multiple(() =>
        {
            Assert.That(triangles, Is.GreaterThan(0), "triangles to check");
            Assert.That(inward, Is.Zero, $"triangles showing their front inside, of {triangles}");
            Assert.That(normalsIn, Is.Zero, $"triangles with their normals in, of {triangles}");
        });
    }

    // Where a round side's picture comes round to meet itself is out of sight of a camera in front: at the back.
    private static IEnumerable<TestCaseData> RoundSides()
    {
        yield return Case("Cylinder", Shapes.Cylinder.GenerateGeometry(GeometryType.Solid, 2, 1, 16), Outside.FromTheCenter);
        yield return Case("Cone", Shapes.Cone.GenerateGeometry(GeometryType.Solid, 2, 0.5, 1, 16), Outside.FromTheCenter);
        yield return Case("UVSphere", Shapes.Sphere.GenerateGeometry(GeometryType.Solid, SphereType.UVSphere, 2, 16), Outside.FromTheCenter);
        yield return Case("GeoSphere", Shapes.Sphere.GenerateGeometry(GeometryType.Solid, SphereType.GeoSphere, 2, 3), Outside.FromTheCenter);
        yield return Case("Capsule", Shapes.Capsule.GenerateGeometry(GeometryType.Solid, 1, 1, 16), Outside.FromTheCenter);
        yield return Case("Torus", Shapes.Torus.GenerateGeometry(GeometryType.Solid, 2, 0.5, 16), Outside.FromTheRing);
        yield return Case("Tube", Shapes.Tube.GenerateGeometry(GeometryType.Solid, 2, 1, 0.2, 16), Outside.OffTheWall);
    }

    [TestCaseSource(nameof(RoundSides))]
    public void ARoundSidesSeam_IsAtTheBack(Mesh mesh, Outside outside, bool flatCaps)
    {
        var inFront = 0;
        for (var i = 0; i < mesh.Points.Length; i++)
        {
            var point = mesh.Points[i];
            var onSide = Math.Abs(mesh.Normals[i].Y) < 0.99f && Math.Abs(point.X) + Math.Abs(point.Z) > 1e-6;
            var onSeam = mesh.UV0[i].X < 1e-4f || mesh.UV0[i].X > 1 - 1e-4f;
            if (onSide && onSeam && point.Z < -1e-6) inFront++;
        }

        Assert.That(inFront, Is.Zero, "points of the seam in front");
    }

    // A strip's every other triangle runs its first two points the other way round.
    private static IEnumerable<(int A, int B, int C)> Triangles(Mesh mesh)
    {
        var strip = mesh.MeshTopology == PrimitiveType.TriangleStrip;
        var indices = mesh.Indices;
        for (var i = 0; i + 2 < indices.Length; i += strip ? 1 : 3)
        {
            yield return strip && i % 2 == 1
                ? (indices[i + 1], indices[i], indices[i + 2])
                : (indices[i], indices[i + 1], indices[i + 2]);
        }
    }

    // Which way is out at a point of the surface, roughly: only its side of the surface matters.
    private static Vector3 OutsideAt(Vector3 point, Vector3 normal, Outside outside, double midRadius)
    {
        var radial = new Vector3(point.X, 0, point.Z);
        switch (outside)
        {
            case Outside.TowardTheEye:
                return -Vector3.UnitZ;
            case Outside.Up:
                return -Vector3.UnitY;
            case Outside.FromTheRing:
                return point - Vector3.Normalize(radial) * midRadius;
            case Outside.OffTheWall:
                if (Math.Abs(normal.Y) > 0.9) return new Vector3(0, point.Y, 0);
                return radial.Length() > midRadius ? radial : -radial;
            default:
                return point;
        }
    }
}
