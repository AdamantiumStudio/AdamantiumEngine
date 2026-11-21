using Adamantium.DarkMatter;
using Adamantium.Mathematics;

namespace Adamantium.ECS.Physics;

/// <summary>The game's physics world as a chain: each action is the service's own, asked at once, in order, and gives
/// the chain back for the next.</summary>
public readonly ref struct PhysicsWorldActions
{
    private readonly IPhysicsService _physics;

    internal PhysicsWorldActions(IPhysicsService physics) => _physics = physics;

    /// <summary>Sets <see cref="IPhysicsService.Gravity"/>.</summary>
    public PhysicsWorldActions SetGravity(Vector3F gravity)
    {
        _physics.Gravity = gravity;
        return this;
    }

    public PhysicsWorldActions WakeUp(in BoundingBox<double> area)
    {
        _physics.WakeUp(area);
        return this;
    }

    public PhysicsWorldActions ApplyRadialImpulse(WorldPosition center, float radius, float strength)
    {
        _physics.ApplyRadialImpulse(center, radius, strength);
        return this;
    }
}
