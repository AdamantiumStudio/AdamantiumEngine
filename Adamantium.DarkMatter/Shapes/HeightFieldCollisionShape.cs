using System;
using System.Collections.Generic;

namespace Adamantium.DarkMatter.Shapes;

/// <summary>Ground: heights over a grid, centered on the body, columns along X and rows along Z. Each cell is two
/// triangles split from its first corner to its last; a height that is not a number is a hole, and the triangles on it
/// are not there.</summary>
public sealed class HeightFieldCollisionShape : SurfaceCollisionShape
{
    private readonly float[] _heights;
    private readonly byte[] _activeEdges;
    private readonly float _originX;
    private readonly float _originZ;
    private readonly float _lowest;
    private readonly float _highest;
    private readonly float _boundingRadius;

    /// <summary>A grid of <paramref name="columnCount"/> by <paramref name="rowCount"/> samples, at least two each way,
    /// <paramref name="spacingX"/> and <paramref name="spacingZ"/> apart; sample (column, row) stands at the Y
    /// <paramref name="heights"/> holds at row × columns + column.</summary>
    public HeightFieldCollisionShape(int columnCount, int rowCount, IReadOnlyList<float> heights, float spacingX,
        float spacingZ)
    {
        ArgumentNullException.ThrowIfNull(heights);
        ArgumentOutOfRangeException.ThrowIfLessThan(columnCount, 2);
        ArgumentOutOfRangeException.ThrowIfLessThan(rowCount, 2);
        if ((long)columnCount * rowCount != heights.Count)
            throw new ArgumentException("There must be a height for every sample.", nameof(heights));

        ColumnCount = columnCount;
        RowCount = rowCount;
        SpacingX = RequireSize(spacingX, nameof(spacingX));
        SpacingZ = RequireSize(spacingZ, nameof(spacingZ));
        _originX = -(columnCount - 1) * spacingX / 2;
        _originZ = -(rowCount - 1) * spacingZ / 2;
        _heights = new float[heights.Count];
        _lowest = float.MaxValue;
        _highest = float.MinValue;
        for (var index = 0; index < _heights.Length; index++)
        {
            var height = heights[index];
            if (float.IsInfinity(height))
                throw new ArgumentException("A height is finite, or not a number for a hole.", nameof(heights));

            _heights[index] = height;
            if (!float.IsNaN(height))
            {
                _lowest = MathF.Min(_lowest, height);
                _highest = MathF.Max(_highest, height);
            }
        }

        _activeEdges = new byte[TriangleCount];
        var anyTriangle = false;
        for (var triangle = 0; triangle < TriangleCount; triangle++)
        {
            if (!TryGetTriangle(triangle, out _, out _, out _))
                continue;

            anyTriangle = true;
            _activeEdges[triangle] = FindActiveEdges(triangle);
        }

        if (!anyTriangle)
            throw new ArgumentException("A height field needs at least one triangle that is not a hole.",
                nameof(heights));

        var corner = new Vector3F(-_originX, MathF.Max(MathF.Abs(_lowest), MathF.Abs(_highest)), -_originZ);
        _boundingRadius = corner.Length();
        RequireFiniteReach(nameof(heights));
    }

    public int ColumnCount { get; }

    public int RowCount { get; }

    public float SpacingX { get; }

    public float SpacingZ { get; }

    /// <summary>The height of sample (<paramref name="column"/>, <paramref name="row"/>); not a number for a hole.
    /// </summary>
    public float GetHeight(int column, int row)
    {
        ArgumentOutOfRangeException.ThrowIfNegative(column);
        ArgumentOutOfRangeException.ThrowIfGreaterThanOrEqual(column, ColumnCount);
        ArgumentOutOfRangeException.ThrowIfNegative(row);
        ArgumentOutOfRangeException.ThrowIfGreaterThanOrEqual(row, RowCount);
        return _heights[row * ColumnCount + column];
    }

    public override CollisionShapeKind Kind => CollisionShapeKind.HeightField;

    public override float BoundingRadius => _boundingRadius;

    internal override int TriangleCount => 2 * (ColumnCount - 1) * (RowCount - 1);

