namespace Adamantium.DarkMatter.Shapes;

/// <summary>The convex form a body collides with, centered on the body; it holds no state of a world, so many bodies
/// can share one.</summary>
public abstract class Shape
{
    /// <summary>Which shape this is.</summary>
    public abstract ShapeKind Kind { get; }

    /// <summary>The distance from the center to the farthest point of the shape.</summary>
    public abstract float BoundingRadius { get; }

    /// <summary>The mass and inertia of the shape filled at <paramref name="density"/>, in kilograms per cubic meter.
    /// </summary>
    public abstract MassProperties ComputeMass(float density);

    /// <summary>The box, aligned with the world's axes, that holds the shape standing at <paramref name="pose"/>.</summary>
    public abstract BoundingBox<float> ComputeBounds(in Pose pose);

    /// <summary>The point of the shape farthest along <paramref name="direction"/>, both in the shape's own axes.</summary>
    public abstract Vector3F GetSupport(Vector3F direction);

    /// <summary>Where <paramref name="ray"/> (own axes, unit direction) first meets the shape within
    /// <paramref name="maxDistance"/>, and the outward normal; from inside it meets at once, facing back.</summary>
    internal abstract bool CastRay(in Ray<float> ray, float maxDistance, out float distance, out Vector3F normal);
}
