namespace Adamantium.Engine.Physics;

/// <summary>Where a body stood and how it moved after a step, in the physics' own coordinates.</summary>
internal readonly struct BodyMotion
{
    public BodyMotion(int key, Vector3F position, QuaternionF rotation, Vector3F linearVelocity, Vector3F angularVelocity)
    {
        Key = key;
        Position = position;
        Rotation = rotation;
        LinearVelocity = linearVelocity;
        AngularVelocity = angularVelocity;
    }

    public int Key { get; }

    public Vector3F Position { get; }

    public QuaternionF Rotation { get; }

    public Vector3F LinearVelocity { get; }

    public Vector3F AngularVelocity { get; }
}
