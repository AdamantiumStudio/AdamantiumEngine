using Adamantium.DarkMatter;
using Adamantium.ECS;
using Adamantium.ECS.Components;
using Adamantium.Mathematics;

namespace Adamantium.Engine.Physics;

/// <summary>An entity the physics moves, as the service keeps it: its body in the simulation, what the body was made
/// from, and where the entity was last stood.</summary>
internal sealed class PhysicsBodyLink
{
    public PhysicsBodyLink(RigidBody body, BodyParts parts, BodyLayout made, int key, int depth)
    {
        Entity = body.Owner;
        Body = body;
        Parts = parts;
        Made = made;
        Key = key;
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

    /// <summary>The body in the simulation.</summary>
    public BodyId Id { get; set; }

    /// <summary>What the body was made from, to see when it changes.</summary>
    public BodyParts Parts { get; set; }

    /// <summary>The layout the body was made from, to see when it changes.</summary>
    public BodyLayout Made { get; set; }

    /// <summary>Whether the entity hung under another when the body was made.</summary>
    public bool IsParented { get; }

    public int Key { get; }

    /// <summary>Whether the body was taken out; a link left in a list is passed over.</summary>
    public bool IsGone { get; set; }

    /// <summary>The entity's position the physics last gave it, or found when it made the body.</summary>
    public WorldPosition WrittenPosition { get; set; }

    /// <summary>The entity's rotation the physics last gave it, or found when it made the body.</summary>
    public QuaternionF WrittenRotation { get; set; }

    /// <summary>Where the body's origin stood when the entity was last stood there, or was made or put by hand.</summary>
    public Pose PlacedPose { get; set; }

    /// <summary>The entity's name when the body was made, for the log.</summary>
    public string Name { get; }

    public BodyKind Kind { get; set; }

    /// <summary>How many entities the entity hangs under; parents are stood before their children.</summary>
    public int Depth { get; }

    /// <summary>Whether the entity is stood at the next frame whatever its body and parent do: hung elsewhere, it no
    /// longer stands where its body does.</summary>
    public bool MustPlace { get; set; }

    /// <summary>Whether the log was told the entity's parent got stretched or mirrored, so it is told once.</summary>
    public bool WarnedOfParent { get; set; }

    /// <summary>The parent's world matrix the entity was last stood under; it is stood again when that changes.</summary>
    public Matrix4x4<double> ParentWorld { get; set; }
}
