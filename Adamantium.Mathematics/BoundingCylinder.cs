using System;

namespace Adamantium.Mathematics;

/// <summary>The cylinder of <see cref="Radius"/> round the axis from <see cref="Start"/> to <see cref="End"/>, flat at
/// both ends.</summary>
public struct BoundingCylinder : IConvexShape<float>
{
    public Vector3F Start;
    public Vector3F End;
    public float Radius;

    public BoundingCylinder(Vector3F start, Vector3F end, float radius)
    {
        Start = start;
        End = end;
        Radius = radius;
    }

    public readonly Vector3F Center => (Start + End) * 0.5f;

    /// <summary>The length from end to end.</summary>
    public readonly float Length => (End - Start).Length();

    /// <summary>The cylinder carried through an affine matrix, its radius by the largest scale so it still encloses.
    /// </summary>
    public readonly BoundingCylinder Transform(Matrix4x4F matrix)
    {
        var scale = Math.Max(matrix.Right.Length(), Math.Max(matrix.Up.Length(), matrix.Forward.Length()));
        return new BoundingCylinder(Vector3F.TransformCoordinate(Start, matrix), Vector3F.TransformCoordinate(End, matrix),
            Radius * scale);
    }

    public readonly Vector3F Support(Vector3F direction)
    {
        var axis = End - Start;
        var end = Vector3F.Dot(direction, axis) >= 0 ? End : Start;
        var lengthSquared = axis.LengthSquared();
        var across = lengthSquared > 0 ? direction - axis * (Vector3F.Dot(direction, axis) / lengthSquared) : direction;
        var acrossLength = across.Length();
        return acrossLength > 0 ? end + across * (Radius / acrossLength) : end;
    }

    public readonly ContainmentType Contains(Vector3F point)
    {
        var axis = End - Start;
        var lengthSquared = axis.LengthSquared();
        var along = lengthSquared > 0 ? Vector3F.Dot(point - Start, axis) / lengthSquared : 0;
        if (along < 0 || along > 1)
        {
            return ContainmentType.Disjoint;
        }

        return Vector3F.DistanceSquared(Start + axis * along, point) <= Radius * Radius
            ? ContainmentType.Contains
            : ContainmentType.Disjoint;
    }

    /// <summary>Where <paramref name="ray"/> first meets the cylinder; a ray starting inside meets it at 0.</summary>
    public readonly bool Intersects(Ray<float> ray, out float distance)
    {
        if (Contains(ray.Position) != ContainmentType.Disjoint)
        {
            distance = 0;
            return true;
        }

        var found = false;
        distance = float.MaxValue;
        var axis = End - Start;
        var axisLength = Vector3F.Dot(axis, axis);
        if (!(axisLength > float.Epsilon))
        {
            distance = 0;
            return false;
        }

        // The side: where the ray comes within the radius of the axis line, between the ends.
        var offset = ray.Position - Start;
        var alongDirection = Vector3F.Dot(axis, ray.Direction);
        var alongOffset = Vector3F.Dot(axis, offset);
        var a = axisLength * Vector3F.Dot(ray.Direction, ray.Direction) - alongDirection * alongDirection;
        var b = axisLength * Vector3F.Dot(ray.Direction, offset) - alongOffset * alongDirection;
        var c = axisLength * Vector3F.Dot(offset, offset) - alongOffset * alongOffset - Radius * Radius * axisLength;
        var h = b * b - a * c;
        if (a > 1e-7f * axisLength * Vector3F.Dot(ray.Direction, ray.Direction) && h >= 0)
        {
            var t = (-b - MathF.Sqrt(h)) / a;
            var along = alongOffset + t * alongDirection;
            if (t >= 0 && along >= 0 && along <= axisLength)
            {
                distance = t;
                found = true;
            }
        }

        // The ends: the plane through each, met within the radius of its center.
        foreach (var end in (ReadOnlySpan<Vector3F>)[Start, End])
        {
            if (alongDirection == 0)
            {
                break;
            }

            var t = Vector3F.Dot(end - ray.Position, axis) / alongDirection;
            if (t >= 0 && t < distance && Vector3F.DistanceSquared(ray.Position + ray.Direction * t, end) <= Radius * Radius)
            {
                distance = t;
                found = true;
            }
        }

        if (!found)
        {
            distance = 0;
        }

        return found;
    }

    /// <summary>Where <paramref name="ray"/> first meets the cylinder; a ray starting inside meets it at its start.
    /// </summary>
    public readonly bool Intersects(Ray<float> ray, out Vector3F point)
    {
        if (!Intersects(ray, out float distance))
        {
            point = Vector3F.Zero;
            return false;
        }

        point = ray.Position + ray.Direction * distance;
        return true;
    }

    /// <summary>Whether the cylinder is inside <paramref name="frustum"/>, crosses it or is outside; a NaN is outside.
    /// </summary>
    public readonly ContainmentType Within(BoundingFrustum<float> frustum)
    {
        var result = ContainmentType.Contains;
        Span<Plane<float>> planes = [frustum.Near, frustum.Left, frustum.Right, frustum.Top, frustum.Bottom, frustum.Far];
        foreach (var plane in frustum.EnableFarPlaneCheck ? planes : planes[..5])
        {
            var reach = ReachAcross(plane.Normal);
            var start = Plane<float>.DotCoordinate(plane, Start);
            var end = Plane<float>.DotCoordinate(plane, End);
            if (!(Math.Max(start, end) >= -reach))
            {
                return ContainmentType.Disjoint;
            }

            if (Math.Min(start, end) < reach)
            {
                result = ContainmentType.Intersects;
            }
        }

        return result;
    }

    public readonly BoundingBox<float> GetBoundingBox()
    {
        var reach = new Vector3F(ReachAcross(Vector3F.UnitX), ReachAcross(Vector3F.UnitY), ReachAcross(Vector3F.UnitZ));
        return new BoundingBox<float>(Vector3F.Min(Start, End) - reach, Vector3F.Max(Start, End) + reach);
    }

    public override readonly string ToString() => $"{{Start:{Start} End:{End} Radius:{Radius}}}";

    // How far the ends' circles reach along a unit direction.
    private readonly float ReachAcross(Vector3F direction)
    {
        var axis = End - Start;
        var length = axis.Length();
        if (!(length > 0))
        {
            return Radius;
        }

        var cosine = Vector3F.Dot(direction, axis) / length;
        return Radius * MathF.Sqrt(MathF.Max(0, 1 - cosine * cosine));
    }
}
