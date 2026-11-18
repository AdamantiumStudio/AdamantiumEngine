using System;

namespace Adamantium.DarkMatter.Shapes;

/// <summary>The convex form a body collides with, centered on the body; it holds no state of a world, so many bodies
/// can share one.</summary>
public abstract class CollisionShape
{
    /// <summary>The least size a shape has, in meters: a micrometer.</summary>
    public const float MinimumSize = 1e-6f;

    /// <summary>How round the edges of a hull or a cylinder are unless told otherwise, in meters.</summary>
    public const float DefaultConvexRadius = 0.05f;

    /// <summary>Which shape this is.</summary>
    public abstract CollisionShapeKind Kind { get; }

    /// <summary>The distance from the center to the farthest point of the shape.</summary>
    public abstract float BoundingRadius { get; }

    /// <summary>The mass and inertia of the shape filled at <paramref name="density"/>, in kilograms per cubic meter.
    /// </summary>
    public abstract MassProperties ComputeMass(float density);

    /// <summary>The box, aligned with the world's axes, that holds the shape standing at <paramref name="pose"/>.</summary>
    public abstract BoundingBox<float> ComputeBounds(in Pose pose);

    /// <summary>The point of the shape farthest along <paramref name="direction"/>, both in the shape's own axes.</summary>
    public abstract Vector3F GetSupport(Vector3F direction);

    /// <summary>How far the shape reaches past its core, the part GJK and EPA meet: a ball's radius around a point, a
    /// capsule's around a segment, a rounded hull's around its hull drawn in; nothing for a box, its own core.</summary>
    internal virtual float CoreRadius => 0;

    /// <summary>The point of the core farthest along <paramref name="direction"/>, both in the shape's own axes.</summary>
    internal virtual Vector3F GetCoreSupport(Vector3F direction) => GetSupport(direction);

    /// <summary>Writes into <paramref name="into"/> the corners of the core's face turned most toward
    /// <paramref name="direction"/>, in the shape's own axes, and gives their count: one for a point, two for an
    /// edge.</summary>
    internal virtual int GetSupportingFace(Vector3F direction, Span<Vector3F> into)
    {
        into[0] = GetCoreSupport(direction);
        return 1;
    }

    /// <summary>How many flat faces the shape has, each a separating axis worth trying; none for a round shape.
    /// </summary>
    internal virtual int FlatFaceCount => 0;

    /// <summary>The outward normal of face <paramref name="face"/>, of length one, in the shape's own axes.</summary>
    internal virtual Vector3F GetFlatFaceNormal(int face) => throw new ArgumentOutOfRangeException(nameof(face));

    /// <summary>How far the plane of the core's face <paramref name="face"/> stands from the shape's origin along its
    /// normal: the farthest the core reaches that way, the shape reaching <see cref="CoreRadius"/> farther.</summary>
    internal virtual float GetFlatFaceOffset(int face) => throw new ArgumentOutOfRangeException(nameof(face));

    /// <summary>The face whose normal turns most toward <paramref name="direction"/>, in the shape's own axes; -1 for
    /// a round shape.</summary>
    internal virtual int FindFlatFace(Vector3F direction) => -1;

    /// <summary>Writes into <paramref name="into"/> the corners of the core's face <paramref name="face"/>, in the
    /// shape's own axes, and gives their count.</summary>
    internal virtual int GetFlatFace(int face, Span<Vector3F> into) =>
        throw new ArgumentOutOfRangeException(nameof(face));

    /// <summary>How fast the space the shape fills sweeps outward when, turned by <paramref name="rotation"/>, it spins
    /// at <paramref name="angularVelocity"/>; a ball spinning in place sweeps nothing.</summary>
    internal virtual float MeasureSweepSpeed(Vector3F angularVelocity, QuaternionF rotation) =>
        angularVelocity.Length() * BoundingRadius;

    /// <summary>Gives <paramref name="value"/> back, or throws unless it is a finite size of at least
    /// <see cref="MinimumSize"/>.</summary>
    protected static float RequireSize(float value, string parameterName)
    {
        if (!float.IsFinite(value) || value < MinimumSize)
            throw new ArgumentOutOfRangeException(parameterName, value, "A size must be finite and at least a micrometer.");

        return value;
    }

    /// <summary>Throws when the shape reaches so far that its volume cannot be counted.</summary>
    protected void RequireFiniteReach(string parameterName)
    {
        var reach = BoundingRadius;
        if (!float.IsFinite(8f * reach * reach * reach))
            throw new ArgumentOutOfRangeException(parameterName, reach, "The shape is too large to count with.");
    }

    /// <summary>Where <paramref name="ray"/> (own axes, unit direction) first meets the shape within
    /// <paramref name="maxDistance"/>, and the outward normal; from inside it meets at once, facing back.</summary>
    public abstract bool CastRay(in Ray<float> ray, float maxDistance, out float distance, out Vector3F normal);
}
