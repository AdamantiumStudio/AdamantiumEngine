namespace Adamantium.DarkMatter;

/// <summary>Where a body stands and how it is turned: the position of its center, in double so a world far from its
/// origin keeps millimeters, and its rotation.</summary>
public struct Pose
{
    /// <summary>The position of the center.</summary>
    public Vector3<double> Position;

    /// <summary>The rotation, of length one.</summary>
    public QuaternionF Rotation;

    /// <summary>The pose at <paramref name="position"/> turned by <paramref name="rotation"/>.</summary>
    public Pose(Vector3<double> position, QuaternionF rotation)
    {
        Position = position;
        Rotation = rotation;
    }

    /// <summary>The pose at <paramref name="position"/>, not turned.</summary>
    public Pose(Vector3<double> position) : this(position, QuaternionF.Identity)
    {
    }

    /// <summary>The pose at the origin, not turned.</summary>
    public static Pose Identity => new(Vector3<double>.Zero, QuaternionF.Identity);

    /// <summary>A point given in the body's own axes, in the world.</summary>
    public readonly Vector3<double> TransformPoint(Vector3F localPoint) =>
        Position + Vector3F.Transform(localPoint, Rotation).As<double>();

    /// <summary>A pose given in the body's own axes, in the world.</summary>
    public readonly Pose Transform(in Pose localPose) =>
        new(Position + Vector3<double>.Transform(localPose.Position, Rotation.As<double>()),
            Rotation * localPose.Rotation);

    /// <summary>A direction given in the body's own axes, in the world.</summary>
    public readonly Vector3F TransformDirection(Vector3F localDirection) => Vector3F.Transform(localDirection, Rotation);

    /// <summary>A point of the world in the body's own axes, near the body.</summary>
    public readonly Vector3F InverseTransformPoint(Vector3<double> point) =>
        Vector3F.Transform((point - Position).As<float>(), QuaternionF.Conjugate(Rotation));

    /// <summary>A pose of the world in the body's own axes.</summary>
    public readonly Pose InverseTransform(in Pose pose) =>
        new(Vector3<double>.Transform(pose.Position - Position, QuaternionF.Conjugate(Rotation).As<double>()),
            QuaternionF.Conjugate(Rotation) * pose.Rotation);

    /// <summary>A direction of the world in the body's own axes.</summary>
    public readonly Vector3F InverseTransformDirection(Vector3F direction) =>
        Vector3F.Transform(direction, QuaternionF.Conjugate(Rotation));

    /// <summary>The pose in float, for one near its frame's origin: a part in its body.</summary>
    internal readonly LocalPose ToLocal() => new(Position.As<float>(), Rotation);
}
