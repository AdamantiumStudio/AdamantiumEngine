namespace Adamantium.Engine.Physics;

/// <summary>A force the physics thread keeps applying, step by step, until its time is used up or a newer push of the
/// same body comes.</summary>
internal readonly struct PhysicsPush
{
    public PhysicsPush(int key, Vector3F force, float seconds, long batch)
    {
        Key = key;
        Force = force;
        Seconds = seconds;
        Batch = batch;
    }

    public int Key { get; }

    public Vector3F Force { get; }

    /// <summary>How much longer it pushes, in seconds of the simulation.</summary>
    public float Seconds { get; }

    /// <summary>The batch of commands it came with.</summary>
    public long Batch { get; }
}
