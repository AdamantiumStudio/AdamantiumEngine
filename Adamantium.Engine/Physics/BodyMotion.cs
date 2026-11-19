namespace Adamantium.Engine.Physics;

/// <summary>Where a body's center of mass stood and how the body moved after a step, in the physics' own coordinates.
/// </summary>
internal readonly struct BodyMotion
{
    public BodyMotion(int key, int generation, Vector3F centerOfMass, Vector3F localCenterOfMass, QuaternionF rotation,
        Vector3F linearVelocity, Vector3F angularVelocity)
    {
        Key = key;
        Generation = generation;
        CenterOfMass = centerOfMass;
        LocalCenterOfMass = localCenterOfMass;
        Rotation = rotation;
        LinearVelocity = linearVelocity;
        AngularVelocity = angularVelocity;
    }

    public int Key { get; }

    /// <summary>Which body under the key moved: one put in its place later has another.</summary>
    public int Generation { get; }

    public Vector3F CenterOfMass { get; }

    /// <summary>The center of mass in the body's own axes.</summary>
    public Vector3F LocalCenterOfMass { get; }

    public QuaternionF Rotation { get; }

    public Vector3F LinearVelocity { get; }

    public Vector3F AngularVelocity { get; }
}
