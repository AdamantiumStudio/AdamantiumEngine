using System;
using Adamantium.DarkMatter.Shapes;

namespace Adamantium.DarkMatter;

/// <summary>
/// One body of a <see cref="PhysicsSimulation"/> as a chain: each action is the simulation's own, asked at once, and
/// gives the chain back for the next; each reading is the simulation's too. Two words on the caller's stack.
/// </summary>
public readonly ref struct BodyActions
{
    private readonly PhysicsSimulation _simulation;

    internal BodyActions(PhysicsSimulation simulation, BodyId body)
    {
        _simulation = simulation;
        Id = body;
    }

    public BodyId Id { get; }

    public PhysicsStatus Status => _simulation.StatusOf(Id);
    public string Refusal => _simulation.RefusalOf(Id);
    public object Data => _simulation.DataOf(Id);
    public Pose Pose => _simulation.PoseOf(Id);
    public Pose InterpolatedPose => _simulation.InterpolatedPoseOf(Id);
    public WorldPosition CenterOfMass => _simulation.CenterOfMassOf(Id);
    public Vector3F LinearVelocity => _simulation.LinearVelocityOf(Id);
    public Vector3F AngularVelocity => _simulation.AngularVelocityOf(Id);
    public float Mass => _simulation.MassOf(Id);
    public BodyKind Kind => _simulation.KindOf(Id);
    public bool IsSleeping => _simulation.IsSleeping(Id);
    public Layer Layer => _simulation.LayerOf(Id);

    /// <summary>The caller's own given with part <paramref name="part"/>.</summary>
    public object DataOf(int part) => _simulation.DataOf(Id, part);

    public BodyActions ApplyImpulse(Vector3F impulse)
    {
        _simulation.ApplyImpulse(Id, impulse);
        return this;
    }

    public BodyActions ApplyImpulse(Vector3F impulse, WorldPosition point)
    {
        _simulation.ApplyImpulse(Id, impulse, point);
        return this;
    }

    public BodyActions ApplyAngularImpulse(Vector3F impulse)
    {
        _simulation.ApplyAngularImpulse(Id, impulse);
        return this;
    }

    public BodyActions SetLinearVelocity(Vector3F velocity)
    {
        _simulation.SetLinearVelocity(Id, velocity);
        return this;
    }

    public BodyActions SetAngularVelocity(Vector3F velocity)
    {
        _simulation.SetAngularVelocity(Id, velocity);
        return this;
    }

    public BodyActions SetForce(Vector3F force)
    {
        _simulation.SetForce(Id, force);
        return this;
    }

    public BodyActions SetTorque(Vector3F torque)
    {
        _simulation.SetTorque(Id, torque);
        return this;
    }

    public BodyActions SetPose(in Pose pose)
    {
        _simulation.SetPose(Id, pose);
        return this;
    }

    public BodyActions MoveKinematic(in Pose pose)
    {
        _simulation.MoveKinematic(Id, pose);
        return this;
    }

    public BodyActions WakeUp()
    {
        _simulation.WakeUp(Id);
        return this;
    }

    public BodyActions SetKind(BodyKind kind)
    {
        _simulation.SetKind(Id, kind);
        return this;
    }

    public BodyActions SetShape(CollisionShape shape)
    {
        _simulation.SetShape(Id, shape);
        return this;
    }

    public BodyActions SetParts(ReadOnlySpan<BodyPart> parts)
    {
        _simulation.SetParts(Id, parts);
        return this;
    }

    public BodyActions SetMaterial(in PhysicsMaterial material)
    {
        _simulation.SetMaterial(Id, material);
        return this;
    }

    public BodyActions SetLayer(Layer layer)
    {
        _simulation.SetLayer(Id, layer);
        return this;
    }

    public BodyActions SetMass(float mass, float density)
    {
        _simulation.SetMass(Id, mass, density);
        return this;
    }

    public BodyActions SetDamping(float linearDamping, float angularDamping)
    {
        _simulation.SetDamping(Id, linearDamping, angularDamping);
        return this;
    }

    public BodyActions SetGravityScale(float gravityScale)
    {
        _simulation.SetGravityScale(Id, gravityScale);
        return this;
    }

    public BodyActions SetCanSleep(bool canSleep)
    {
        _simulation.SetCanSleep(Id, canSleep);
        return this;
    }

    /// <summary>Takes the body out, its joints with it; the chain ends here.</summary>
    public void Remove() => _simulation.RemoveBody(Id);
}
