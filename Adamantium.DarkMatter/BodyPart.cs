using Adamantium.DarkMatter.Shapes;

namespace Adamantium.DarkMatter;

/// <summary>One shape of a body of several, where it stands in the body's own axes, and the caller's own for it - the
/// collider it was made of, say.</summary>
public readonly struct BodyPart
{
    public BodyPart()
    {
    }

    /// <summary>The shape; a compound cannot be a part.</summary>
    public CollisionShape Shape { get; init; }

    /// <summary>Where the shape's center stands and how it is turned, in the body's own axes.</summary>
    public LocalPose Pose { get; init; } = LocalPose.Identity;

    /// <summary>How the part rubs and bounces; the body's for none.</summary>
    public PhysicsMaterial? Material { get; init; }

    /// <summary>The layer the part collides and is met by queries on; the body's for none.</summary>
    public Layer? Layer { get; init; }

    /// <summary>The caller's own, given back by <see cref="PhysicsSimulation.DataOf(BodyId, int)"/>.</summary>
    public object Data { get; init; }
}
