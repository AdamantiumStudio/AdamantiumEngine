namespace Adamantium.DarkMatter.Simulation;

/// <summary>A body as a run of the stepper left it.</summary>
internal struct BodyReading
{
    public int Slot;
    public int Generation;
    public Pose Pose;
    public WorldPosition CenterOfMass;
    public Vector3F LinearVelocity;
    public Vector3F AngularVelocity;
    public float Mass;
    public BodyKind Kind;
    public bool IsSleeping;

    /// <summary>Whether it was put where it stands by hand in the run: drawn from there, not from where it was.</summary>
    public bool Jumped;
}
