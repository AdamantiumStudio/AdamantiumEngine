using Adamantium.DarkMatter;
using Adamantium.ECS;

namespace Adamantium.Engine.Physics;

/// <summary>An entity the physics moves, as the game's side keeps it: where its shape stands on it, and the last two
/// poses the physics sent, to draw between.</summary>
internal sealed class PhysicsBodyLink
{
    public PhysicsBodyLink(Entity entity, BodyKind kind, in ColliderShape shape, Vector3F position, QuaternionF rotation)
    {
        Entity = entity;
        Kind = kind;
        Offset = shape.Offset;
        Turn = shape.Turn;
        PreviousPosition = position;
        PreviousRotation = rotation;
        CurrentPosition = position;
        CurrentRotation = rotation;
    }

    public Entity Entity { get; }

    public BodyKind Kind { get; }

    public Vector3F Offset { get; }

    public QuaternionF Turn { get; }

    public Vector3F PreviousPosition { get; set; }

    public QuaternionF PreviousRotation { get; set; }

    public Vector3F CurrentPosition { get; set; }

    public QuaternionF CurrentRotation { get; set; }

    public Vector3F LinearVelocity { get; set; }

    public Vector3F AngularVelocity { get; set; }

    /// <summary>The number of the last result that brought the body a pose; it is drawn moving until a result later.
    /// </summary>
    public long MovedInResult { get; set; } = -2;
}
