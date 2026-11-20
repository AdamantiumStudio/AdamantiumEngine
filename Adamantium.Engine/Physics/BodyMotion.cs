using Adamantium.Mathematics;

namespace Adamantium.Engine.Physics;

/// <summary>Where a body's center of mass stood in the world and how the body moved after a step.</summary>
internal readonly struct BodyMotion
{
    public BodyMotion(int key, int generation, WorldPosition centerOfMass, Vector3F localCenterOfMass,
        QuaternionF rotation, Vector3F linearVelocity, Vector3F angularVelocity)
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

    public WorldPosition CenterOfMass { get; }

    /// <summary>The center of mass in the body's own axes.</summary>
    public Vector3F LocalCenterOfMass { get; }

    public QuaternionF Rotation { get; }

    public Vector3F LinearVelocity { get; }

    public Vector3F AngularVelocity { get; }
}
