namespace Adamantium.DarkMatter.Simulation;

/// <summary>What a <see cref="PhysicsSimulation"/> keeps of one body: what the caller sees of it after the last step,
/// what the caller asked of it since, and the core's handle.</summary>
internal sealed class SimulatedBody
{
    public int Generation;
    public PhysicsStatus Status;
    public string Refusal;
    public object Data;
    public object[] PartData = [];
    public BodyHandle Handle;

    /// <summary>Where it stands in the simulation's list of bodies alive.</summary>
    public int ListIndex;

    public Pose Pose;
    public WorldPosition CenterOfMass;
    public Vector3F LinearVelocity;
    public Vector3F AngularVelocity;
    public float Mass;
    public BodyKind Kind;
    public bool IsSleeping;

    /// <summary>The kind it will have once the commands sent are done, which the actions asked of it are checked by.
    /// </summary>
    public BodyKind KindAsked;

    /// <summary>What pushes and turns it at every step until set again.</summary>
    public Vector3F Force;
    public Vector3F Torque;

    /// <summary>Whether it is in the simulation's list of bodies pushed at every step.</summary>
    public bool IsPushed;

    /// <summary>Whether it is in the simulation's list of bodies read back after every step: those not static.</summary>
    public bool IsFollowed;

    /// <summary>The step it was last read at, by the simulation's count: its joints are read with it.</summary>
    public long ReadAt;

    /// <summary>Whether a command changed it, so it is read after this step whatever it was.</summary>
    public bool MustRead;

    /// <summary>Whether it is in the simulation's list of bodies moved to a pose over this step.</summary>
    public bool IsMovedToPose;

    /// <summary>Whether its velocity, or its spin, was set to move it to a pose over this step, and stops once the step
    /// is done; a velocity set after it is kept.</summary>
    public bool StopsMoving;
    public bool StopsTurning;
}
