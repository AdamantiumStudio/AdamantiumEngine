namespace Adamantium.DarkMatter.Shapes;

/// <summary>One shape of a <see cref="CompoundCollisionShape"/> and where it stands in the compound's own axes.
/// </summary>
public readonly struct CompoundPart
{
    public CompoundPart(CollisionShape shape, in Pose pose)
    {
        Shape = shape;
        Pose = pose;
        LocalPose = pose.ToLocal();
    }

    /// <summary>The shape; it cannot be a compound itself.</summary>
    public CollisionShape Shape { get; }

    /// <summary>Where the shape's center stands and how it is turned, in the compound's own axes.</summary>
    public Pose Pose { get; }

    /// <summary>The pose in float, as collisions take it.</summary>
    internal LocalPose LocalPose { get; }
}
