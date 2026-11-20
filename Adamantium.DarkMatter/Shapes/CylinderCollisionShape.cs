using System;

namespace Adamantium.DarkMatter.Shapes;

/// <summary>A cylinder along the body's Y axis - a wheel, a barrel - its rims rounded by <see cref="ConvexRadius"/>
/// within it.</summary>
public sealed class CylinderCollisionShape : CollisionShape
{
    /// <summary>How many corners each end gives as a face: the circle met where a cylinder stands on its end.</summary>
    internal const int EndCorners = 16;

    // The rounding takes at most this share of the smaller of the half height and the radius.
    private const float MaxShareOfSize = 0.25f;

    // The sine of the tilt under which the side counts as lying along a direction: about six degrees.
    private const float LyingAlong = 0.1f;

    // The cosine and sine of each end corner's angle, counter-clockwise seen from above.
    private static readonly float[] CornerCosines = new float[EndCorners];
    private static readonly float[] CornerSines = new float[EndCorners];

    static CylinderCollisionShape()
    {
        for (var corner = 0; corner < EndCorners; corner++)
        {
            var angle = 2 * MathF.PI * corner / EndCorners;
            CornerCosines[corner] = MathF.Cos(angle);
            CornerSines[corner] = MathF.Sin(angle);
        }
    }

    private readonly float _coreHalfHeight;
    private readonly float _coreRadius;

    /// <summary>A cylinder with rims rounded by <paramref name="convexRadius"/>, at most a quarter of its smaller size.
    /// </summary>
    public CylinderCollisionShape(float halfHeight, float radius, float convexRadius = DefaultConvexRadius)
    {
        HalfHeight = RequireSize(halfHeight, nameof(halfHeight));
        Radius = RequireSize(radius, nameof(radius));
        RequireFiniteReach(halfHeight >= radius ? nameof(halfHeight) : nameof(radius));
        if (!float.IsFinite(convexRadius) || convexRadius < 0)
            throw new ArgumentOutOfRangeException(nameof(convexRadius), convexRadius,
                "A convex radius must be finite and not below zero.");

        ConvexRadius = MathF.Min(convexRadius, MaxShareOfSize * MathF.Min(halfHeight, radius));
        _coreHalfHeight = HalfHeight - ConvexRadius;
        _coreRadius = Radius - ConvexRadius;
    }

    /// <summary>Half the height, end to end.</summary>
    public float HalfHeight { get; }

    /// <summary>The radius.</summary>
    public float Radius { get; }

    /// <summary>How round the rims are, in meters.</summary>
    public float ConvexRadius { get; }

    public override CollisionShapeKind Kind => CollisionShapeKind.Cylinder;

    public override float BoundingRadius => MathF.Sqrt(HalfHeight * HalfHeight + Radius * Radius);

    /// <summary>The mass of the cylinder with sharp rims, its rounding not taken off.</summary>
    public override MassProperties ComputeMass(float density)
    {
        var radiusSquared = Radius * Radius;
        var mass = density * MathF.PI * radiusSquared * 2f * HalfHeight;
        var across = mass * (3f * radiusSquared + 4f * HalfHeight * HalfHeight) / 12f;
        return new MassProperties(mass, new Vector3F(across, mass * radiusSquared / 2f, across));
    }

    internal override BoundingBox<float> ComputeBounds(in LocalPose pose)
    {
        var axis = pose.TransformDirection(Vector3F.UnitY);
        var reach = new Vector3F(
            HalfHeight * MathF.Abs(axis.X) + Radius * MathF.Sqrt(MathF.Max(0, 1 - axis.X * axis.X)),
            HalfHeight * MathF.Abs(axis.Y) + Radius * MathF.Sqrt(MathF.Max(0, 1 - axis.Y * axis.Y)),
            HalfHeight * MathF.Abs(axis.Z) + Radius * MathF.Sqrt(MathF.Max(0, 1 - axis.Z * axis.Z)));
        return new BoundingBox<float>(pose.Position - reach, pose.Position + reach);
    }

    /// <summary>The core's farthest point, out by the convex radius.</summary>
    public override Vector3F GetSupport(Vector3F direction)
    {
        var core = GetCoreSupport(direction);
        var length = direction.Length();
        return ConvexRadius > 0 && length > 0 ? core + direction * (ConvexRadius / length) : core;
    }

    internal override float CoreRadius => ConvexRadius;

