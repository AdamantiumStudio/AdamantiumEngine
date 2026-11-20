namespace Adamantium.DarkMatter;

/// <summary>Where a body stands in the world and how it is turned: the position of its center, kept as closely far from
/// the origin as near it, and its rotation.</summary>
public struct Pose
{
    /// <summary>The position of the center.</summary>
    public WorldPosition Position;

    /// <summary>The rotation, of length one.</summary>
    public QuaternionF Rotation;

    /// <summary>The pose at <paramref name="position"/> turned by <paramref name="rotation"/>.</summary>
    public Pose(WorldPosition position, QuaternionF rotation)
    {
        Position = position;
        Rotation = rotation;
    }

    /// <summary>The pose at <paramref name="position"/>, not turned.</summary>
    public Pose(WorldPosition position) : this(position, QuaternionF.Identity)
    {
    }

    /// <summary>The pose at the origin, not turned.</summary>
    public static Pose Identity => new(WorldPosition.Zero, QuaternionF.Identity);

    /// <summary>A point given in the body's own axes, in the world.</summary>
    public readonly WorldPosition TransformPoint(Vector3F localPoint) =>
        Position + Vector3F.Transform(localPoint, Rotation);

    /// <summary>A pose given in the body's own axes, in the world.</summary>
    public readonly Pose Transform(in LocalPose localPose) =>
        new(TransformPoint(localPose.Position), Rotation * localPose.Rotation);

    /// <summary>A direction given in the body's own axes, in the world.</summary>
    public readonly Vector3F TransformDirection(Vector3F localDirection) => Vector3F.Transform(localDirection, Rotation);

    /// <summary>A point of the world in the body's own axes, near the body.</summary>
    public readonly Vector3F InverseTransformPoint(WorldPosition point) =>
        Vector3F.Transform(point.OffsetFrom(Position), QuaternionF.Conjugate(Rotation));

    /// <summary>A pose of the world in the body's own axes, near the body.</summary>
    public readonly LocalPose InverseTransform(in Pose pose) =>
        new(InverseTransformPoint(pose.Position), QuaternionF.Conjugate(Rotation) * pose.Rotation);

    /// <summary>A direction of the world in the body's own axes.</summary>
    public readonly Vector3F InverseTransformDirection(Vector3F direction) =>
        Vector3F.Transform(direction, QuaternionF.Conjugate(Rotation));
}
