using Adamantium.DarkMatter;

namespace Adamantium.ECS.Physics;

/// <summary>The entity of a body a game's physics found (<see cref="IPhysicsService.Find"/>).</summary>
public static class BodyViewEntity
{
    extension(BodyView body)
    {
        /// <summary>The entity whose rigid body it is; null for a body the game's physics did not make.</summary>
        public Entity Entity => body.Data as Entity;
    }
}
