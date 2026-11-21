using Adamantium.ECS;

namespace Adamantium.Engine.Physics;

/// <summary>What a query of the game's physics passes by.</summary>
public readonly struct EntityQueryFilter
{
    /// <summary>An entity whose body is passed by: the car a wheel is cast from, say; none for none.</summary>
    public Entity Except { get; init; }

    /// <summary>Whether it meets triggers too; they are passed by unless told.</summary>
    public bool MeetsTriggers { get; init; }
}
