using System;
using System.Collections.Generic;
using Adamantium.DarkMatter.Dynamics;

namespace Adamantium.DarkMatter.Shapes;

/// <summary>The smallest convex volume holding a set of points, in the body's own axes: a rock, a crate's lid, a cab.
/// Its triangles lying in one plane are one face, so a hull resting on a face touches with all its corners. A hull of
/// more than <see cref="MaxCorners"/> corners keeps that many of them, as far out as they reach.</summary>
public sealed class ConvexHullCollisionShape : CollisionShape
{
    /// <summary>The most corners a hull keeps: each contact and query walks them.</summary>
    public const int MaxCorners = 256;

    private readonly Vector3F[] _vertices;
    private readonly Vector3F[] _faceNormals;
    private readonly float[] _faceOffsets;
    private readonly int[] _faceStarts;
    private readonly int[] _faceCorners;
    private readonly Vector3F _boundsCenter;
    private readonly Vector3F _boundsHalf;
    private readonly float _boundingRadius;

    /// <summary>The hull of <paramref name="points"/>, which must hold a volume: not all in one plane.</summary>
    public ConvexHullCollisionShape(IReadOnlyList<Vector3F> points)
    {
        ArgumentNullException.ThrowIfNull(points);
        foreach (var point in points)
        {
            if (!float.IsFinite(point.X) || !float.IsFinite(point.Y) || !float.IsFinite(point.Z))
                throw new ArgumentException("A hull's points must be finite.", nameof(points));
        }

        var hull = points.Count >= 4 ? ConvexHull.FromPoints(points) : null;
        if (hull != null && hull.Vertices.Count > MaxCorners)
            hull = ConvexHull.FromPoints(KeepFarthest(hull));

        if (hull == null || hull.IsFlat || hull.Triangles.Count < 12)
            throw new ArgumentException("A hull needs points that hold a volume, not all in one plane.", nameof(points));

        _vertices = [.. hull.Vertices];
        var minimum = new Vector3F(float.MaxValue);
        var maximum = new Vector3F(float.MinValue);
        foreach (var vertex in _vertices)
        {
            minimum = Vector3F.Min(minimum, vertex);
            maximum = Vector3F.Max(maximum, vertex);
            _boundingRadius = MathF.Max(_boundingRadius, vertex.Length());
        }

        _boundsCenter = (minimum + maximum) * 0.5f;
        _boundsHalf = (maximum - minimum) * 0.5f;
        if (Vector3F.Min(_boundsHalf) < MinimumSize / 2)
            throw new ArgumentException("A hull must be at least a micrometer across.", nameof(points));

        RequireFiniteReach(nameof(points));
        var faces = MergeFaces(hull, 2 * Vector3F.Max(_boundsHalf));
        _faceNormals = new Vector3F[faces.Count];
        _faceOffsets = new float[faces.Count];
        _faceStarts = new int[faces.Count + 1];
        var corners = new List<int>();
        for (var index = 0; index < faces.Count; index++)
        {
            _faceStarts[index] = corners.Count;
            corners.AddRange(faces[index].Corners);
            _faceNormals[index] = faces[index].Normal;

            // The plane through the corner reaching farthest, so it bounds the hull though its corners lie a hair apart.
            var offset = float.MinValue;
            foreach (var corner in faces[index].Corners)
            {
                offset = MathF.Max(offset, Vector3F.Dot(faces[index].Normal, _vertices[corner]));
            }

            _faceOffsets[index] = offset;
        }

        _faceStarts[faces.Count] = corners.Count;
        _faceCorners = [.. corners];
    }

    /// <summary>The corners of the hull.</summary>
    public IReadOnlyList<Vector3F> Vertices => _vertices;

    /// <summary>How many flat faces the hull has.</summary>
    public int FaceCount => _faceNormals.Length;

    /// <inheritdoc/>
    public override CollisionShapeKind Kind => CollisionShapeKind.ConvexHull;