    internal override Vector3F GetCoreSupport(Vector3F direction)
    {
        var height = direction.Y >= 0 ? _coreHalfHeight : -_coreHalfHeight;
        var across = MathF.Sqrt(direction.X * direction.X + direction.Z * direction.Z);
        return across > 0
            ? new Vector3F(direction.X / across * _coreRadius, height, direction.Z / across * _coreRadius)
            : new Vector3F(0, height, 0);
    }

    /// <summary>The end when the direction runs more along the axis than across; the side's line when it lies across
    /// within a few degrees; else one point of the rim.</summary>
    internal override int GetSupportingFace(Vector3F direction, Span<Vector3F> into)
    {
        var length = direction.Length();
        var across = MathF.Sqrt(direction.X * direction.X + direction.Z * direction.Z);
        if (MathF.Abs(direction.Y) >= across)
            return GetFlatFace(direction.Y >= 0 ? 0 : 1, into);

        if (MathF.Abs(direction.Y) < LyingAlong * length)
        {
            var rimX = direction.X / across * _coreRadius;
            var rimZ = direction.Z / across * _coreRadius;
            into[0] = new Vector3F(rimX, -_coreHalfHeight, rimZ);
            into[1] = new Vector3F(rimX, _coreHalfHeight, rimZ);
            return 2;
        }

        into[0] = GetCoreSupport(direction);
        return 1;
    }

    internal override int FlatFaceCount => 2;

    internal override Vector3F GetFlatFaceNormal(int face) => face == 0 ? Vector3F.UnitY : -Vector3F.UnitY;

    internal override float GetFlatFaceOffset(int face) => _coreHalfHeight;

    internal override int FindFlatFace(Vector3F direction) => direction.Y >= 0 ? 0 : 1;

    /// <summary>The core's end as a circle of <see cref="EndCorners"/> corners, wound counter-clockwise seen from
    /// outside.</summary>
    internal override int GetFlatFace(int face, Span<Vector3F> into)
    {
        var height = face == 0 ? _coreHalfHeight : -_coreHalfHeight;
        var turn = face == 0 ? -1 : 1;
        var count = Math.Min(EndCorners, into.Length);
        for (var corner = 0; corner < count; corner++)
        {
            var at = corner * EndCorners / count;
            into[corner] = new Vector3F(CornerCosines[at] * _coreRadius, height, turn * CornerSines[at] * _coreRadius);
        }

        return count;
    }

    /// <summary>Spinning about its own axis a cylinder sweeps nothing: a wheel turning stays where it is.</summary>
    internal override float MeasureSweepSpeed(Vector3F angularVelocity, QuaternionF rotation)
    {
        var axis = Vector3F.Transform(Vector3F.UnitY, rotation);
        return (angularVelocity - axis * Vector3F.Dot(angularVelocity, axis)).Length() * BoundingRadius;
    }

    /// <summary>Where <paramref name="ray"/> first meets the cylinder, the rounding not met; from inside, at once.
    /// </summary>
    public override bool CastRay(in Ray<float> ray, float maxDistance, out float distance, out Vector3F normal)
    {
        var start = ray.Position;
        var direction = ray.Direction;
        var radiusSquared = Radius * Radius;
        if (MathF.Abs(start.Y) <= HalfHeight && start.X * start.X + start.Z * start.Z <= radiusSquared)
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
            var flatOutside = start.X * start.X + start.Z * start.Z - radiusSquared;
            var discriminant = flatAlong * flatAlong - flatSquared * flatOutside;
            if (discriminant >= 0)
            {
                var entry = (-flatAlong - MathF.Sqrt(discriminant)) / flatSquared;
                var height = start.Y + direction.Y * entry;
                if (entry >= 0 && entry <= distance && MathF.Abs(height) <= HalfHeight)
                {
                    found = true;
                    distance = entry;
                    normal = Vector3F.Normalize(new Vector3F(start.X + direction.X * entry, 0,
                        start.Z + direction.Z * entry));
                }
            }
        }

        // An end is met from beyond it, heading toward the middle.
        if (direction.Y != 0 && MathF.Abs(start.Y) > HalfHeight && start.Y * direction.Y < 0)
        {
            var end = start.Y > 0 ? HalfHeight : -HalfHeight;
            var entry = (end - start.Y) / direction.Y;
            var point = start + direction * entry;
            if (entry >= 0 && entry <= distance && point.X * point.X + point.Z * point.Z <= radiusSquared)
            {
                found = true;
                distance = entry;
                normal = new Vector3F(0, MathF.Sign(end), 0);
            }
        }

        if (!found)
            distance = 0;

        return found;
    }
}
