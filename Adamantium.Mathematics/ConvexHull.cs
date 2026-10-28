using System;
using System.Collections.Generic;

namespace Adamantium.Mathematics;

/// <summary>
/// The smallest convex volume holding a set of points: its corners, its triangles wound counter-clockwise seen from
/// outside, and the outward planes that bound it. A flat set gives a hull of no thickness, closed at the sides by planes
/// through its outline; a set on one line or at one point gives corners only, which a ray never meets.
/// </summary>
public sealed class ConvexHull : IConvexShape<float>
{
    private const float Tolerance = 1e-5f;

    private readonly Vector3F[] vertices;
    private readonly int[] triangles;
    private readonly int[] outline;
    private readonly Plane<float>[] planes;
    private readonly float scale;
    private readonly float tolerance;

    private ConvexHull(Vector3F[] vertices, int[] triangles, int[] outline)
    {
        this.vertices = vertices;
        this.triangles = triangles;
        this.outline = outline;
        scale = Math.Max(Size(vertices), 1e-6f);
        tolerance = Tolerance * scale;
        planes = BuildPlanes();
    }

    public IReadOnlyList<Vector3F> Vertices => vertices;

    /// <summary>Index triples into <see cref="Vertices"/>; a flat hull lists both of its sides.</summary>
    public IReadOnlyList<int> Triangles => triangles;

    /// <summary>Planes whose normals point out of the hull: a point is inside when it is behind all of them.</summary>
    public IReadOnlyList<Plane<float>> Planes => planes;

    public bool IsFlat => outline != null;

    /// <summary>Builds the hull by Quickhull: from the widest tetrahedron, always adding the farthest point outside.</summary>
    public static ConvexHull FromPoints(IReadOnlyList<Vector3F> points)
    {
        if (points == null || points.Count == 0)
        {
            throw new ArgumentException("A hull needs at least one point.", nameof(points));
        }

        var tolerance = Tolerance * Math.Max(Size(points), 1e-6f);
        if (!Extremes(points, tolerance, out var first, out var second, out var third, out var fourth))
        {
            return Degenerate(points, first, second);
        }

        if (fourth < 0)
        {
            return Flat(points, first, second, third);
        }

        return new SolidBuilder(points, tolerance).Build(first, second, third, fourth);
    }

    /// <summary>The hull carried through an affine matrix; a mirroring matrix keeps its faces turned outward.</summary>
    public ConvexHull Transform(Matrix4x4F matrix)
    {
        var moved = new Vector3F[vertices.Length];
        for (int i = 0; i < vertices.Length; i++)
        {
            moved[i] = Vector3F.TransformCoordinate(vertices[i], matrix);
        }

        if (Vector3F.Dot(Vector3F.Cross(matrix.Right, matrix.Up), matrix.Backward) >= 0)
        {
            return new ConvexHull(moved, triangles, outline);
        }

        var turned = (int[])triangles.Clone();
        for (int i = 0; i < turned.Length; i += 3)
        {
            (turned[i + 1], turned[i + 2]) = (turned[i + 2], turned[i + 1]);
        }

        return new ConvexHull(moved, turned, outline);
    }

    public Vector3F Support(Vector3F direction)
    {
        var best = vertices[0];
        var reach = Vector3F.Dot(best, direction);
        for (int i = 1; i < vertices.Length; i++)
        {
            var along = Vector3F.Dot(vertices[i], direction);
            if (along > reach)
            {
                reach = along;
                best = vertices[i];
            }
        }

        return best;
    }

    public bool Contains(Vector3F point)
    {
        if (planes.Length == 0)
        {
            return false;
        }

        foreach (var plane in planes)
        {
            if (Plane<float>.DotCoordinate(plane, point) > tolerance)
            {
                return false;
            }
        }

        return true;
    }

