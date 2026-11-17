namespace Adamantium.Engine.Physics;

/// <summary>A frame's force the physics thread applies step by step for the frame's length in the simulation; a body's
/// pushes follow one another as its frames did.</summary>
internal readonly struct PhysicsPush
{
    public PhysicsPush(int key, Vector3F force, float seconds)
    {
        Key = key;
        Force = force;
        Seconds = seconds;
    }

    public int Key { get; }

    public Vector3F Force { get; }

    /// <summary>How much longer it pushes, in seconds of the simulation.</summary>
    public float Seconds { get; }
}
