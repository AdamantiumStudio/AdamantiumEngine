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
    // AlongItsNormals: a shape too knotted to tell its outside from its form - a teapot's handle and spout - is taken at
    // the word of normals it works out from its own surfaces, not from its triangles' winding.
    public enum Outside { TowardTheEye, Up, FromTheCenter, FromTheRing, OffTheWall, AlongItsNormals }

    private static IEnumerable<TestCaseData> Shaped()
    {
        yield return Case("Rectangle", Shapes.Rectangle.GenerateGeometry(GeometryType.Solid, 2, 1, new CornerRadius(0)), Outside.TowardTheEye);
        yield return Case("RoundedRectangle", Shapes.Rectangle.GenerateGeometry(GeometryType.Solid, 2, 1, new CornerRadius(0.2)), Outside.TowardTheEye);
        yield return Case("Ellipse", Shapes.Ellipse.GenerateGeometry(GeometryType.Solid, EllipseType.EdgeToEdge, new Vector2<double>(2, 1)), Outside.TowardTheEye);
        yield return Case("Polygon", Shapes.Polygon.GenerateGeometry(GeometryType.Solid, new Vector2<double>(2), 6), Outside.TowardTheEye);
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

        var stretched = new Vector3<double>(2, 3, 1.5);
        yield return Case("StretchedUVSphere", Shapes.Sphere.GenerateGeometry(GeometryType.Solid, SphereType.UVSphere, stretched, 16), Outside.FromTheCenter);
        yield return Case("StretchedGeoSphere", Shapes.Sphere.GenerateGeometry(GeometryType.Solid, SphereType.GeoSphere, stretched, 3), Outside.FromTheCenter);
        yield return Case("StretchedCylinder", Shapes.Cylinder.GenerateGeometry(GeometryType.Solid, stretched, 16), Outside.FromTheCenter, true);
        yield return Case("StretchedCone", Shapes.Cone.GenerateGeometry(GeometryType.Solid, stretched, 0.5, 16), Outside.FromTheCenter, true);
        yield return Case("StretchedCapsule", Shapes.Capsule.GenerateGeometry(GeometryType.Solid, stretched, 16), Outside.FromTheCenter);
        yield return Case("Arc", Shapes.Arc.GenerateGeometry(GeometryType.Solid, new Vector2<double>(2), 0.3, 0, 270), Outside.TowardTheEye);
        yield return Case("ArcCounterClockwise", Shapes.Arc.GenerateGeometry(GeometryType.Solid, new Vector2<double>(2), 0.3, 0, 270, false), Outside.TowardTheEye);
        yield return Case("Line", Shapes.Line.GenerateGeometry(GeometryType.Solid, new Vector3<double>(-1, 0, 0), new Vector3<double>(1, 0.5, 0), 0.2), Outside.TowardTheEye);
        yield return Case("Polyline", Shapes.Polyline.GenerateGeometry([new Vector2<double>(-1, 0), new Vector2<double>(0, 0.5), new Vector2<double>(1, 0.2)], 0.2f), Outside.TowardTheEye);
        yield return Case("Teapot", Shapes.Teapot.GenerateGeometry(GeometryType.Solid, 2, 8), Outside.AlongItsNormals);
        yield return Case("CoarseTeapot", Shapes.Teapot.GenerateGeometry(GeometryType.Solid, 2, 4), Outside.AlongItsNormals);
        yield return Case("TallTeapot", Shapes.Teapot.GenerateGeometry(GeometryType.Solid, new Vector3<double>(1, 3, 1), 8), Outside.AlongItsNormals);
        yield return Case("FlatTeapot", Shapes.Teapot.GenerateGeometry(GeometryType.Solid, new Vector3<double>(3, 0.5, 3), 8), Outside.AlongItsNormals);
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
        int mirrored = 0, upsideDown = 0, capTurned = 0, beyond = 0, collapsed = 0, triangles = 0, sides = 0;

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
            var face = Vector3<double>.Cross(e1, e2);
            var longest = Math.Max(Math.Max(e1.LengthSquared(), e2.LengthSquared()), (e2 - e1).LengthSquared());
            // A sliver - a pole's points a rounding apart - has no way of its own to face.
            if (face.Length() < 1e-4 * longest) continue;

            triangles++;
            if (Math.Abs(det) < 1e-7)
            {
                collapsed++;
                continue;
            }

            var normal = Vector3<double>.Normalize(face);
            var center = (points[a] + points[b] + points[c]) / 3;
            if (Vector3<double>.Dot(normal, OutsideAt(center, normal, MeshNormal(mesh, a, b, c), outside, midRadius)) < 0) normal = -normal;

            var alongU = (e1 * dv2 - e2 * dv1) / det;
            var alongV = (e2 * du1 - e1 * du2) / det;
            var down = Vector3<double>.Normalize(alongV);

            if (Vector3<double>.Dot(Vector3<double>.Cross(alongU, alongV), normal) >= 0) mirrored++;

            // Round a ring's tube the picture runs down its outer wall and so, coming back, up its inner one - and over
            // a teapot's rim into its opening.
            var innerWall = outside is Outside.FromTheRing or Outside.AlongItsNormals
                            && Vector3<double>.Dot(normal, new Vector3<double>(center.X, 0, center.Z)) < 0;
            if (Math.Abs(normal.Y) < 0.5 && !innerWall)
            {
                sides++;
                if (down.Y <= (outside == Outside.AlongItsNormals ? 0 : 0.8)) upsideDown++;
            }
            else if (flatCaps && Math.Abs(normal.Y) > 0.99)
            {
                // Seen from above its top runs away from the default camera (+Z); seen from below, toward it.
                var wanted = normal.Y < 0 ? -Vector3<double>.UnitZ : Vector3<double>.UnitZ;
                if (Vector3<double>.Dot(down, wanted) <= 0.5) capTurned++;
            }
        }

        Assert.Multiple(() =>
        {
            Assert.That(triangles, Is.GreaterThan(0), "triangles to check");
            Assert.That(beyond, Is.Zero, $"texture coordinates outside 0..1 of {uvs.Length}");
            Assert.That(collapsed, Is.Zero, $"triangles with the picture collapsed to a line or a point, of {triangles}");
            Assert.That(mirrored, Is.Zero, $"mirrored triangles of {triangles}");
            // A free form - a teapot's spout curling at its lip - reads down on all but a few of its sides.
            Assert.That(upsideDown, outside == Outside.AlongItsNormals ? Is.LessThanOrEqualTo(sides / 20) : Is.Zero,
                $"upside-down side triangles of {sides}");
            Assert.That(capTurned, Is.Zero, $"turned cap triangles of {triangles}");
        });
    }

    // The GPU takes a triangle clockwise on screen for its front, which, up being -Y, is the side its
    // cross(p1 - p0, p2 - p0) points away from; that front, and the normals, face out.
    // A mirrored shape shows its picture mirrored, as it should, but its front still outside.
    private static IEnumerable<TestCaseData> Facing()
    {
        foreach (var shaped in Shaped()) yield return shaped;
        var mirror = Matrix4x4.CreateScale<double>(-1, 1, 1);
        yield return Case("MirroredCube", Shapes.Cube.GenerateGeometry(GeometryType.Solid, 2, 1, 3, 2, mirror), Outside.FromTheCenter);
        yield return Case("MirroredRectangle",
            Shapes.Rectangle.GenerateGeometry(GeometryType.Solid, 2, 1, new CornerRadius(0), 20, mirror), Outside.TowardTheEye);
        yield return Case("MirroredArc",
            Shapes.Arc.GenerateGeometry(GeometryType.Solid, new Vector2<double>(2), 0.3, 0, 270, true, 36, mirror), Outside.TowardTheEye);
        var merged = new Mesh(PrimitiveType.TriangleList)
            .Merge([new MergeInstance(Shapes.Cube.GenerateGeometry(GeometryType.Solid, 2, 1, 3, 2), mirror, true)]);
        yield return Case("MergedMirroredCube", merged, Outside.FromTheCenter);
        var mergedArc = new Mesh(PrimitiveType.TriangleStrip)
            .Merge([new MergeInstance(Shapes.Arc.GenerateGeometry(GeometryType.Solid, new Vector2<double>(2), 0.3, 0, 270), mirror, true)]);
        yield return Case("MergedMirroredArc", mergedArc, Outside.TowardTheEye);
    }

    [TestCaseSource(nameof(Facing))]
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
            var face = Vector3<double>.Cross(e1, e2);
            var longest = Math.Max(Math.Max(e1.LengthSquared(), e2.LengthSquared()), (e2 - e1).LengthSquared());
            if (face.Length() < 1e-4 * longest) continue;

            var center = (points[a] + points[b] + points[c]) / 3;
            var sides = Vector3<double>.Normalize(face);
            var out_ = OutsideAt(center, sides, MeshNormal(mesh, a, b, c), outside, midRadius);
            triangles++;
            if (Vector3<double>.Dot(face, out_) > 0) inward++;
            if (outside != Outside.AlongItsNormals && normals.Length == points.Length
                && Vector3<double>.Dot((normals[a] + normals[b] + normals[c]).As<double>(), out_) < 0)
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

    // A teapot told by its form, not by its normals: wound one way all over - an edge two triangles share runs opposite
    // ways in them, a patch turned over would run its edges the same way as its neighbors - its front, by that winding,
    // out at its widest point, and reading down all round its body where neither handle nor spout is.
    [TestCase(4)]
    [TestCase(8)]
    [TestCase(16)]
    public void ATeapot_IsWoundOneWay_FacesOut_AndReadsDownRoundItsBody(int tessellation)
    {
        var mesh = Shapes.Teapot.GenerateGeometry(GeometryType.Solid, 2, tessellation);
        var points = mesh.Points;
        var uvs = mesh.UV0;
        var welded = new Dictionary<(long, long, long), int>();
        int Weld(Vector3<double> p)
        {
            var key = ((long)Math.Round(p.X * 1e4), (long)Math.Round(p.Y * 1e4), (long)Math.Round(p.Z * 1e4));
            if (!welded.TryGetValue(key, out var id)) welded[key] = id = welded.Count;
            return id;
        }

        var edges = new HashSet<(int, int)>();
        int sameWay = 0, upsideDown = 0, body = 0;
        var widest = (X: double.MinValue, Face: Vector3<double>.Zero);
        foreach (var (a, b, c) in Triangles(mesh))
        {
            var e1 = points[b] - points[a];
            var e2 = points[c] - points[a];
            var face = Vector3<double>.Cross(e1, e2);
            var longest = Math.Max(Math.Max(e1.LengthSquared(), e2.LengthSquared()), (e2 - e1).LengthSquared());
            // A sliver at a pole, where a patch's control points meet, runs its edges any way.
            if (face.Length() < 1e-4 * longest) continue;

            int wa = Weld(points[a]), wb = Weld(points[b]), wc = Weld(points[c]);
            foreach (var edge in new[] { (wa, wb), (wb, wc), (wc, wa) })
            {
                if (edge.Item1 != edge.Item2 && !edges.Add(edge)) sameWay++;
            }

            // The handle and the spout stand along z: the body alone is round x.
            var center = (points[a] + points[b] + points[c]) / 3;
            if (Math.Abs(center.Z) < 0.05 && center.X > widest.X) widest = (center.X, face);

            var outward = -Vector3<double>.Normalize(face);
            var radial = new Vector3<double>(center.X, 0, center.Z);
            if (Math.Abs(center.Z) > Math.Abs(center.X) || Math.Abs(outward.Y) >= 0.5 || Vector3<double>.Dot(outward, radial) <= 0) continue;

            var du1 = uvs[b].X - uvs[a].X;
            var dv1 = uvs[b].Y - uvs[a].Y;
            var du2 = uvs[c].X - uvs[a].X;
            var dv2 = uvs[c].Y - uvs[a].Y;
            var det = du1 * dv2 - du2 * dv1;
            if (Math.Abs(det) < 1e-9) continue;

            body++;
            if (Vector3<double>.Normalize((e2 * du1 - e1 * du2) / det).Y <= 0.5) upsideDown++;
        }

        Assert.Multiple(() =>
        {
            Assert.That(sameWay, Is.Zero, "edges two triangles run the same way");
            Assert.That(widest.Face.X, Is.LessThan(0), "the front of the widest triangle, to the side, faces away from the inside");
            Assert.That(body, Is.GreaterThan(0), "sides of the body to check");
            Assert.That(upsideDown, Is.Zero, $"sides of the body, front and back, not reading down, of {body}");
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
    // A strip starts over after each separator (-1), its parity with it.
    private static IEnumerable<(int A, int B, int C)> Triangles(Mesh mesh)
    {
        var strip = mesh.MeshTopology == PrimitiveType.TriangleStrip;
        var indices = mesh.Indices;
        var runStart = 0;
        for (var i = 0; i + 2 < indices.Length; i += strip ? 1 : 3)
        {
            if (strip && indices[i] < 0)
            {
                runStart = i + 1;
                continue;
            }

            if (indices[i + 1] < 0 || indices[i + 2] < 0) continue;

            yield return strip && (i - runStart) % 2 == 1
                ? (indices[i + 1], indices[i], indices[i + 2])
                : (indices[i], indices[i + 1], indices[i + 2]);
        }
    }

    // Which way is out at a point of the surface, roughly: only its side of the surface matters.
    // Of a triangle's vertex normals, the one most in line with it: where the surface folds - a teapot's knob - the
    // vertices on either side of the fold point either way, and their sum says nothing.
    private static Vector3<double> MeshNormal(Mesh mesh, int a, int b, int c)
    {
        if (mesh.Normals.Length != mesh.Points.Length) return Vector3<double>.Zero;

        var face = Vector3<double>.Normalize(Vector3<double>.Cross(mesh.Points[b] - mesh.Points[a], mesh.Points[c] - mesh.Points[a]));
        var best = Vector3<double>.Zero;
        foreach (var index in new[] { a, b, c })
        {
            var normal = mesh.Normals[index].As<double>();
            if (Math.Abs(Vector3<double>.Dot(normal, face)) > Math.Abs(Vector3<double>.Dot(best, face))) best = normal;
        }

        return best;
    }

    private static Vector3<double> OutsideAt(Vector3<double> point, Vector3<double> normal, Vector3<double> meshNormal, Outside outside, double midRadius)
    {
        var radial = new Vector3<double>(point.X, 0, point.Z);
        switch (outside)
        {
            case Outside.AlongItsNormals:
                return meshNormal;
            case Outside.TowardTheEye:
                return -Vector3<double>.UnitZ;
            case Outside.Up:
                return -Vector3<double>.UnitY;
            case Outside.FromTheRing:
                return point - Vector3<double>.Normalize(radial) * midRadius;
            case Outside.OffTheWall:
                if (Math.Abs(normal.Y) > 0.9) return new Vector3<double>(0, point.Y, 0);
                return radial.Length() > midRadius ? radial : -radial;
            default:
                return point;
        }
    }
}
