using Adamantium.DarkMatter;
using Adamantium.ECS;
using Adamantium.Mathematics;

namespace Adamantium.Engine.Physics;

/// <summary>The physics of a game: moves the entities with a <see cref="ECS.Components.RigidBody"/> while it plays, on a
/// thread of its own, and takes the game's pushes. Down is +Y in the engine's world, so gravity is too.</summary>
public interface IPhysicsService : IGameService
{
    /// <summary>How the next game's physics steps and solves; a game playing keeps the settings it started with.
    /// </summary>
    PhysicsSettings Settings { get; set; }

    /// <summary>The pull every dynamic body feels while the game plays, in meters per second squared.</summary>
    Vector3F Gravity { get; set; }

    /// <summary>What the last step of the game's physics did.</summary>
    StepStatistics LastStep { get; }

    /// <summary>Whether the physics moves <paramref name="entity"/> in the game playing.</summary>
    bool Moves(Entity entity);

    /// <summary>The velocity of <paramref name="entity"/>'s body as the last step left it, in meters per second.</summary>
    Vector3F GetLinearVelocity(Entity entity);

    /// <summary>The spin of <paramref name="entity"/>'s body as the last step left it, in radians per second.</summary>
    Vector3F GetAngularVelocity(Entity entity);

    /// <summary>Sets the velocity of <paramref name="entity"/>'s dynamic or kinematic body from the next step.</summary>
    void SetLinearVelocity(Entity entity, Vector3F velocity);

    /// <summary>Sets the spin of <paramref name="entity"/>'s dynamic or kinematic body from the next step.</summary>
    void SetAngularVelocity(Entity entity, Vector3F velocity);

    /// <summary>Pushes the center of <paramref name="entity"/>'s dynamic body through the next step, in newtons.</summary>
    void ApplyForce(Entity entity, Vector3F force);

    /// <summary>Strikes the center of <paramref name="entity"/>'s dynamic body at the next step, in newton seconds.
    /// </summary>
    void ApplyImpulse(Entity entity, Vector3F impulse);

    /// <summary>Strikes <paramref name="entity"/>'s dynamic body at <paramref name="point"/> of the world at the next
    /// step, in newton seconds.</summary>
    void ApplyImpulse(Entity entity, Vector3F impulse, Vector3<double> point);
}
