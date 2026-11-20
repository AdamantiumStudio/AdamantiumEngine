namespace Adamantium.DarkMatter.Simulation;

/// <summary>Where a joint stood as a run of the stepper left it.</summary>
internal struct JointReading
{
    public int Slot;
    public int Generation;
    public float Position;
}
