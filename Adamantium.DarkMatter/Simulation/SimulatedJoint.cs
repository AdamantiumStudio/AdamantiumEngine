namespace Adamantium.DarkMatter.Simulation;

/// <summary>What a <see cref="PhysicsSimulation"/> keeps of one joint.</summary>
internal sealed class SimulatedJoint
{
    public int Generation;
    public PhysicsStatus Status;
    public string Refusal;
    public JointHandle Handle;

    /// <summary>The slots of its bodies; -1 for the world.</summary>
    public int FirstBody;
    public int SecondBody;

    /// <summary>Where it stands after the last step: a hinge's angle, a slider's offset, a distance joint's length.
    /// </summary>
    public float Position;

    /// <summary>Where it stands in the simulation's list of joints alive.</summary>
    public int ListIndex;
}
