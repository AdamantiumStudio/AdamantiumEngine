namespace Adamantium.DarkMatter;

/// <summary>A pose near the origin of the frame it is given in - a part in its body, a body against the other of a pair -
/// in float: collisions are worked out in such frames, however far from the world's origin the pair stands.</summary>
internal struct LocalPose
{
    public Vector3F Position;

    public QuaternionF Rotation;

    public LocalPose(Vector3F position, QuaternionF rotation)
    {
        Position = position;
        Rotation = rotation;
    }

    public LocalPose(Vector3F position) : this(position, QuaternionF.Identity)
    {
    }

    public static LocalPose Identity => new(Vector3F.Zero, QuaternionF.Identity);

    public readonly Vector3F TransformPoint(Vector3F localPoint) => Position + Vector3F.Transform(localPoint, Rotation);

    public readonly LocalPose Transform(in LocalPose localPose) =>
        new(TransformPoint(localPose.Position), Rotation * localPose.Rotation);

    public readonly Vector3F TransformDirection(Vector3F localDirection) => Vector3F.Transform(localDirection, Rotation);

    public readonly Vector3F InverseTransformPoint(Vector3F point) =>
        Vector3F.Transform(point - Position, QuaternionF.Conjugate(Rotation));

    public readonly LocalPose InverseTransform(in LocalPose pose) =>
        new(InverseTransformPoint(pose.Position), QuaternionF.Conjugate(Rotation) * pose.Rotation);

    public readonly Vector3F InverseTransformDirection(Vector3F direction) =>
        Vector3F.Transform(direction, QuaternionF.Conjugate(Rotation));
}
