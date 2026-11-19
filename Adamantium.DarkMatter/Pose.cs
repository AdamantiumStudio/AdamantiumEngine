namespace Adamantium.DarkMatter;

/// <summary>Where a body stands and how it is turned: the position of its center and its rotation.</summary>
public struct Pose
{
    /// <summary>The position of the center.</summary>
    public Vector3F Position;

    /// <summary>The rotation, of length one.</summary>
    public QuaternionF Rotation;

    /// <summary>The pose at <paramref name="position"/> turned by <paramref name="rotation"/>.</summary>
    public Pose(Vector3F position, QuaternionF rotation)
    {
        Position = position;
        Rotation = rotation;
    }

    /// <summary>The pose at <paramref name="position"/>, not turned.</summary>
    public Pose(Vector3F position) : this(position, QuaternionF.Identity)
    {
    }

    /// <summary>The pose at the origin, not turned.</summary>
    public static Pose Identity => new(Vector3F.Zero, QuaternionF.Identity);

    /// <summary>A point given in the body's own axes, in the world.</summary>
    public readonly Vector3F TransformPoint(Vector3F localPoint) => Position + Vector3F.Transform(localPoint, Rotation);

    /// <summary>A pose given in the body's own axes, in the world.</summary>
    public readonly Pose Transform(in Pose localPose) =>
        new(TransformPoint(localPose.Position), Rotation * localPose.Rotation);

    /// <summary>A direction given in the body's own axes, in the world.</summary>
    public readonly Vector3F TransformDirection(Vector3F localDirection) => Vector3F.Transform(localDirection, Rotation);

    /// <summary>A point of the world in the body's own axes.</summary>
    public readonly Vector3F InverseTransformPoint(Vector3F point) =>
        Vector3F.Transform(point - Position, QuaternionF.Conjugate(Rotation));

    /// <summary>A pose of the world in the body's own axes.</summary>
    public readonly Pose InverseTransform(in Pose pose) =>
        new(InverseTransformPoint(pose.Position), QuaternionF.Conjugate(Rotation) * pose.Rotation);

    /// <summary>A direction of the world in the body's own axes.</summary>
    public readonly Vector3F InverseTransformDirection(Vector3F direction) =>
        Vector3F.Transform(direction, QuaternionF.Conjugate(Rotation));
}
