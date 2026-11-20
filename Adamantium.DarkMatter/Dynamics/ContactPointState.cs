namespace Adamantium.DarkMatter.Dynamics;

/// <summary>One point of a contact: where it holds on each body, what it pushed last, and what the solver prepared.
/// </summary>
internal struct ContactPointState
{
    /// <summary>Where the point stands in the world, as reported.</summary>
    public WorldPosition Position;
    public Vector3F LocalAnchorFirst;
    public Vector3F LocalAnchorSecond;
    public float Separation;

    public Vector3F AnchorFirst;
    public Vector3F AnchorSecond;
    public float AdjustedSeparation;

    public float NormalImpulse;
    public float MaxNormalImpulse;
    public Vector3F FrictionImpulse;

    public float NormalMass;
    public float TangentMass;
    public float BitangentMass;
    public float RelativeVelocity;
}
