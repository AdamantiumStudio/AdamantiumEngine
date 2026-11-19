using System;
using System.Collections.Generic;

namespace Adamantium.DarkMatter.Shapes;

/// <summary>A surface of triangles: a level, a building, a rock face. Corners that stand at one place are one, so the
/// edges a body can catch on are told from those inside the surface; a body meets each triangle near it on its own.
/// </summary>
public sealed class TriangleMeshCollisionShape : SurfaceCollisionShape
{
    private readonly Vector3F[] _vertices;
    private readonly int[] _corners;
    private readonly Vector3F[] _normals;
    private readonly byte[] _activeEdges;
    private readonly TriangleTree _tree;
    private readonly float _boundingRadius;

    /// <summary>The triangles of <paramref name="vertices"/> listed three by three in <paramref name="indices"/>, each
    /// wound counterclockwise seen from the side its normal points to; triangles with no area are left out.</summary>
    public TriangleMeshCollisionShape(IReadOnlyList<Vector3F> vertices, IReadOnlyList<int> indices)
    {
        ArgumentNullException.ThrowIfNull(vertices);
        ArgumentNullException.ThrowIfNull(indices);
        if (indices.Count % 3 != 0)
            throw new ArgumentException("Indices come three to a triangle.", nameof(indices));

        var welded = new Dictionary<Vector3F, int>();
        var weldedOf = new int[vertices.Count];
        var kept = new List<Vector3F>();
        for (var index = 0; index < vertices.Count; index++)
        {
            // A vertex that is not finite is refused only once a triangle stands on it.
            var vertex = vertices[index];
            if (!float.IsFinite(vertex.X) || !float.IsFinite(vertex.Y) || !float.IsFinite(vertex.Z))
            {
                weldedOf[index] = -1;
                continue;
            }

            if (!welded.TryGetValue(vertex, out var at))
            {
                at = kept.Count;
                welded.Add(vertex, at);
                kept.Add(vertex);
                _boundingRadius = MathF.Max(_boundingRadius, vertex.Length());
            }

            weldedOf[index] = at;
        }

        _vertices = [.. kept];
        var corners = new List<int>(indices.Count);
        var normals = new List<Vector3F>(indices.Count / 3);
        var seen = new HashSet<MeshTriangle>(indices.Count / 3);
        for (var index = 0; index < indices.Count; index += 3)
        {
            var first = Corner(indices[index], vertices.Count, weldedOf);
            var second = Corner(indices[index + 1], vertices.Count, weldedOf);
            var third = Corner(indices[index + 2], vertices.Count, weldedOf);
            var normal = NormalOf(_vertices[first], _vertices[second], _vertices[third]);
            if (first == second || second == third || third == first || normal == Vector3F.Zero)
                continue;

            // A side met from both sides already, a double-sided mesh's back is the triangle once more: kept once,
            // its edges stay inside the surface.
            if (!seen.Add(MeshTriangle.Of(first, second, third)))
                continue;

            corners.Add(first);
            corners.Add(second);
            corners.Add(third);
            normals.Add(normal);
        }

        if (normals.Count == 0)
            throw new ArgumentException("A mesh needs at least one triangle with an area.", nameof(indices));

        _corners = [.. corners];
        _normals = [.. normals];
        RequireFiniteReach(nameof(vertices));
        _activeEdges = FindActiveEdges();
        _tree = new TriangleTree(_vertices, _corners);
    }

    /// <summary>The corners, those that stood at one place made one.</summary>
    public IReadOnlyList<Vector3F> Vertices => _vertices;

    public override CollisionShapeKind Kind => CollisionShapeKind.TriangleMesh;

    public override float BoundingRadius => _boundingRadius;

    internal override int TriangleCount => _normals.Length;

    public override BoundingBox<float> ComputeBounds(in Pose pose) =>
        PlaceBox(_tree.Bounds.Minimum, _tree.Bounds.Maximum, pose);

    /// <summary>The corner farthest along <paramref name="direction"/>.</summary>
    public override Vector3F GetSupport(Vector3F direction)
    {
        var best = _vertices[0];
        var bestDistance = Vector3F.Dot(best, direction);
        for (var index = 1; index < _vertices.Length; index++)
        {
            var distance = Vector3F.Dot(_vertices[index], direction);
            if (distance > bestDistance)
            {
                bestDistance = distance;
                best = _vertices[index];
            }
        }

        return best;
    }

    public override bool CastRay(in Ray<float> ray, float maxDistance, out float distance, out Vector3F normal)
    {
        normal = default;
        if (!_tree.Intersect(ray, maxDistance, out distance, out var triangle))
            return false;

        normal = Vector3F.Dot(ray.Direction, _normals[triangle]) <= 0 ? _normals[triangle] : -_normals[triangle];
        return true;
    }

    internal override void FindTriangles(in BoundingBox<float> box, List<int> into) => _tree.Query(box, into);

    internal override bool TryGetTriangle(int triangle, out Vector3F first, out Vector3F second, out Vector3F third)
    {
        first = _vertices[_corners[3 * triangle]];
        second = _vertices[_corners[3 * triangle + 1]];
        third = _vertices[_corners[3 * triangle + 2]];
        return true;
    }

    internal override int GetActiveEdges(int triangle) => _activeEdges[triangle];

    internal override Vector3F GetTriangleNormal(int triangle, Vector3F first, Vector3F second, Vector3F third) =>
        _normals[triangle];

    private static int Corner(int index, int vertexCount, int[] weldedOf)
    {
        if ((uint)index >= (uint)vertexCount)
            throw new ArgumentException("An index points past the vertices.", "indices");

        if (weldedOf[index] < 0)
            throw new ArgumentException("A triangle stands on a vertex that is not finite.", "vertices");

        return weldedOf[index];
    }

    // An edge one triangle has alone, or more than two share, is the surface's rim: a body catches on it. One two
    // share is as convex and as sharp as the turn between them.
    private byte[] FindActiveEdges()
    {
        var active = new byte[_normals.Length];
        var firstOwners = new Dictionary<MeshEdge, int>(_corners.Length);
        var owners = new int[_corners.Length];
        Array.Fill(owners, -1);
        for (var edge = 0; edge < _corners.Length; edge++)
        {
            var key = EdgeAt(edge);
            if (!firstOwners.TryGetValue(key, out var other))
            {
                firstOwners.Add(key, edge);
                continue;
            }

            // A third triangle on the edge makes it a rim for all of them.
            if (owners[other] >= 0 || owners[other] == -2)
            {
                owners[edge] = -2;
                if (owners[other] >= 0)
                    owners[owners[other]] = -2;

                owners[other] = -2;
                continue;
            }

            owners[other] = edge;
            owners[edge] = other;
        }

        for (var edge = 0; edge < _corners.Length; edge++)
        {
            var triangle = edge / 3;
            var other = owners[edge];
            var isActive = other < 0
                           || IsEdgeActive(_normals[triangle], _normals[other / 3],
                               _vertices[_corners[NextCorner(edge)]] - _vertices[_corners[edge]]);
            if (isActive)
                active[triangle] |= (byte)(1 << (edge % 3));
        }

        return active;
    }

    private MeshEdge EdgeAt(int edge)
    {
        var start = _corners[edge];
        var end = _corners[NextCorner(edge)];
        return start < end ? new MeshEdge(start, end) : new MeshEdge(end, start);
    }

    private static int NextCorner(int edge) => edge % 3 == 2 ? edge - 2 : edge + 1;
}
