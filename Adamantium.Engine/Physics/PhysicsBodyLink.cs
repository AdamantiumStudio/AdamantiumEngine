using Adamantium.DarkMatter;
using Adamantium.ECS;

namespace Adamantium.Engine.Physics;

/// <summary>An entity the physics moves, as the game's side keeps it: the last two poses the physics sent - where the
/// body's center of mass stood and how it was turned - to draw between.</summary>
internal sealed class PhysicsBodyLink
{
    public PhysicsBodyLink(Entity entity, BodyKind kind, Vector3F centerOfMass, Vector3F position, QuaternionF rotation,
        int depth)
    {
        Entity = entity;
        Name = entity.Name;
        Kind = kind;
        Depth = depth;
        CenterOfMass = centerOfMass;
        PreviousPosition = position;
        PreviousRotation = rotation;
        CurrentPosition = position;
        CurrentRotation = rotation;
    }

    public Entity Entity { get; }

    /// <summary>The entity's name when the game started, for the physics thread's log.</summary>
    public string Name { get; }

    public BodyKind Kind { get; }

    /// <summary>How many entities the entity hangs under; parents are stood before their children.</summary>
    public int Depth { get; }

    /// <summary>The body's center of mass in the entity's own axes, unscaled: what a turn is drawn about.</summary>
    public Vector3F CenterOfMass { get; }

    public Vector3F PreviousPosition { get; set; }

    public QuaternionF PreviousRotation { get; set; }

    public Vector3F CurrentPosition { get; set; }

    public QuaternionF CurrentRotation { get; set; }

    public Vector3F LinearVelocity { get; set; }

    public Vector3F AngularVelocity { get; set; }

    /// <summary>The number of the last result that brought the body a pose.</summary>
    public long MovedInResult { get; set; } = -2;

    /// <summary>The number of the frame the entity was last stood in, so it is stood once a frame.</summary>
    public long PlacedInFrame { get; set; } = -1;

    /// <summary>Whether the entity is stood this frame whatever its parent does: its body moved or just stopped.</summary>
    public bool MustPlace { get; set; }

    /// <summary>Whether the log was told the entity's parent got stretched or mirrored, so it is told once.</summary>
    public bool WarnedOfParent { get; set; }

    /// <summary>The parent's world matrix the entity was last stood under; it is stood again when that changes.</summary>
    public Matrix4x4F ParentWorld { get; set; }
}
