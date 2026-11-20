using System;
using System.Collections.Generic;

namespace Adamantium.DarkMatter.Shapes;

/// <summary>The corners and flat faces of a hull: its triangles lying in one plane merged into one face, outlined by
/// the two-dimensional hull of their corners, each face's plane through its corner reaching farthest.</summary>
internal sealed class HullFaces
{
    // Quickhull's own flatness, as a share of a hull's size: corners this near one plane are on it.
    private const double Flatness = 2e-5;

    // The faces at each corner, every corner a face's triangles touch counted: those of corner i from
    // _facesAtStarts[i] up to _facesAtStarts[i + 1].
    private readonly int[] _facesAtStarts;
    private readonly int[] _facesAt;

    // The corners' coordinates each in an array of its own, so the search for the farthest reads them in a row.
    private readonly float[] _xs;
    private readonly float[] _ys;
    private readonly float[] _zs;

    private HullFaces(Vector3F[] vertices, List<HullFace> faces)
    {
        Vertices = vertices;
        _xs = new float[vertices.Length];
        _ys = new float[vertices.Length];
        _zs = new float[vertices.Length];
        for (var index = 0; index < vertices.Length; index++)
        {
            _xs[index] = vertices[index].X;
            _ys[index] = vertices[index].Y;
            _zs[index] = vertices[index].Z;
        }

        Normals = new Vector3F[faces.Count];
        Offsets = new float[faces.Count];
        Starts = new int[faces.Count + 1];
        var corners = new List<int>();
        _facesAtStarts = new int[vertices.Length + 1];
        for (var face = 0; face < faces.Count; face++)
        {
            Starts[face] = corners.Count;
            corners.AddRange(faces[face].Corners);
            Normals[face] = faces[face].Normal;

            // The plane through the hull's corner reaching farthest that way, so it bounds the hull though a face's
            // corners lie a hair apart, or a joined face's normal is the mean of its parts'.
            var offset = float.MinValue;
            foreach (var vertex in vertices)
            {
                offset = MathF.Max(offset, Vector3F.Dot(faces[face].Normal, vertex));
            }

            foreach (var corner in faces[face].Touching)
            {
                _facesAtStarts[corner + 1]++;
            }

            Offsets[face] = offset;
        }

        Starts[faces.Count] = corners.Count;
        Corners = [.. corners];
        for (var index = 0; index < vertices.Length; index++)
        {
            _facesAtStarts[index + 1] += _facesAtStarts[index];
        }

        _facesAt = new int[_facesAtStarts[vertices.Length]];
        var filled = new int[vertices.Length];
        for (var face = 0; face < faces.Count; face++)
        {
            foreach (var corner in faces[face].Touching)
            {
                _facesAt[_facesAtStarts[corner] + filled[corner]++] = face;
            }
        }
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

    /// <summary>The faces of <paramref name="hull"/>; with <paramref name="joinFlatness"/>, a share of its size, faces
    /// meeting at an edge whose corners all lie that near one plane are one.</summary>
    public static HullFaces Build(ConvexHull hull, float size, double joinFlatness = 0)
    {
        var vertices = new Vector3F[hull.Vertices.Count];
        for (var index = 0; index < vertices.Length; index++)
        {
            vertices[index] = hull.Vertices[index];
        }

        return new HullFaces(vertices, MergeFaces(hull, size, joinFlatness));
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

    /// <summary>Of the faces meeting at the corner farthest along <paramref name="direction"/>, the one turned most that
    /// way: the face a shape bears on, never one elsewhere turned a hair nearer.</summary>
    public int FindSupportingFace(Vector3F direction)
    {
        var farthest = FindFarthest(direction);
        var best = -1;
        var alignment = float.MinValue;
        for (var at = _facesAtStarts[farthest]; at < _facesAtStarts[farthest + 1]; at++)
        {
            var along = Vector3F.Dot(Normals[_facesAt[at]], direction);
            if (along > alignment)
            {
                alignment = along;
                best = _facesAt[at];
            }
        }

        // A corner of no face - its triangles of no area - leaves every face to tell.
        return best >= 0 ? best : FindFace(direction);
    }

    /// <summary>The corner farthest along <paramref name="direction"/>.</summary>
    public Vector3F GetSupport(Vector3F direction) => Vertices[FindFarthest(direction)];

    private int FindFarthest(Vector3F direction)
    {
        // The three coordinates as spans of one length, read in a row.
        ReadOnlySpan<float> cornerXs = _xs;
        var cornerYs = _ys.AsSpan(0, cornerXs.Length);
        var cornerZs = _zs.AsSpan(0, cornerXs.Length);
        var wayX = direction.X;
        var wayY = direction.Y;
        var wayZ = direction.Z;
        var farthest = 0;
        var reach = cornerXs[0] * wayX + cornerYs[0] * wayY + cornerZs[0] * wayZ;
        for (var index = 1; index < cornerXs.Length; index++)
        {
            var along = cornerXs[index] * wayX + cornerYs[index] * wayY + cornerZs[index] * wayZ;
            if (along > reach)
            {
                reach = along;
                farthest = index;
            }
        }

        return farthest;
    }

    // The hull's triangles grouped by the plane they lie in; each group is one face, its outline the two-dimensional
    // hull of its corners in that plane, wound counter-clockwise seen from outside.
    private static List<HullFace> MergeFaces(ConvexHull hull, float size, double joinFlatness)
    {
        var vertices = hull.Vertices;
        var triangles = hull.Triangles;
        var tolerance = Flatness * Math.Max(size, 1e-3f);
        var groups = new List<List<int>>();
        var groupNormals = new List<Vector3<double>>();
        var groupOffsets = new List<double>();
        for (var triangle = 0; triangle < triangles.Count; triangle += 3)
        {
            var first = vertices[triangles[triangle]].As<double>();
            var second = vertices[triangles[triangle + 1]].As<double>();
            var third = vertices[triangles[triangle + 2]].As<double>();
            var normal = Vector3<double>.Cross(second - first, third - first);
            var length = normal.Length();
            if (!(length > 0))
                continue;

            normal /= length;
            var offset = Vector3<double>.Dot(normal, first);
            var group = -1;
            for (var index = 0; index < groups.Count; index++)
            {
                var groupNormal = groupNormals[index];
                if (Vector3<double>.Dot(groupNormal, normal) > 0.9
                    && Math.Abs(Vector3<double>.Dot(groupNormal, first) - groupOffsets[index]) <= tolerance
                    && Math.Abs(Vector3<double>.Dot(groupNormal, second) - groupOffsets[index]) <= tolerance
                    && Math.Abs(Vector3<double>.Dot(groupNormal, third) - groupOffsets[index]) <= tolerance)
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

        if (joinFlatness > 0)
            JoinNearlyCoplanar(vertices, groups, groupNormals, joinFlatness * Math.Max(size, 1e-3f));

        var faces = new List<HullFace>(groups.Count);
        for (var index = 0; index < groups.Count; index++)
        {
            var group = groups[index];
            var normal = groupNormals[index];
            if (TryOutline(vertices, group, normal, out var outline))
            {
                faces.Add(new HullFace(outline, Distinct(group), normal.As<float>()));
                continue;
            }

            // Triangles not filling one convex face - near one plane on a curved hull - stay faces of their own, each
            // with its own normal, so no face reaches over another.
            for (var corner = 0; corner < group.Count; corner += 3)
            {
                var first = vertices[group[corner]].As<double>();
                var own = Vector3<double>.Normalize(Vector3<double>.Cross(vertices[group[corner + 1]].As<double>() - first,
                    vertices[group[corner + 2]].As<double>() - first));
                List<int> triangle = [group[corner], group[corner + 1], group[corner + 2]];
                faces.Add(new HullFace(triangle, triangle, own.As<float>()));
            }
        }

        return faces;
    }

    // Faces meeting at an edge whose corners all lie within tolerance of one plane become one face - one that a body
    // lying on them bears on whole - each join made only while it stays one convex face. Flattest pairs first.
    private static void JoinNearlyCoplanar(IReadOnlyList<Vector3F> vertices, List<List<int>> groups,
        List<Vector3<double>> normals, double tolerance)
    {
        var weighted = new Vector3<double>[groups.Count];
        var root = new int[groups.Count];
        var joined = new List<int>[groups.Count];
        var edgeOwners = new Dictionary<long, int>();
        var pairFirst = new List<int>();
        var pairSecond = new List<int>();
        for (var group = 0; group < groups.Count; group++)
        {
            root[group] = group;
            joined[group] = [.. groups[group]];
            var triangles = groups[group];
            for (var corner = 0; corner < triangles.Count; corner += 3)
            {
                var first = vertices[triangles[corner]].As<double>();
                weighted[group] += Vector3<double>.Cross(vertices[triangles[corner + 1]].As<double>() - first,
                    vertices[triangles[corner + 2]].As<double>() - first);
                for (var side = 0; side < 3; side++)
                {
                    var from = triangles[corner + side];
                    var to = triangles[corner + (side + 1) % 3];
                    var edge = ((long)Math.Min(from, to) << 32) | (uint)Math.Max(from, to);
                    if (!edgeOwners.TryGetValue(edge, out var owner))
                    {
                        edgeOwners[edge] = group;
                    }
                    else if (owner != group)
                    {
                        pairFirst.Add(owner);
                        pairSecond.Add(group);
                    }
                }
            }
        }

        var order = new int[pairFirst.Count];
        var keys = new double[pairFirst.Count];
        for (var pair = 0; pair < order.Length; pair++)
        {
            order[pair] = pair;
            keys[pair] = -Vector3<double>.Dot(Vector3<double>.Normalize(weighted[pairFirst[pair]]),
                Vector3<double>.Normalize(weighted[pairSecond[pair]]));
        }

        Array.Sort(keys, order);
        foreach (var pair in order)
        {
            var first = FindRoot(root, pairFirst[pair]);
            var second = FindRoot(root, pairSecond[pair]);
            if (first == second)
                continue;

            var normal = Vector3<double>.Normalize(weighted[first] + weighted[second]);
            List<int> union = [.. joined[first], .. joined[second]];
            if (!IsNearOnePlane(vertices, union, normal, tolerance) || !TryOutline(vertices, union, normal, out _))
                continue;

            root[second] = first;
            weighted[first] += weighted[second];
            joined[first] = union;
        }

        var joinedGroups = new List<List<int>>();
        var joinedNormals = new List<Vector3<double>>();
        for (var group = 0; group < groups.Count; group++)
        {
            if (root[group] != group)
                continue;

            var alone = joined[group].Count == groups[group].Count;
            joinedGroups.Add(joined[group]);
            joinedNormals.Add(alone ? normals[group] : Vector3<double>.Normalize(weighted[group]));
        }

        groups.Clear();
        groups.AddRange(joinedGroups);
        normals.Clear();
        normals.AddRange(joinedNormals);
    }

    private static int FindRoot(int[] root, int group)
    {
        while (root[group] != group)
        {
            root[group] = root[root[group]];
            group = root[group];
        }

        return group;
    }

    // Whether every corner of the triangles lies within tolerance of the farthest of them along normal.
    private static bool IsNearOnePlane(IReadOnlyList<Vector3F> vertices, List<int> triangles, Vector3<double> normal,
        double tolerance)
    {
        var highest = double.MinValue;
        var lowest = double.MaxValue;
        foreach (var corner in triangles)
        {
            var along = Vector3<double>.Dot(normal, vertices[corner].As<double>());
            highest = Math.Max(highest, along);
            lowest = Math.Min(lowest, along);
        }

        return highest - lowest <= tolerance;
    }

    // The outline of the triangles seen down the normal, when they fill it as one convex face does.
    private static bool TryOutline(IReadOnlyList<Vector3F> vertices, List<int> triangles, Vector3<double> normal,
        out List<int> outline)
    {
        outline = Outline(vertices, triangles, normal);
        var outlineArea = outline.Count >= 3 ? Area(vertices, outline, normal) : 0;
        return outline.Count >= 3
               && Math.Abs(outlineArea - TrianglesArea(vertices, triangles, normal)) <= 1e-4 * outlineArea;
    }

    private static List<int> Distinct(List<int> corners)
    {
        var met = new HashSet<int>();
        var distinct = new List<int>();
        foreach (var corner in corners)
        {
            if (met.Add(corner))
                distinct.Add(corner);
        }

        return distinct;
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
            var first = vertices[triangles[corner]].As<double>();
            var cross = Vector3<double>.Cross(vertices[triangles[corner + 1]].As<double>() - first,
                vertices[triangles[corner + 2]].As<double>() - first);
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
        var unique = Distinct(corners);
        unique.Sort((first, second) =>
        {
            var byAcross = Vector3<double>.Dot(vertices[first].As<double>(), across)
                .CompareTo(Vector3<double>.Dot(vertices[second].As<double>(), across));
            return byAcross != 0
                ? byAcross
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

    // How the path from start through middle to end turns, seen down the normal: more than zero counter-clockwise.
    private static double Turn(IReadOnlyList<Vector3F> vertices, int start, int middle, int end, Vector3<double> across,
        Vector3<double> up)
    {
        var startPoint = vertices[start].As<double>();
        var toMiddle = vertices[middle].As<double>() - startPoint;
        var toEnd = vertices[end].As<double>() - startPoint;
        return Vector3<double>.Dot(toMiddle, across) * Vector3<double>.Dot(toEnd, up)
               - Vector3<double>.Dot(toMiddle, up) * Vector3<double>.Dot(toEnd, across);
    }

    private static Vector3<double> AxisFor(Vector3<double> normal)
    {
        var other = Math.Abs(normal.X) < 0.57735 ? Vector3<double>.UnitX : Vector3<double>.UnitY;
        return Vector3<double>.Normalize(Vector3<double>.Cross(other, normal));
    }
}