    /// <summary>Where a ray first meets the hull, in lengths of its direction; a ray starting inside meets it at 0.</summary>
    public bool Intersects(Ray<float> ray, out float distance)
    {
        distance = 0;
        if (planes.Length == 0)
        {
            return false;
        }

        var near = float.MinValue;
        var far = float.MaxValue;
        foreach (var plane in planes)
        {
            var toward = Vector3F.Dot(plane.Normal, ray.Direction);
            var outside = Plane<float>.DotCoordinate(plane, ray.Position);
            if (Math.Abs(toward) < 1e-12f)
            {
                if (outside > tolerance)
                {
                    return false;
                }

                continue;
            }

            var t = -outside / toward;
            if (toward < 0)
            {
                near = Math.Max(near, t);
            }
            else
            {
                far = Math.Min(far, t);
            }

            if (near > far + tolerance || far < 0)
            {
                return false;
            }
        }

        distance = Math.Max(near, 0);
        return true;
    }

    public bool Intersects(Ray<float> ray, out Vector3F point)
    {
        if (!Intersects(ray, out float distance))
        {
            point = Vector3F.Zero;
            return false;
        }

        point = ray.Position + ray.Direction * distance;
        return true;
    }

    public bool Intersects(BoundingBox<float> box)
    {
        return Gjk.Intersects(this, box);
    }

    public bool Intersects(BoundingSphere<float> sphere)
    {
        return Gjk.Intersects(this, sphere);
    }

    public bool Intersects(OrientedBoundingBox<float> box)
    {
        return Gjk.Intersects(this, box);
    }

    public bool Intersects(BoundingCapsule capsule)
    {
        return Gjk.Intersects(this, capsule);
    }

    public bool Intersects(ConvexHull hull)
    {
        return Gjk.Intersects(this, hull);
    }

    /// <summary>On which side of a plane the hull lies, or whether the plane cuts it.</summary>
    public PlaneIntersectionType Intersects(Plane<float> plane)
    {
        var front = false;
        var back = false;
        foreach (var vertex in vertices)
        {
            var side = Plane<float>.DotCoordinate(plane, vertex);
            front |= side > 0;
            back |= side < 0;
        }

        return front && !back ? PlaneIntersectionType.Front
            : back && !front ? PlaneIntersectionType.Back
            : PlaneIntersectionType.Intersecting;
    }

    public BoundingBox<float> GetBoundingBox()
    {
        return BoundingBox.CreateFromPoints<float>(vertices);
    }

    private Plane<float>[] BuildPlanes()
    {
        if (outline != null)
        {
            var origin = vertices[outline[0]].As<double>();
            var normal = Vector3<double>.Normalize(Vector3<double>.Cross(vertices[outline[1]].As<double>() - origin, vertices[outline[2]].As<double>() - origin));
            List<Plane<float>> sides = [Through(origin, normal), Through(origin, -normal)];
            for (int i = 0; i < outline.Length; i++)
            {
                var from = vertices[outline[i]].As<double>();
                var to = vertices[outline[(i + 1) % outline.Length]].As<double>();
                sides.Add(Through(from, Vector3<double>.Normalize(Vector3<double>.Cross(to - from, normal))));
            }

            return sides.ToArray();
        }

        double smallest = tolerance * scale;
        List<Plane<float>> faces = [];
        for (int i = 0; i < triangles.Length; i += 3)
        {
            var a = vertices[triangles[i]].As<double>();
            var cross = Vector3<double>.Cross(vertices[triangles[i + 1]].As<double>() - a, vertices[triangles[i + 2]].As<double>() - a);
            var length = cross.Length();
            if (length > smallest)
            {
                faces.Add(Through(a, cross / length));
            }
        }

        return faces.ToArray();
    }

    private static Plane<float> Through(Vector3<double> point, Vector3<double> normal)
    {
        return new Plane<float>(normal.As<float>(), (float)-Vector3<double>.Dot(normal, point));
    }

    private static Vector3F Normal(Vector3F a, Vector3F b, Vector3F c)
    {
        return Vector3F.Normalize(Vector3F.Cross(b - a, c - a));
    }

