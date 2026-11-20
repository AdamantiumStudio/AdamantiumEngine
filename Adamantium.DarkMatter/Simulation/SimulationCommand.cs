using Adamantium.DarkMatter.Shapes;

namespace Adamantium.DarkMatter.Simulation;

/// <summary>One action asked of a <see cref="PhysicsSimulation"/>, checked when asked and done at the start of the next
/// step, in the order asked.</summary>
internal struct SimulationCommand
{
    public SimulationCommandKind Kind;

    /// <summary>The slot of the body or joint it is for, and the generation an add gives it.</summary>
    public int Slot;
    public int Generation;
    public Vector3F Vector;
    public WorldPosition Point;
    public Pose Pose;
    public BodyKind BodyKind;

    /// <summary>Which of the kinds asked of the body this is, counted by the caller.</summary>
    public int KindAsk;
    public CollisionShape Shape;

    /// <summary>The caller's own of each part of <see cref="Shape"/>, the body's once the core takes it.</summary>
    public object[] PartData;
    public PhysicsMaterial Material;
    public float Mass;
    public float Density;
    public float LinearDamping;
    public float AngularDamping;
    public float GravityScale;
    public bool CanSleep;
    public BodyDescription Body;
    public JointSettings Joint;
    public JointLimits? Limits;
    public JointMotor? Motor;
}
