using System;

namespace Adamantium.Mathematics;

/// <summary>
/// Every point within <see cref="Radius"/> of the segment from <see cref="Start"/> to <see cref="End"/>. A ray's
/// distances are in units of its direction, as in <see cref="Collision"/>.
/// </summary>
public struct BoundingCapsule : IConvexShape<float>
{
    public Vector3F Start;
    public Vector3F End;
    public float Radius;

    public BoundingCapsule(Vector3F start, Vector3F end, float radius)
    {
        Start = start;
        End = end;
        Radius = radius;
    }

    public readonly Vector3F Center => (Start + End) * 0.5f;

    /// <summary>The length of the segment, without the rounded ends.</summary>
    public readonly float Length => (End - Start).Length();

    /// <summary>
    /// The capsule carried through an affine matrix. The radius takes the largest axis scale, so a non-uniform scale
    /// keeps it enclosing.
    /// </summary>
    public readonly BoundingCapsule Transform(Matrix4x4F matrix)
    {
        var scale = Math.Max(matrix.Right.Length(), Math.Max(matrix.Up.Length(), matrix.Forward.Length()));
        return new BoundingCapsule(Vector3F.TransformCoordinate(Start, matrix), Vector3F.TransformCoordinate(End, matrix), Radius * scale);
    }

    public readonly Vector3F Support(Vector3F direction)
    {
        var end = Vector3F.Dot(direction, End - Start) >= 0 ? End : Start;
        var length = direction.Length();
        return length > 0 ? end + direction * (Radius / length) : end + new Vector3F(Radius, 0, 0);
    }

    public readonly ContainmentType Contains(Vector3F point) =>
        Vector3F.DistanceSquared(Collision.ClosestPointSegmentPoint(Start, End, point), point) <= Radius * Radius
            ? ContainmentType.Contains
            : ContainmentType.Disjoint;

    /// <summary>Where <paramref name="ray"/> first meets the capsule; a ray starting inside meets it at 0.</summary>
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
        if (axisLength > float.Epsilon)
        {
            var offset = ray.Position - Start;
            var alongDirection = Vector3F.Dot(axis, ray.Direction);
            var alongOffset = Vector3F.Dot(axis, offset);
            var a = axisLength * Vector3F.Dot(ray.Direction, ray.Direction) - alongDirection * alongDirection;
            var b = axisLength * Vector3F.Dot(ray.Direction, offset) - alongOffset * alongDirection;
            var c = axisLength * Vector3F.Dot(offset, offset) - alongOffset * alongOffset - Radius * Radius * axisLength;
            var h = b * b - a * c;
            if (a > 1e-7f * axisLength && h >= 0)
            {
                var t = (-b - MathF.Sqrt(h)) / a;
                var along = alongOffset + t * alongDirection;
                if (t >= 0 && along >= 0 && along <= axisLength)
                {
                    distance = t;
                    found = true;
                }
            }
        }

        foreach (var end in (ReadOnlySpan<Vector3F>)[Start, End])
        {
            if (Collision.RayIntersectsSphere(ray, new BoundingSphere<float>(end, Radius), out float toEnd) && toEnd < distance)
            {
                distance = toEnd;
                found = true;
            }
        }

        if (!found)
        {
            distance = 0;
        }

        return found;
    }

    /// <summary>Where <paramref name="ray"/> first meets the capsule; a ray starting inside meets it at its start.</summary>
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

    public readonly bool Intersects(BoundingSphere<float> sphere)
    {
        var reach = Radius + sphere.Radius;
        return Vector3F.DistanceSquared(Collision.ClosestPointSegmentPoint(Start, End, sphere.Center), sphere.Center) <= reach * reach;
    }

    public readonly bool Intersects(BoundingCapsule capsule)
    {
        var reach = Radius + capsule.Radius;
        return Collision.DistanceSquaredSegmentSegment(Start, End, capsule.Start, capsule.End) <= reach * reach;
    }

    public readonly bool Intersects(OrientedBoundingBox<float> box) => Gjk.Intersects(this, box);

    public readonly bool Intersects(BoundingBox<float> box) => Gjk.Intersects(this, box);

    public readonly bool Intersects(ConvexHull hull) => Gjk.Intersects(this, hull);

    /// <summary>On which side of a plane the capsule lies, or whether the plane cuts it.</summary>
    public readonly PlaneIntersectionType Intersects(Plane<float> plane)
    {
        var start = Plane<float>.DotCoordinate(plane, Start);
        var end = Plane<float>.DotCoordinate(plane, End);
        if (Math.Min(start, end) > Radius)
        {
            return PlaneIntersectionType.Front;
        }

        return Math.Max(start, end) < -Radius ? PlaneIntersectionType.Back : PlaneIntersectionType.Intersecting;
    }

    /// <summary>Whether the capsule is inside <paramref name="frustum"/>, crosses its surface, or is outside - plane by
    /// plane, as the frustum tests its other shapes; a NaN is outside.</summary>
    public readonly ContainmentType Within(BoundingFrustum<float> frustum)
    {
        var result = ContainmentType.Contains;
        Span<Plane<float>> planes = [frustum.Near, frustum.Left, frustum.Right, frustum.Top, frustum.Bottom, frustum.Far];
        foreach (var plane in frustum.EnableFarPlaneCheck ? planes : planes[..5])
        {
            var start = Plane<float>.DotCoordinate(plane, Start);
            var end = Plane<float>.DotCoordinate(plane, End);
            if (!(Math.Max(start, end) >= -Radius))
            {
                return ContainmentType.Disjoint;
            }

            if (Math.Min(start, end) < Radius)
            {
                result = ContainmentType.Intersects;
            }
        }

        return result;
    }

    public readonly BoundingBox<float> GetBoundingBox()
    {
        var radius = new Vector3F(Radius);
        return new BoundingBox<float>(Vector3F.Min(Start, End) - radius, Vector3F.Max(Start, End) + radius);
    }

    public override readonly string ToString() => $"{{Start:{Start} End:{End} Radius:{Radius}}}";
}
