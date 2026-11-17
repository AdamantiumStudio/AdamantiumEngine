namespace Adamantium.Engine.Physics;

/// <summary>The force a body feels at every step, and the batch of commands that set it.</summary>
internal readonly struct PhysicsForce
{
    public PhysicsForce(Vector3F force, long batch)
    {
        Force = force;
        Batch = batch;
    }

    public Vector3F Force { get; }

    /// <summary>The batch the force came with; one replaced in the same batch never reached a step.</summary>
    public long Batch { get; }
}
