namespace Adamantium.DarkMatter.Simulation;

/// <summary>What the stepper of a <see cref="PhysicsSimulation"/> keeps of one body: the core's handle and what it does
/// to the body at every step. Touched on the stepping thread only.</summary>
internal sealed class CoreBody
{
    public int Generation;
    public BodyHandle Handle;

    /// <summary>What pushes and turns it at every step until set again.</summary>
    public Vector3F Force;
    public Vector3F Torque;

    /// <summary>Whether it is in the stepper's list of bodies pushed at every step.</summary>
    public bool IsPushed;

    /// <summary>Whether it is in the stepper's list of bodies read after every step: those not static.</summary>
    public bool IsFollowed;

    /// <summary>Whether a command changed it, so it is read after this run whatever it was.</summary>
    public bool MustRead;

    /// <summary>Whether it slept at its last reading: a sleeper still asleep has nothing new.</summary>
    public bool WasSleeping;

    /// <summary>The run it was last read in: its joints are read with it.</summary>
    public long ReadIn;

    /// <summary>Whether it is in the stepper's list of bodies moved to a pose over the next step.</summary>
    public bool IsMovedToPose;

    /// <summary>Whether its velocity, or its spin, was set to move it to a pose over the next step, and stops once the
    /// step is done; a velocity set after it is kept.</summary>
    public bool StopsMoving;
    public bool StopsTurning;

    /// <summary>Whether it was put somewhere by hand in this run: a jump, not drawn as a motion.</summary>
    public bool Jumped;

    /// <summary>Whether a change of it was refused in this run: only the first is told.</summary>
    public bool IsRefusedInRun;
}
