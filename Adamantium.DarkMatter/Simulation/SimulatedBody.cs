using Adamantium.DarkMatter.Shapes;

namespace Adamantium.DarkMatter.Simulation;

/// <summary>What a <see cref="PhysicsSimulation"/> keeps of one body on the caller's side: what the caller sees of it
/// after the last run read, and what the caller asked of it since. Touched on the caller's thread only.</summary>
internal sealed class SimulatedBody
{
    public int Generation;
    public PhysicsStatus Status;
    public string Refusal;
    public object Data;
    public object[] PartData = [];

    /// <summary>The shape the core took for it last, for the queries' snapshot.</summary>
    public CollisionShape Shape;
    public PartTraits[] PartTraits;

    /// <summary>Its layer after the last run read.</summary>
    public Layer Layer;

    /// <summary>Its material as last taken, for the queries' snapshot: what surface its parts with none of their own
    /// are.</summary>
    public PhysicsMaterial Material;

    /// <summary>Where it stands in the simulation's list of bodies alive.</summary>
    public int ListIndex;

    /// <summary>Where its origin stood after the last run read and after the one before: it is drawn between.</summary>
    public Pose Pose;
    public Pose PreviousPose;
    public WorldPosition CenterOfMass;
    public Vector3F LinearVelocity;
    public Vector3F AngularVelocity;
    public float Mass;
    public BodyKind Kind;
    public bool IsSleeping;

    /// <summary>The run read it last came in, by the caller's count; below zero for none yet.</summary>
    public long ReadIn;

    /// <summary>Where it was put by hand, and the batch of commands that took it there: it is drawn there until a run
    /// of that batch is read; zero for none under way.</summary>
    public Pose JumpPose;
    public long JumpBatch;

    /// <summary>The Update its snapshot copy was last listed to move in, by the caller's count.</summary>
    public long SnapshotMark;

    /// <summary>The last walk of a search that gave it, by the simulation's count of walks: a body taken out on the way
    /// moves another into its place, given once all the same.</summary>
    public long WalkedIn;

    /// <summary>The kind it will have once the commands sent are done, which the actions asked of it are checked by;
    /// how many kinds were asked of it, so only the last one asked settles it.</summary>
    public BodyKind KindAsked;
    public int KindAsks;
}
