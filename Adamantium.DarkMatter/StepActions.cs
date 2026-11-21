using Adamantium.DarkMatter.Simulation;

namespace Adamantium.DarkMatter;

/// <summary>What an <see cref="IStepHook"/> reads of the bodies and does to them in the step about to be solved: their
/// state as the steps left it so far, and pushes felt in this step alone. A push reaches only a body that can take it -
/// a force, a torque or an impulse a dynamic one, a velocity one that moves - as the simulation's own actions do. A body
/// the simulation does not hold now is refused; <see cref="Contains"/> tells.</summary>
public readonly ref struct StepActions
{
    private readonly SimulationStepper _stepper;

    internal StepActions(SimulationStepper stepper) => _stepper = stepper;

    /// <summary>How long the step is, in seconds.</summary>
    public float StepSeconds => _stepper.World.StepSeconds;

    /// <summary>The pull every dynamic body feels, in meters per second squared.</summary>
    public Vector3F Gravity => _stepper.World.Gravity;

    /// <summary>Whether the simulation holds <paramref name="body"/> in this step.</summary>
    public bool Contains(BodyId body) => _stepper.HasBody(body);

    public BodyKind KindOf(BodyId body) => _stepper.World.GetKind(_stepper.FindHandle(body));

    public Pose PoseOf(BodyId body) => _stepper.World.GetPose(_stepper.FindHandle(body));

    public WorldPosition CenterOfMassOf(BodyId body) => _stepper.World.GetCenterOfMass(_stepper.FindHandle(body));

    public Vector3F LinearVelocityOf(BodyId body) => _stepper.World.GetLinearVelocity(_stepper.FindHandle(body));

    public Vector3F AngularVelocityOf(BodyId body) => _stepper.World.GetAngularVelocity(_stepper.FindHandle(body));

    /// <summary>The mass of <paramref name="body"/> as the simulation weighed it, in kilograms.</summary>
    public float MassOf(BodyId body) => _stepper.World.GetMass(_stepper.FindHandle(body));

    /// <summary>Pushes the center of <paramref name="body"/> in this step alone, in newtons.</summary>
    public void ApplyForce(BodyId body, Vector3F force)
    {
        if (FindDynamic(body, out var handle))
            _stepper.World.ApplyForce(handle, force);
    }

    /// <summary>Turns <paramref name="body"/> in this step alone, in newton meters.</summary>
    public void ApplyTorque(BodyId body, Vector3F torque)
    {
        if (FindDynamic(body, out var handle))
            _stepper.World.ApplyTorque(handle, torque);
    }

    /// <summary>Strikes <paramref name="body"/> at <paramref name="point"/> of the world, in newton seconds.</summary>
    public void ApplyImpulse(BodyId body, Vector3F impulse, WorldPosition point)
    {
        if (FindDynamic(body, out var handle))
            _stepper.World.ApplyImpulse(handle, impulse, point);
    }

    /// <summary>Sets the velocity of <paramref name="body"/>, kept past a move to a pose asked before.</summary>
    public void SetLinearVelocity(BodyId body, Vector3F velocity)
    {
        if (FindMoving(body, out var handle))
            _stepper.SetLinearVelocity(body, handle, velocity);
    }

    /// <summary>Sets the spin of <paramref name="body"/>, kept past a move to a pose asked before.</summary>
    public void SetAngularVelocity(BodyId body, Vector3F velocity)
    {
        if (FindMoving(body, out var handle))
            _stepper.SetAngularVelocity(body, handle, velocity);
    }

    private bool FindDynamic(BodyId body, out BodyHandle handle)
    {
        handle = _stepper.FindHandle(body);
        return _stepper.World.GetKind(handle) == BodyKind.Dynamic;
    }

    private bool FindMoving(BodyId body, out BodyHandle handle)
    {
        handle = _stepper.FindHandle(body);
        return _stepper.World.GetKind(handle) != BodyKind.Static;
    }
}
