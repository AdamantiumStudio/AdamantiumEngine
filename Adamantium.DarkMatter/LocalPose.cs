namespace Adamantium.DarkMatter;

/// <summary>A pose in a body's own axes, or near the origin of any frame - a compound's part, a body against the other of
/// a pair - in float: shapes and collisions are worked out in such frames, however far from the world's origin.</summary>
public struct LocalPose
{
    /// <summary>The position of the center, in the frame's axes.</summary>
    public Vector3F Position;

    /// <summary>The rotation, of length one.</summary>
    public QuaternionF Rotation;

    /// <summary>The pose at <paramref name="position"/> turned by <paramref name="rotation"/>.</summary>
    public LocalPose(Vector3F position, QuaternionF rotation)
    {
        Position = position;
        Rotation = rotation;
    }

    /// <summary>The pose at <paramref name="position"/>, not turned.</summary>
    public LocalPose(Vector3F position) : this(position, QuaternionF.Identity)
    {
    }

    /// <summary>The pose at the frame's origin, not turned.</summary>
    public static LocalPose Identity => new(Vector3F.Zero, QuaternionF.Identity);

    /// <summary>A point given in this pose's axes, in the frame.</summary>
    public readonly Vector3F TransformPoint(Vector3F localPoint) => Position + Vector3F.Transform(localPoint, Rotation);

    /// <summary>A pose given in this pose's axes, in the frame.</summary>
    public readonly LocalPose Transform(in LocalPose localPose) =>
        new(TransformPoint(localPose.Position), Rotation * localPose.Rotation);

    /// <summary>A direction given in this pose's axes, in the frame.</summary>
    public readonly Vector3F TransformDirection(Vector3F localDirection) => Vector3F.Transform(localDirection, Rotation);

    /// <summary>A point of the frame in this pose's axes.</summary>
    public readonly Vector3F InverseTransformPoint(Vector3F point) =>
        Vector3F.Transform(point - Position, QuaternionF.Conjugate(Rotation));

    /// <summary>A pose of the frame in this pose's axes.</summary>
    public readonly LocalPose InverseTransform(in LocalPose pose) =>
        new(InverseTransformPoint(pose.Position), QuaternionF.Conjugate(Rotation) * pose.Rotation);

    /// <summary>A direction of the frame in this pose's axes.</summary>
    public readonly Vector3F InverseTransformDirection(Vector3F direction) =>
        Vector3F.Transform(direction, QuaternionF.Conjugate(Rotation));
}
