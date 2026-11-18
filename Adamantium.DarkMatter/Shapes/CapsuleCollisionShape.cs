using System;

namespace Adamantium.DarkMatter.Shapes;

/// <summary>A capsule along the body's Y axis: every point within <see cref="Radius"/> of the segment reaching
/// <see cref="HalfLength"/> up and down from the center.</summary>
public sealed class CapsuleCollisionShape : CollisionShape
{
    // The sine of the tilt under which the segment counts as lying across a direction: about six degrees.
    private const float LyingAcross = 0.1f;

    public CapsuleCollisionShape(float halfLength, float radius)
    {
        HalfLength = RequireSize(halfLength, nameof(halfLength));
        Radius = RequireSize(radius, nameof(radius));
        RequireFiniteReach(halfLength >= radius ? nameof(halfLength) : nameof(radius));
    }

    /// <summary>Half the length of the inner segment, without the round ends.</summary>
    public float HalfLength { get; }

    /// <summary>The radius.</summary>
    public float Radius { get; }

    public override CollisionShapeKind Kind => CollisionShapeKind.Capsule;

    public override float BoundingRadius => HalfLength + Radius;

    public override MassProperties ComputeMass(float density)
    {
        var radiusSquared = Radius * Radius;
        var cylinder = density * MathF.PI * radiusSquared * 2f * HalfLength;
        var ends = density * 4f / 3f * MathF.PI * radiusSquared * Radius;
        var endOffset = HalfLength + 0.375f * Radius;
        var across = cylinder * (radiusSquared / 4f + HalfLength * HalfLength / 3f)
                     + ends * (83f / 320f * radiusSquared + endOffset * endOffset);
        var along = cylinder * radiusSquared / 2f + ends * 0.4f * radiusSquared;
        return new MassProperties(cylinder + ends, new Vector3F(across, along, across));
    }

    public override BoundingBox<float> ComputeBounds(in Pose pose)
    {
        var top = pose.TransformPoint(new Vector3F(0, HalfLength, 0));
        var bottom = pose.TransformPoint(new Vector3F(0, -HalfLength, 0));
        var reach = new Vector3F(Radius);
        return new BoundingBox<float>(Vector3F.Min(top, bottom) - reach, Vector3F.Max(top, bottom) + reach);
    }

    public override Vector3F GetSupport(Vector3F direction) =>
        new Vector3F(0, direction.Y >= 0 ? HalfLength : -HalfLength, 0) + Vector3F.Normalize(direction) * Radius;

    internal override float CoreRadius => Radius;

    internal override Vector3F GetCoreSupport(Vector3F direction) =>
        new(0, direction.Y >= 0 ? HalfLength : -HalfLength, 0);

    /// <summary>The whole segment when it lies across <paramref name="direction"/>, within a few degrees, else its end
    /// that way.</summary>
    internal override int GetSupportingFace(Vector3F direction, Span<Vector3F> into)
    {
        var length = direction.Length();
        if (length > 0 && MathF.Abs(direction.Y) < LyingAcross * length)
        {
            into[0] = new Vector3F(0, -HalfLength, 0);
            into[1] = new Vector3F(0, HalfLength, 0);
            return 2;
        }

        into[0] = GetCoreSupport(direction);
        return 1;
    }

    internal override float MeasureSweepSpeed(Vector3F angularVelocity, QuaternionF rotation)
    {
        var axis = Vector3F.Transform(Vector3F.UnitY, rotation);
        return (angularVelocity - axis * Vector3F.Dot(angularVelocity, axis)).Length() * BoundingRadius;
    }

    public override bool CastRay(in Ray<float> ray, float maxDistance, out float distance, out Vector3F normal)
    {
        var start = ray.Position;
        var direction = ray.Direction;
        var bottom = new Vector3F(0, -HalfLength, 0);
        var top = new Vector3F(0, HalfLength, 0);
        if ((start - Collision.ClosestPointSegmentPoint(bottom, top, start)).LengthSquared() <= Radius * Radius)
        {
            distance = 0;
            normal = -direction;
            return true;
        }

        var found = false;
        distance = maxDistance;
        normal = default;

        var flatSquared = direction.X * direction.X + direction.Z * direction.Z;
        if (flatSquared > 1e-12f)
        {
            var flatAlong = start.X * direction.X + start.Z * direction.Z;
            var flatOutside = start.X * start.X + start.Z * start.Z - Radius * Radius;
            var discriminant = flatAlong * flatAlong - flatSquared * flatOutside;
            if (discriminant >= 0)
            {
                var entry = (-flatAlong - MathF.Sqrt(discriminant)) / flatSquared;
                var height = start.Y + direction.Y * entry;
                if (entry >= 0 && entry <= distance && height >= -HalfLength && height <= HalfLength)
                {
                    found = true;
                    distance = entry;
                    var point = start + direction * entry;
                    normal = Vector3F.Normalize(new Vector3F(point.X, 0, point.Z));
                }
            }
        }

        found |= CastAtEnd(ray, top, ref distance, ref normal);
        found |= CastAtEnd(ray, bottom, ref distance, ref normal);
        return found;
    }

    private bool CastAtEnd(in Ray<float> ray, Vector3F center, ref float distance, ref Vector3F normal)
    {
        if (!Collision.RayIntersectsSphere(ray, new BoundingSphere<float>(center, Radius), out float entry)
            || entry > distance)
            return false;

        distance = entry;
        normal = Vector3F.Normalize(ray.Position + ray.Direction * entry - center);
        return true;
    }
}