    /// <inheritdoc/>
    public override float BoundingRadius => _boundingRadius;

    /// <inheritdoc/>
    public override MassProperties ComputeMass(float density)
    {
        // Tetrahedra from a point inside to each face's triangles: their volumes, middles and second moments
        // (Blow and Binstock, How to find the inertia tensor).
        var origin = Vector3<double>.Zero;
        foreach (var vertex in _vertices)
        {
            origin += vertex.As<double>();
        }

        origin /= _vertices.Length;
        var volume = 0.0;
        var moment = Vector3<double>.Zero;
        var covariance = default(Matrix3x3<double>);
        for (var face = 0; face < FaceCount; face++)
        {
            var first = _vertices[_faceCorners[_faceStarts[face]]].As<double>() - origin;
            for (var corner = _faceStarts[face] + 1; corner < _faceStarts[face + 1] - 1; corner++)
            {
                var second = _vertices[_faceCorners[corner]].As<double>() - origin;
                var third = _vertices[_faceCorners[corner + 1]].As<double>() - origin;
                var determinant = Vector3<double>.Dot(first, Vector3<double>.Cross(second, third));
                volume += determinant / 6;
                var sum = first + second + third;
                moment += sum * (determinant / 24);
                covariance += (Outer(first) + Outer(second) + Outer(third) + Outer(sum)) * (determinant / 120);
            }
        }

        var mass = density * volume;
        if (!(volume > 0))
            return new MassProperties(0, Vector3F.Zero);

        var center = moment / volume;
        var centered = covariance * density - Outer(center) * mass;
        var trace = centered.M11 + centered.M22 + centered.M33;
        var tensor = Matrix3x3<double>.Identity * trace - centered;
        InertiaMath.Diagonalize(tensor, out var moments, out var axes);
        return new MassProperties((float)mass, moments, (origin + center).As<float>(), axes);
    }

    /// <inheritdoc/>
    public override BoundingBox<float> ComputeBounds(in Pose pose)
    {
        var axisX = Vector3F.Abs(pose.TransformDirection(Vector3F.UnitX));
        var axisY = Vector3F.Abs(pose.TransformDirection(Vector3F.UnitY));
        var axisZ = Vector3F.Abs(pose.TransformDirection(Vector3F.UnitZ));
        var reach = axisX * _boundsHalf.X + axisY * _boundsHalf.Y + axisZ * _boundsHalf.Z;
        var center = pose.TransformPoint(_boundsCenter);
        return new BoundingBox<float>(center - reach, center + reach);
    }

    /// <inheritdoc/>
    public override Vector3F GetSupport(Vector3F direction)
    {
        var best = _vertices[0];
        var reach = Vector3F.Dot(best, direction);
        for (var index = 1; index < _vertices.Length; index++)
        {
            var along = Vector3F.Dot(_vertices[index], direction);
            if (along > reach)
            {
                reach = along;
                best = _vertices[index];
            }
        }

        return best;
    }

    internal override int FlatFaceCount => _faceNormals.Length;

    internal override Vector3F GetFlatFaceNormal(int face) => _faceNormals[face];

    internal override float GetFlatFaceOffset(int face) => _faceOffsets[face];

    /// <summary>The corners of the face; a face of more corners than <paramref name="into"/> holds gives them spread
    /// evenly round it, so what is kept still spans it.</summary>
    internal override int GetFlatFace(int face, Span<Vector3F> into)
    {
        var start = _faceStarts[face];
        var corners = _faceStarts[face + 1] - start;
        var count = Math.Min(corners, into.Length);
        for (var corner = 0; corner < count; corner++)
        {
            into[corner] = _vertices[_faceCorners[start + (int)((long)corner * corners / count)]];
        }

        return count;
    }

