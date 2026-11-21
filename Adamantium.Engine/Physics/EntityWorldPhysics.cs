using Adamantium.ECS;

namespace Adamantium.Engine.Physics;

/// <summary>The physics of an entity world.</summary>
public static class EntityWorldPhysics
{
    /// <summary>The world's <see cref="IPhysicsService"/>: the one way the game's code works with the physics; null for
    /// a world with none.</summary>
    public static IPhysicsService Physics(this EntityWorld world) => world.GetService<IPhysicsService>();
}