    private static float Size(IReadOnlyList<Vector3F> points)
    {
        var min = points[0];
        var max = points[0];
        for (int i = 1; i < points.Count; i++)
        {
            min = Vector3F.Min(min, points[i]);
            max = Vector3F.Max(max, points[i]);
        }

        var extent = max - min;
        return Math.Max(extent.X, Math.Max(extent.Y, extent.Z));
    }

    private static bool Extremes(IReadOnlyList<Vector3F> points, float tolerance, out int first, out int second, out int third, out int fourth)
    {
        first = 0;
        second = 0;
        third = -1;
        fourth = -1;

        var bestSpread = -1f;
        for (int axis = 0; axis < 3; axis++)
        {
            var low = 0;
            var high = 0;
            for (int i = 1; i < points.Count; i++)
            {
                if (points[i][axis] < points[low][axis])
                {
                    low = i;
                }

                if (points[i][axis] > points[high][axis])
                {
                    high = i;
                }
            }

            var spread = points[high][axis] - points[low][axis];
            if (spread > bestSpread)
            {
                bestSpread = spread;
                first = low;
                second = high;
            }
        }

        if (Vector3F.Distance(points[first], points[second]) <= tolerance)
        {
            second = first;
            return false;
        }

        var line = Vector3F.Normalize(points[second] - points[first]);
        var farthest = tolerance;
        for (int i = 0; i < points.Count; i++)
        {
            var offset = points[i] - points[first];
            var away = (offset - line * Vector3F.Dot(offset, line)).Length();
            if (away > farthest)
            {
                farthest = away;
                third = i;
            }
        }

        if (third < 0)
        {
            return false;
        }

        var normal = Normal(points[first], points[second], points[third]);
        farthest = tolerance;
        for (int i = 0; i < points.Count; i++)
        {
            var away = Math.Abs(Vector3F.Dot(points[i] - points[first], normal));
            if (away > farthest)
            {
                farthest = away;
                fourth = i;
            }
        }

        return true;
    }

    private static ConvexHull Degenerate(IReadOnlyList<Vector3F> points, int first, int second)
    {
        Vector3F[] corners = first == second ? [points[first]] : [points[first], points[second]];
        return new ConvexHull(corners, [], null);
    }

    private static ConvexHull Flat(IReadOnlyList<Vector3F> points, int first, int second, int third)
    {
        var origin = points[first];
        var normal = Normal(points[first], points[second], points[third]);
        var u = Vector3F.Normalize(points[second] - origin);
        var v = Vector3F.Cross(normal, u);

        var projected = new Projected[points.Count];
        for (int i = 0; i < points.Count; i++)
        {
            var offset = points[i] - origin;
            projected[i] = new Projected(i, Vector3F.Dot(offset, u), Vector3F.Dot(offset, v));
        }

        Array.Sort(projected, Projected.Compare);
        var chain = new Projected[projected.Length * 2];
        var count = 0;
        for (int i = 0; i < projected.Length; i++)
        {
            while (count >= 2 && Turn(chain[count - 2], chain[count - 1], projected[i]) <= 0)
            {
                count--;
            }

            chain[count++] = projected[i];
        }

        var lower = count + 1;
        for (int i = projected.Length - 2; i >= 0; i--)
        {
            while (count >= lower && Turn(chain[count - 2], chain[count - 1], projected[i]) <= 0)
            {
                count--;
            }

            chain[count++] = projected[i];
        }

        count--;
        if (count < 3)
        {
            return Degenerate(points, first, second);
        }

        var corners = new Vector3F[count];
        var loop = new int[count];
        for (int i = 0; i < count; i++)
        {
            corners[i] = points[chain[i].Index];
            loop[i] = i;
        }

        var sides = new int[(count - 2) * 6];
        for (int i = 1, at = 0; i + 1 < count; i++)
        {
            sides[at++] = 0;
            sides[at++] = i;
            sides[at++] = i + 1;
            sides[at++] = 0;
            sides[at++] = i + 1;
            sides[at++] = i;
        }

        return new ConvexHull(corners, sides, loop);
    }