    internal override int FindFlatFace(Vector3F direction)
    {
        var best = 0;
        var alignment = float.MinValue;
        for (var face = 0; face < FaceCount; face++)
        {
            var along = Vector3F.Dot(_faceNormals[face], direction);
            if (along > alignment)
            {
                alignment = along;
                best = face;
            }
        }

        return best;
    }

    internal override int GetSupportingFace(Vector3F direction, Span<Vector3F> into) =>
        GetFlatFace(FindFlatFace(direction), into);

    /// <inheritdoc/>
    public override bool CastRay(in Ray<float> ray, float maxDistance, out float distance, out Vector3F normal)
    {
        var enter = 0f;
        var leave = maxDistance;
        normal = -ray.Direction;
        var inside = true;
        for (var face = 0; face < FaceCount; face++)
        {
            var facing = _faceNormals[face];
            var height = Vector3F.Dot(facing, ray.Position) - _faceOffsets[face];
            var closing = Vector3F.Dot(facing, ray.Direction);
            if (height > 0)
                inside = false;

            if (closing == 0)
            {
                if (height > 0)
                {
                    distance = 0;
                    return false;
                }

                continue;
            }

            // On or outside this face and heading away from it: the ray never comes in.
            if (height >= 0 && closing > 0)
            {
                distance = 0;
                return false;
            }

            var crossing = -height / closing;
            if (closing < 0)
            {
                if (crossing > enter)
                {
                    enter = crossing;
                    normal = facing;
                }
            }
            else if (crossing < leave)
            {
                leave = crossing;
            }

            if (enter > leave)
            {
                distance = 0;
                return false;
            }
        }

        if (inside)
        {
            distance = 0;
            normal = -ray.Direction;
            return true;
        }

        distance = enter;
        return true;
    }

    private static Matrix3x3<double> Outer(Vector3<double> vector) =>
        new(vector * vector.X, vector * vector.Y, vector * vector.Z);

    // The corners farthest along the six axes, then along directions spread ever more finely over the whole sphere:
    // all of a coarser spread are kept, and of the next finer one as many more as MaxCorners allows, so the kept ones
    // reach every way the whole hull does.
    private static List<Vector3F> KeepFarthest(ConvexHull hull)
    {
        var kept = new List<Vector3F>(MaxCorners);
        var keptSet = new HashSet<Vector3F>();
        ReadOnlySpan<int> signs = [1, -1];
        for (var axis = 0; axis < 3; axis++)
        {
            foreach (var sign in signs)
            {
                var direction = axis switch { 0 => Vector3F.UnitX, 1 => Vector3F.UnitY, _ => Vector3F.UnitZ } * sign;
                var corner = hull.Support(direction);
                if (keptSet.Add(corner))
                    kept.Add(corner);
            }
        }

        var met = new List<Vector3F>();
        var metSet = new HashSet<Vector3F>();
        var golden = MathF.PI * (3 - MathF.Sqrt(5));
        for (var directions = MaxCorners; directions <= 16 * MaxCorners && kept.Count < MaxCorners; directions *= 2)
        {
            met.Clear();
            metSet.Clear();
            for (var index = 0; index < directions; index++)
            {
                var height = 1 - 2 * (index + 0.5f) / directions;
                var across = MathF.Sqrt(1 - height * height);
                var angle = golden * index;
                var corner = hull.Support(new Vector3F(across * MathF.Cos(angle), height, across * MathF.Sin(angle)));
                if (!keptSet.Contains(corner) && metSet.Add(corner))
                    met.Add(corner);
            }

            // The whole spread fits: kept, and a finer one tried; it does not: every so many of it fill what is left.
            if (kept.Count + met.Count <= MaxCorners)
            {
                foreach (var corner in met)
                {
                    keptSet.Add(corner);
                    kept.Add(corner);
                }

                continue;
            }

            var room = MaxCorners - kept.Count;
            for (var index = 0; index < room; index++)
            {
                kept.Add(met[(int)((long)index * met.Count / room)]);
            }
        }

        return kept;
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