    internal override BoundingBox<float> ComputeBounds(in LocalPose pose) => PlaceBox(BoxCorner(0), BoxCorner(7), pose);

    /// <summary>The corner of the box round the heights farthest along <paramref name="direction"/>.</summary>
    public override Vector3F GetSupport(Vector3F direction) =>
        new(direction.X >= 0 ? -_originX : _originX, direction.Y >= 0 ? _highest : _lowest,
            direction.Z >= 0 ? -_originZ : _originZ);

    // Walks the cells under the ray in the order it crosses them; the first cell one of whose triangles it meets holds
    // the nearest, the point met lying over that cell.
    public override bool CastRay(in Ray<float> ray, float maxDistance, out float distance, out Vector3F normal)
    {
        distance = 0;
        normal = default;
        var box = new BoundingBox<float>(BoxCorner(0), BoxCorner(7));
        if (!Collision.RayIntersectsBox(ray, box, out float entry) || entry > maxDistance)
            return false;

        var start = ray.Position + ray.Direction * entry;
        var column = Math.Clamp((int)MathF.Floor((start.X - _originX) / SpacingX), 0, ColumnCount - 2);
        var row = Math.Clamp((int)MathF.Floor((start.Z - _originZ) / SpacingZ), 0, RowCount - 2);
        var stepColumn = ray.Direction.X > 0 ? 1 : ray.Direction.X < 0 ? -1 : 0;
        var stepRow = ray.Direction.Z > 0 ? 1 : ray.Direction.Z < 0 ? -1 : 0;
        var crossX = NextCrossing(ray.Position.X, ray.Direction.X, _originX + (column + (stepColumn > 0 ? 1 : 0)) * SpacingX);
        var crossZ = NextCrossing(ray.Position.Z, ray.Direction.Z, _originZ + (row + (stepRow > 0 ? 1 : 0)) * SpacingZ);
        var everyX = stepColumn != 0 ? SpacingX / MathF.Abs(ray.Direction.X) : float.PositiveInfinity;
        var everyZ = stepRow != 0 ? SpacingZ / MathF.Abs(ray.Direction.Z) : float.PositiveInfinity;
        // Past where it leaves the heights' range no triangle can be met.
        var limit = MathF.Min(maxDistance, ray.Direction.Y > 0 ? (_highest - ray.Position.Y) / ray.Direction.Y
            : ray.Direction.Y < 0 ? (_lowest - ray.Position.Y) / ray.Direction.Y : float.PositiveInfinity);
        while (true)
        {
            var cell = 2 * (row * (ColumnCount - 1) + column);
            var found = false;
            var nearest = maxDistance;
            for (var triangle = cell; triangle < cell + 2; triangle++)
            {
                if (!TryGetTriangle(triangle, out var first, out var second, out var third)
                    || !Collision.RayIntersectsTriangle(ray, first, second, third, out float reach) || reach > nearest)
                    continue;

                nearest = reach;
                found = true;
                var faceNormal = NormalOf(first, second, third);
                normal = Vector3F.Dot(ray.Direction, faceNormal) <= 0 ? faceNormal : -faceNormal;
            }

            if (found)
            {
                distance = nearest;
                return true;
            }

            // A ray straight up or down crosses no other cell.
            if (stepColumn == 0 && stepRow == 0)
                return false;

            if (crossX < crossZ)
            {
                if (crossX > limit)
                    return false;

                column += stepColumn;
                crossX += everyX;
            }
            else
            {
                if (crossZ > limit)
                    return false;

                row += stepRow;
                crossZ += everyZ;
            }

            if (column < 0 || column > ColumnCount - 2 || row < 0 || row > RowCount - 2)
                return false;
        }
    }

