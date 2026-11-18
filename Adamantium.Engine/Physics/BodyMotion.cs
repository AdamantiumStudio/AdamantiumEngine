namespace Adamantium.Engine.Physics;

/// <summary>Where a body's center of mass stood and how the body moved after a step, in the physics' own coordinates.
/// </summary>
internal readonly struct BodyMotion
{
    public BodyMotion(int key, Vector3F centerOfMass, QuaternionF rotation, Vector3F linearVelocity,
        Vector3F angularVelocity)
    {
        Key = key;
        CenterOfMass = centerOfMass;
        Rotation = rotation;
        LinearVelocity = linearVelocity;
        AngularVelocity = angularVelocity;
    }

    public int Key { get; }

    public Vector3F CenterOfMass { get; }

    public QuaternionF Rotation { get; }

    public Vector3F LinearVelocity { get; }

    public Vector3F AngularVelocity { get; }
}
