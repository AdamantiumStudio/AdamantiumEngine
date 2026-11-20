namespace Adamantium.DarkMatter.Simulation;

/// <summary>What the stepper of a <see cref="PhysicsSimulation"/> keeps of one joint. Touched on the stepping thread
/// only.</summary>
internal sealed class CoreJoint
{
    public int Generation;
    public JointHandle Handle;

    /// <summary>The slots of its bodies; -1 for the world.</summary>
    public int FirstBody;
    public int SecondBody;

    /// <summary>Whether a change of it was refused in this run: only the first is told.</summary>
    public bool IsRefusedInRun;
}