    internal override void FindTriangles(in BoundingBox<float> box, List<int> into)
    {
        var firstColumn = Math.Max(0, (int)MathF.Floor((box.Minimum.X - _originX) / SpacingX));
        var lastColumn = Math.Min(ColumnCount - 2, (int)MathF.Floor((box.Maximum.X - _originX) / SpacingX));
        var firstRow = Math.Max(0, (int)MathF.Floor((box.Minimum.Z - _originZ) / SpacingZ));
        var lastRow = Math.Min(RowCount - 2, (int)MathF.Floor((box.Maximum.Z - _originZ) / SpacingZ));
        if (box.Maximum.Y < _lowest || box.Minimum.Y > _highest)
            return;

        for (var row = firstRow; row <= lastRow; row++)
        {
            for (var column = firstColumn; column <= lastColumn; column++)
            {
                var cell = 2 * (row * (ColumnCount - 1) + column);
                for (var triangle = cell; triangle < cell + 2; triangle++)
                {
                    if (!TryGetTriangle(triangle, out var first, out var second, out var third))
                        continue;

                    var low = MathF.Min(first.Y, MathF.Min(second.Y, third.Y));
                    var high = MathF.Max(first.Y, MathF.Max(second.Y, third.Y));
                    if (high >= box.Minimum.Y && low <= box.Maximum.Y)
                        into.Add(triangle);
                }
            }
        }
    }

    // Cell (column, row) is triangles 2 (row × (columns - 1) + column) and the next: the first on the corners
    // (column, row), (column, row + 1), (column + 1, row + 1), the second on (column, row), (column + 1, row + 1),
    // (column + 1, row); both face +Y on flat ground.
    internal override bool TryGetTriangle(int triangle, out Vector3F first, out Vector3F second, out Vector3F third)
    {
        var cell = triangle / 2;
        var column = cell % (ColumnCount - 1);
        var row = cell / (ColumnCount - 1);
        first = Sample(column, row);
        if (triangle % 2 == 0)
        {
            second = Sample(column, row + 1);
            third = Sample(column + 1, row + 1);
        }
        else
        {
            second = Sample(column + 1, row + 1);
            third = Sample(column + 1, row);
        }

        return !float.IsNaN(first.Y) && !float.IsNaN(second.Y) && !float.IsNaN(third.Y);
    }

    internal override int GetActiveEdges(int triangle) => _activeEdges[triangle];

    private Vector3F Sample(int column, int row) =>
        new(_originX + column * SpacingX, _heights[row * ColumnCount + column], _originZ + row * SpacingZ);

    private Vector3F BoxCorner(int corner) =>
        new((corner & 1) == 0 ? _originX : -_originX, (corner & 2) == 0 ? _lowest : _highest,
            (corner & 4) == 0 ? _originZ : -_originZ);

    private static float NextCrossing(float position, float direction, float boundary) =>
        direction != 0 ? (boundary - position) / direction : float.PositiveInfinity;

    // The triangle across each edge: the first of a cell meets the cell before it in X by its first edge, the cell after
    // it in Z by its second and its own second triangle by the diagonal; the second meets its first by the diagonal,
    // the cell after it in X by its second edge and the cell before it in Z by its third.
    private byte FindActiveEdges(int triangle)
    {
        var cell = triangle / 2;
        var column = cell % (ColumnCount - 1);
        var row = cell / (ColumnCount - 1);
        var isFirst = triangle % 2 == 0;
        TryGetTriangle(triangle, out var first, out var second, out var third);
        var normal = NormalOf(first, second, third);
        var active = 0;
        for (var edge = 0; edge < 3; edge++)
        {
            var neighbor = isFirst
                ? edge switch
                {
                    0 => column > 0 ? triangle - 1 : -1,
                    1 => row < RowCount - 2 ? 2 * ((row + 1) * (ColumnCount - 1) + column) + 1 : -1,
                    _ => triangle + 1
                }
                : edge switch
                {
                    0 => triangle - 1,
                    1 => column < ColumnCount - 2 ? triangle + 1 : -1,
                    _ => row > 0 ? 2 * ((row - 1) * (ColumnCount - 1) + column) : -1
                };
            var start = edge == 0 ? first : edge == 1 ? second : third;
            var end = edge == 0 ? second : edge == 1 ? third : first;
            if (neighbor < 0 || !TryGetTriangle(neighbor, out var otherFirst, out var otherSecond, out var otherThird)
                || IsEdgeActive(normal, NormalOf(otherFirst, otherSecond, otherThird), end - start))
                active |= 1 << edge;
        }

        return (byte)active;
    }
}
