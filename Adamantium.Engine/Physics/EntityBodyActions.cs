using Adamantium.DarkMatter;
using Adamantium.ECS;
using Adamantium.Mathematics;

namespace Adamantium.Engine.Physics;

/// <summary>An entity's body in the game's physics as a chain: each action is the service's own, asked at once, in
/// order, and gives the chain back for the next; readings are as the last step left the body.</summary>
public readonly ref struct EntityBodyActions
{
    private readonly IPhysicsService _physics;

    internal EntityBodyActions(IPhysicsService physics, Entity entity)
    {
        _physics = physics;
        Entity = entity;
    }

    public Entity Entity { get; }

    public Vector3F LinearVelocity => _physics.GetLinearVelocity(Entity);
    public Vector3F AngularVelocity => _physics.GetAngularVelocity(Entity);
    public float Mass => _physics.GetMass(Entity);
    public bool IsSleeping => _physics.IsSleeping(Entity);

    public EntityBodyActions ApplyImpulse(Vector3F impulse)
    {
        _physics.ApplyImpulse(Entity, impulse);
        return this;
    }

    public EntityBodyActions ApplyImpulse(Vector3F impulse, WorldPosition point)
    {
        _physics.ApplyImpulse(Entity, impulse, point);
        return this;
    }

    public EntityBodyActions ApplyAngularImpulse(Vector3F impulse)
    {
        _physics.ApplyAngularImpulse(Entity, impulse);
        return this;
    }

    public EntityBodyActions SetLinearVelocity(Vector3F velocity)
    {
        _physics.SetLinearVelocity(Entity, velocity);
        return this;
    }

    public EntityBodyActions SetAngularVelocity(Vector3F velocity)
    {
        _physics.SetAngularVelocity(Entity, velocity);
        return this;
    }

    public EntityBodyActions SetForce(Vector3F force)
    {
        _physics.SetForce(Entity, force);
        return this;
    }

    public EntityBodyActions SetTorque(Vector3F torque)
    {
        _physics.SetTorque(Entity, torque);
        return this;
    }

    public EntityBodyActions MoveKinematic(in Pose pose)
    {
        _physics.MoveKinematic(Entity, pose);
        return this;
    }

    public EntityBodyActions WakeUp()
    {
        _physics.WakeUp(Entity);
        return this;
    }
}
