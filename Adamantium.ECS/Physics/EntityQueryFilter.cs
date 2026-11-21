using Adamantium.DarkMatter;

namespace Adamantium.ECS.Physics;

/// <summary>What a query of the game's physics passes by.</summary>
public readonly struct EntityQueryFilter
{
    private readonly LayerMask? _layers;

    /// <summary>An entity whose body is passed by: the car a wheel is cast from, say; none for none.</summary>
    public Entity Except { get; init; }

    /// <summary>The layers whose bodies it meets (<see cref="IPhysicsService.FindLayer"/>); all unless told.</summary>
    public LayerMask Layers
    {
        get => _layers ?? LayerMask.All;
        init => _layers = value;
    }

    /// <summary>Whether it meets triggers too; they are passed by unless told.</summary>
    public bool MeetsTriggers { get; init; }
}
