using System;

namespace Adamantium.DarkMatter.Shapes;

/// <summary>A ball of <see cref="Radius"/> around the body's center.</summary>
public sealed class SphereShape : Shape
{
    public SphereShape(float radius)
    {
        Radius = RequireSize(radius, nameof(radius));
        RequireFiniteReach(nameof(radius));
    }

    /// <summary>The radius.</summary>
    public float Radius { get; }

    public override ShapeKind Kind => ShapeKind.Sphere;

    public override float BoundingRadius => Radius;

    public override MassProperties ComputeMass(float density)
    {
        var mass = density * 4f / 3f * MathF.PI * Radius * Radius * Radius;
        return new MassProperties(mass, new Vector3F(0.4f * mass * Radius * Radius));
    }

    public override BoundingBox<float> ComputeBounds(in Pose pose)
    {
        var reach = new Vector3F(Radius);
        return new BoundingBox<float>(pose.Position - reach, pose.Position + reach);
    }

    public override Vector3F GetSupport(Vector3F direction) => Vector3F.Normalize(direction) * Radius;

    internal override float MeasureSweepSpeed(Vector3F angularVelocity, QuaternionF rotation) => 0;

    internal override bool CastRay(in Ray<float> ray, float maxDistance, out float distance, out Vector3F normal)
    {
        normal = default;
        if (!Collision.RayIntersectsSphere(ray, new BoundingSphere<float>(Vector3F.Zero, Radius), out distance)
            || distance > maxDistance)
            return false;

        normal = distance == 0 ? -ray.Direction : Vector3F.Normalize(ray.Position + ray.Direction * distance);
        return true;
    }
}
