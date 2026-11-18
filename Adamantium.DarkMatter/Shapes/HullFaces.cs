using System;
using System.Collections.Generic;

namespace Adamantium.DarkMatter.Shapes;

/// <summary>The corners and flat faces of a hull: its triangles lying in one plane merged into one face, outlined by
/// the two-dimensional hull of their corners, each face's plane through its corner reaching farthest.</summary>
internal sealed class HullFaces
{
    private HullFaces(Vector3F[] vertices, Vector3F[] normals, float[] offsets, int[] starts, int[] corners)
    {
        Vertices = vertices;
        Normals = normals;
        Offsets = offsets;
        Starts = starts;
        Corners = corners;
    }

    public Vector3F[] Vertices { get; }

    public Vector3F[] Normals { get; }

    /// <summary>How far each face's plane stands from the origin along its normal.</summary>
    public float[] Offsets { get; }

    /// <summary>Where each face's corners begin in <see cref="Corners"/>, one more than there are faces.</summary>
    public int[] Starts { get; }

    /// <summary>Indices into <see cref="Vertices"/>, face after face, each wound round its face.</summary>
    public int[] Corners { get; }

    public int Count => Normals.Length;

    public static HullFaces Build(ConvexHull hull, float size)
    {
        var vertices = new Vector3F[hull.Vertices.Count];
        for (var index = 0; index < vertices.Length; index++)
        {
            vertices[index] = hull.Vertices[index];
        }

        var faces = MergeFaces(hull, size);
        var normals = new Vector3F[faces.Count];
        var offsets = new float[faces.Count];
        var starts = new int[faces.Count + 1];
        var corners = new List<int>();
        for (var index = 0; index < faces.Count; index++)
        {
            starts[index] = corners.Count;
            corners.AddRange(faces[index].Corners);
            normals[index] = faces[index].Normal;

            // The plane through the corner reaching farthest, so it bounds the hull though its corners lie a hair apart.
            var offset = float.MinValue;
            foreach (var corner in faces[index].Corners)
            {
                offset = MathF.Max(offset, Vector3F.Dot(faces[index].Normal, vertices[corner]));
            }

            offsets[index] = offset;
        }

        starts[faces.Count] = corners.Count;
        return new HullFaces(vertices, normals, offsets, starts, [.. corners]);
    }

    /// <summary>The corners of the face; a face of more corners than <paramref name="into"/> holds gives them spread
    /// evenly round it, so what is kept still spans it.</summary>
    public int GetFace(int face, Span<Vector3F> into)
    {
        var start = Starts[face];
        var corners = Starts[face + 1] - start;
        var count = Math.Min(corners, into.Length);
        for (var corner = 0; corner < count; corner++)
        {
            into[corner] = Vertices[Corners[start + (int)((long)corner * corners / count)]];
        }

        return count;
    }

    /// <summary>The face whose normal turns most toward <paramref name="direction"/>.</summary>
    public int FindFace(Vector3F direction)
    {
        var best = 0;
        var alignment = float.MinValue;
        for (var face = 0; face < Normals.Length; face++)
        {
            var along = Vector3F.Dot(Normals[face], direction);
            if (along > alignment)
            {
                alignment = along;
                best = face;
            }
        }

        return best;
    }

    /// <summary>The corner farthest along <paramref name="direction"/>.</summary>
    public Vector3F GetSupport(Vector3F direction)
    {
        var best = Vertices[0];
        var reach = Vector3F.Dot(best, direction);
        for (var index = 1; index < Vertices.Length; index++)
        {
            var along = Vector3F.Dot(Vertices[index], direction);
            if (along > reach)
            {
                reach = along;
                best = Vertices[index];
            }
        }

        return best;
    }

    // The hull's triangles grouped by the plane they lie in; each group is one face, its outline the two-dimensional
    // hull of its corners in that plane, wound counter-clockwise seen from outside.
    private static List<HullFace> MergeFaces(ConvexHull hull, float size)
    {
        var vertices = hull.Vertices;
        var triangles = hull.Triangles;

        // Quickhull's own flatness, by the hull's size wherever it stands: corners this near one plane are on it.
        var tolerance = 2e-5 * Math.Max(size, 1e-3f);
        var groups = new List<List<int>>();
        var groupNormals = new List<Vector3<double>>();
        var groupOffsets = new List<double>();
        for (var triangle = 0; triangle < triangles.Count; triangle += 3)
        {
            var a = vertices[triangles[triangle]].As<double>();
            var b = vertices[triangles[triangle + 1]].As<double>();
            var c = vertices[triangles[triangle + 2]].As<double>();
            var normal = Vector3<double>.Cross(b - a, c - a);
            var length = normal.Length();
            if (!(length > 0))
                continue;

            normal /= length;
            var offset = Vector3<double>.Dot(normal, a);
            var group = -1;
            for (var index = 0; index < groups.Count; index++)
            {
                var groupNormal = groupNormals[index];
                if (Vector3<double>.Dot(groupNormal, normal) > 0.9
                    && Math.Abs(Vector3<double>.Dot(groupNormal, a) - groupOffsets[index]) <= tolerance
                    && Math.Abs(Vector3<double>.Dot(groupNormal, b) - groupOffsets[index]) <= tolerance
                    && Math.Abs(Vector3<double>.Dot(groupNormal, c) - groupOffsets[index]) <= tolerance)
                {
                    group = index;
                    break;
                }
            }

            if (group < 0)
            {
                group = groups.Count;
                groups.Add([]);
                groupNormals.Add(normal);
                groupOffsets.Add(offset);
            }

            groups[group].Add(triangles[triangle]);
            groups[group].Add(triangles[triangle + 1]);
            groups[group].Add(triangles[triangle + 2]);
        }

        var faces = new List<HullFace>(groups.Count);
        for (var index = 0; index < groups.Count; index++)
        {
            var group = groups[index];
            var normal = groupNormals[index];
            var outline = Outline(vertices, group, normal);
            var outlineArea = outline.Count >= 3 ? Area(vertices, outline, normal) : 0;
            if (outline.Count >= 3 && Math.Abs(outlineArea - TrianglesArea(vertices, group, normal)) <= 1e-4 * outlineArea)
            {
                faces.Add(new HullFace(outline, normal.As<float>()));
                continue;
            }

            // Triangles not filling one convex face - near one plane on a curved hull - stay faces of their own, each
            // with its own normal, so no face reaches over another.
            for (var corner = 0; corner < group.Count; corner += 3)
            {
                var a = vertices[group[corner]].As<double>();
                var own = Vector3<double>.Normalize(Vector3<double>.Cross(vertices[group[corner + 1]].As<double>() - a,
                    vertices[group[corner + 2]].As<double>() - a));
                faces.Add(new HullFace([group[corner], group[corner + 1], group[corner + 2]], own.As<float>()));
            }
        }

        return faces;
    }

