namespace Adamantium.Engine.Physics;

/// <summary>One request of the game, sent to the physics thread and done before its next step.</summary>
internal readonly struct PhysicsCommand
{
    public PhysicsCommand(PhysicsCommandKind kind, int key, Vector3F value, Vector3F point)
    {
        Kind = kind;
        Key = key;
        Value = value;
        Point = point;
    }

    public PhysicsCommandKind Kind { get; }

    /// <summary>The body asked about, as the service numbers them.</summary>
    public int Key { get; }

    public Vector3F Value { get; }

    /// <summary>Where an impulse strikes, in the physics' own coordinates.</summary>
    public Vector3F Point { get; }
}