    private static float Turn(Projected a, Projected b, Projected c)
    {
        return (b.X - a.X) * (c.Y - a.Y) - (b.Y - a.Y) * (c.X - a.X);
    }

    private readonly struct Projected
    {
        public Projected(int index, float x, float y)
        {
            Index = index;
            X = x;
            Y = y;
        }

        public int Index { get; }

        public float X { get; }

        public float Y { get; }

        public static int Compare(Projected a, Projected b)
        {
            return a.X != b.X ? a.X.CompareTo(b.X) : a.Y.CompareTo(b.Y);
        }
    }

    private sealed class SolidBuilder
    {
        private static readonly Predicate<Face> Dead = face => !face.Alive;

        private readonly IReadOnlyList<Vector3F> input;
        private readonly Vector3<double>[] points;
        private readonly double tolerance;
        private readonly List<Face> faces = [];
        private readonly List<HalfEdge> horizon = [];
        private readonly List<Face> visible = [];
        private readonly List<Face> created = [];
        private readonly List<int> orphans = [];

        public SolidBuilder(IReadOnlyList<Vector3F> input, float tolerance)
        {
            this.input = input;
            this.tolerance = tolerance;
            points = new Vector3<double>[input.Count];
            for (int i = 0; i < points.Length; i++)
            {
                points[i] = input[i].As<double>();
            }
        }

        public ConvexHull Build(int first, int second, int third, int fourth)
        {
            Tetrahedron(first, second, third, fourth);
            for (int i = 0; i < points.Length; i++)
            {
                if (i != first && i != second && i != third && i != fourth)
                {
                    Claim(i, faces);
                }
            }

            while (NextEye(out var face, out var eye))
            {
                Grow(face, eye);
            }

            return Result();
        }

        private void Tetrahedron(int first, int second, int third, int fourth)
        {
            var center = (points[first] + points[second] + points[third] + points[fourth]) * 0.25;
            faces.Add(Outward(first, second, third, center));
            faces.Add(Outward(first, second, fourth, center));
            faces.Add(Outward(first, third, fourth, center));
            faces.Add(Outward(second, third, fourth, center));

            var edges = new Dictionary<(int, int), HalfEdge>();
            foreach (var face in faces)
            {
                var edge = face.Edge;
                do
                {
                    edges[(edge.Origin, edge.Next.Origin)] = edge;
                    edge = edge.Next;
                }
                while (edge != face.Edge);
            }

            foreach (var edge in edges.Values)
            {
                edge.Twin = edges[(edge.Next.Origin, edge.Origin)];
            }
        }

        private Face Outward(int a, int b, int c, Vector3<double>inside)
        {
            var face = new Face(points, a, b, c, null);
            return face.Distance(inside) > 0 ? new Face(points, a, c, b, null) : face;
        }

        private void Claim(int index, List<Face> candidates)
        {
            Face best = null;
            var reach = tolerance;
            foreach (var face in candidates)
            {
                var distance = face.Distance(points[index]);
                if (distance > reach)
                {
                    reach = distance;
                    best = face;
                }
            }

            best?.Claim(index, reach);
        }

        private bool NextEye(out Face face, out int eye)
        {
            foreach (var candidate in faces)
            {
                if (candidate.Outside.Count > 0)
                {
                    face = candidate;
                    eye = candidate.Farthest;
                    return true;
                }
            }

            face = null;
            eye = -1;
            return false;
        }

        private void Grow(Face face, int eye)
        {
            horizon.Clear();
            visible.Clear();
            Visit(face, null, points[eye]);

            if (horizon.Count < 3)
            {
                foreach (var seen in visible)
                {
                    seen.Visible = false;
                }

                face.Forget(eye);
                return;
            }

            Cone(eye);

            orphans.Clear();
            foreach (var gone in visible)
            {
                foreach (var index in gone.Outside)
                {
                    if (index != eye)
                    {
                        orphans.Add(index);
                    }
                }

                gone.Alive = false;
            }

            faces.RemoveAll(Dead);
            foreach (var orphan in orphans)
            {
                Claim(orphan, created);
            }
        }

