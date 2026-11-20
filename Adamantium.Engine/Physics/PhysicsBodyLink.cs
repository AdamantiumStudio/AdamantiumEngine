using Adamantium.DarkMatter;
using Adamantium.ECS;
using Adamantium.ECS.Components;
using Adamantium.Mathematics;

namespace Adamantium.Engine.Physics;

/// <summary>An entity the physics moves, as the game's side keeps it: the last two poses the physics sent - where the
/// body's center of mass stood and how it was turned - to draw between, from the first the body sends.</summary>
internal sealed class PhysicsBodyLink
{
    public PhysicsBodyLink(RigidBody body, BodyParts parts, BodyDescription made, int key, int generation, int depth)
    {
        Entity = body.Owner;
        Body = body;
        Parts = parts;
        Made = made;
        Key = key;
        Generation = generation;
        AddedAs = generation;
        Name = Entity.Name;
        Kind = body.Kind;
        IsParented = Entity.Owner != null;
        WrittenPosition = Entity.Transform.Position;
        WrittenRotation = Entity.Transform.Rotation;
        Depth = depth;
    }

    public Entity Entity { get; }

    /// <summary>The rigid body the body follows.</summary>
    public RigidBody Body { get; set; }

    /// <summary>What the body was made from, to see when it changes.</summary>
    public BodyParts Parts { get; set; }

    /// <summary>The description the body was made from, as the game's side keeps it; the physics thread has its own
    /// copy.</summary>
    public BodyDescription Made { get; set; }

    /// <summary>Whether the entity hung under another when the body was made.</summary>
    public bool IsParented { get; }

    public int Key { get; }

    /// <summary>Which body under <see cref="Key"/> this is, or where it stands since it was moved by hand: motions sent
    /// before that are passed over.</summary>
    public int Generation { get; set; }

    /// <summary>The generation the body was sent to the physics thread with: a refusal of it names this one.</summary>
    public int AddedAs { get; }

    /// <summary>How many requests that could be refused - its add, then changes to its settings or shape - were sent
    /// for the body.</summary>
    public int RequestsSent { get; set; }

    /// <summary>Whether the body was taken out or made anew; a link left in a list is passed over.</summary>
    public bool IsGone { get; set; }

    /// <summary>The entity's position the physics last gave it, or found when it made the body.</summary>
    public Vector3<double> WrittenPosition { get; set; }

    /// <summary>The entity's rotation the physics last gave it, or found when it made the body.</summary>
    public QuaternionF WrittenRotation { get; set; }

    /// <summary>The entity's name when the body was made, for the log.</summary>
    public string Name { get; }

    public BodyKind Kind { get; set; }

    /// <summary>How many entities the entity hangs under; parents are stood before their children.</summary>
    public int Depth { get; }

    /// <summary>The body's center of mass in the entity's own axes, unscaled: what a turn is drawn about.</summary>
    public Vector3F CenterOfMass { get; set; }

    public WorldPosition PreviousPosition { get; set; }

    public QuaternionF PreviousRotation { get; set; }

    public WorldPosition CurrentPosition { get; set; }

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