    // The area of a polygon of the hull's corners, seen down the normal.
    private static double Area(IReadOnlyList<Vector3F> vertices, List<int> polygon, Vector3<double> normal)
    {
        var sum = Vector3<double>.Zero;
        var origin = vertices[polygon[0]].As<double>();
        for (var index = 1; index < polygon.Count - 1; index++)
        {
            sum += Vector3<double>.Cross(vertices[polygon[index]].As<double>() - origin,
                vertices[polygon[index + 1]].As<double>() - origin);
        }

        return Math.Abs(Vector3<double>.Dot(sum, normal)) * 0.5;
    }

    // The area of a group's triangles, seen down the normal.
    private static double TrianglesArea(IReadOnlyList<Vector3F> vertices, List<int> triangles, Vector3<double> normal)
    {
        var sum = 0.0;
        for (var corner = 0; corner < triangles.Count; corner += 3)
        {
            var a = vertices[triangles[corner]].As<double>();
            var cross = Vector3<double>.Cross(vertices[triangles[corner + 1]].As<double>() - a,
                vertices[triangles[corner + 2]].As<double>() - a);
            sum += Math.Abs(Vector3<double>.Dot(cross, normal)) * 0.5;
        }

        return sum;
    }

    // Andrew's monotone chain over the corners seen down the normal: a face of a convex hull is convex, so this is its
    // outline whole, however its triangles meet.
    private static List<int> Outline(IReadOnlyList<Vector3F> vertices, List<int> corners, Vector3<double> normal)
    {
        var across = AxisFor(normal);
        var up = Vector3<double>.Cross(normal, across);
        var unique = new List<int>();
        foreach (var corner in corners)
        {
            if (!unique.Contains(corner))
                unique.Add(corner);
        }

        unique.Sort((first, second) =>
        {
            var x = Vector3<double>.Dot(vertices[first].As<double>(), across)
                .CompareTo(Vector3<double>.Dot(vertices[second].As<double>(), across));
            return x != 0
                ? x
                : Vector3<double>.Dot(vertices[first].As<double>(), up)
                    .CompareTo(Vector3<double>.Dot(vertices[second].As<double>(), up));
        });

        var outline = new List<int>(unique.Count + 1);
        for (var pass = 0; pass < 2; pass++)
        {
            var floor = outline.Count;
            for (var step = 0; step < unique.Count; step++)
            {
                var corner = unique[pass == 0 ? step : unique.Count - 1 - step];
                while (outline.Count >= floor + 2
                       && Turn(vertices, outline[^2], outline[^1], corner, across, up) <= 0)
                {
                    outline.RemoveAt(outline.Count - 1);
                }

                outline.Add(corner);
            }

            outline.RemoveAt(outline.Count - 1);
        }

        return outline;
    }

    // How the path from a through b to c turns, seen down the normal: more than zero counter-clockwise.
    private static double Turn(IReadOnlyList<Vector3F> vertices, int a, int b, int c, Vector3<double> across,
        Vector3<double> up)
    {
        var pointA = vertices[a].As<double>();
        var pointB = vertices[b].As<double>();
        var pointC = vertices[c].As<double>();
        var abX = Vector3<double>.Dot(pointB - pointA, across);
        var abY = Vector3<double>.Dot(pointB - pointA, up);
        var acX = Vector3<double>.Dot(pointC - pointA, across);
        var acY = Vector3<double>.Dot(pointC - pointA, up);
        return abX * acY - abY * acX;
    }

    private static Vector3<double> AxisFor(Vector3<double> normal)
    {
        var other = Math.Abs(normal.X) < 0.57735 ? Vector3<double>.UnitX : Vector3<double>.UnitY;
        return Vector3<double>.Normalize(Vector3<double>.Cross(other, normal));
    }
}