        private void Visit(Face face, HalfEdge entry, Vector3<double>eye)
        {
            face.Visible = true;
            visible.Add(face);
            var stop = entry ?? face.Edge;
            var edge = entry == null ? face.Edge : entry.Next;
            do
            {
                var neighbor = edge.Twin.Face;
                if (!neighbor.Visible)
                {
                    if (neighbor.Distance(eye) > 0)
                    {
                        Visit(neighbor, edge.Twin, eye);
                    }
                    else
                    {
                        horizon.Add(edge);
                    }
                }

                edge = edge.Next;
            }
            while (edge != stop);
        }

        private void Cone(int eye)
        {
            created.Clear();
            Face first = null;
            Face previous = null;
            foreach (var edge in horizon)
            {
                var outside = edge.Twin;
                var face = new Face(points, edge.Origin, edge.Next.Origin, eye, outside.Face);
                face.Edge.Twin = outside;
                outside.Twin = face.Edge;
                if (previous == null)
                {
                    first = face;
                }
                else
                {
                    Pair(previous.Edge.Next, face.Edge.Next.Next);
                }

                previous = face;
                created.Add(face);
                faces.Add(face);
            }

            Pair(previous.Edge.Next, first.Edge.Next.Next);
        }

        private static void Pair(HalfEdge toEye, HalfEdge fromEye)
        {
            toEye.Twin = fromEye;
            fromEye.Twin = toEye;
        }

        private ConvexHull Result()
        {
            var remap = new Dictionary<int, int>();
            List<Vector3F> corners = [];
            var indices = new int[faces.Count * 3];
            var at = 0;
            foreach (var face in faces)
            {
                var edge = face.Edge;
                for (int k = 0; k < 3; k++)
                {
                    if (!remap.TryGetValue(edge.Origin, out var mapped))
                    {
                        mapped = corners.Count;
                        remap[edge.Origin] = mapped;
                        corners.Add(input[edge.Origin]);
                    }

                    indices[at++] = mapped;
                    edge = edge.Next;
                }
            }

            return new ConvexHull(corners.ToArray(), indices, null);
        }
    }

    private sealed class HalfEdge
    {
        public HalfEdge(int origin, Face face)
        {
            Origin = origin;
            Face = face;
        }

        public int Origin { get; }

        public Face Face { get; }

        public HalfEdge Next { get; set; }

        public HalfEdge Twin { get; set; }
    }

    private sealed class Face
    {
        private readonly Vector3<double>normal;
        private readonly double offset;
        private double farthestDistance;

        public Face(Vector3<double>[] points, int a, int b, int c, Face fallback)
        {
            var first = new HalfEdge(a, this);
            var second = new HalfEdge(b, this);
            var third = new HalfEdge(c, this);
            first.Next = second;
            second.Next = third;
            third.Next = first;
            Edge = first;

            var ab = points[b] - points[a];
            var ac = points[c] - points[a];
            var cross = Vector3<double>.Cross(ab, ac);
            var length = cross.Length();
            normal = length > 1e-9 * (ab.LengthSquared() + ac.LengthSquared()) || fallback == null
                ? cross / Math.Max(length, double.Epsilon)
                : fallback.normal;
            offset = Vector3<double>.Dot(normal, points[a]);
        }

        public HalfEdge Edge { get; }

        public List<int> Outside { get; } = [];

        public int Farthest { get; private set; } = -1;

        public bool Visible { get; set; }

        public bool Alive { get; set; } = true;

        public double Distance(Vector3<double>point)
        {
            return Vector3<double>.Dot(normal, point) - offset;
        }

        public void Claim(int index, double distance)
        {
            Outside.Add(index);
            if (Farthest < 0 || distance > farthestDistance)
            {
                Farthest = index;
                farthestDistance = distance;
            }
        }

        public void Forget(int index)
        {
            Outside.Remove(index);
            Farthest = -1;
            farthestDistance = 0;
            foreach (var kept in Outside)
            {
                Farthest = kept;
                break;
            }
        }
    }
}
