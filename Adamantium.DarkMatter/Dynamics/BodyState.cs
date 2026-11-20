using Adamantium.DarkMatter.Shapes;

namespace Adamantium.DarkMatter.Dynamics;

/// <summary>Everything a world keeps about one body, in one slot of its array.</summary>
internal struct BodyState
{
    public bool IsUsed;
    public int Generation;
    public BodyKind Kind;
    public int FirstPart;

    /// <summary>How far the body reaches from its center of mass.</summary>
    public float Reach;
    public BodyMarks Marks;
    public Contact Contacts;

    public WorldPosition Position;
    public QuaternionF Rotation;
    public WorldPosition StartPosition;
    public QuaternionF StartRotation;
    public Vector3F LinearVelocity;
    public Vector3F AngularVelocity;
    public Vector3F Force;
    public Vector3F Torque;

    public float InverseMass;
    public Vector3F InverseInertia;

    /// <summary>How the axes of <see cref="InverseInertia"/> are turned against the body's own.</summary>
    public QuaternionF InertiaRotation;
    public Matrix3x3F WorldInverseInertia;

    /// <summary>The center of mass in the body's own axes: where <see cref="Position"/> is from the body's origin.
    /// </summary>
    public Vector3F CenterOfMass;

    /// <summary>The whole shape, and the mass and density it is weighed by, to weigh it again.</summary>
    public CollisionShape Shape;
    public float Mass;
    public float Density;

    public float Friction;
    public float Restitution;
    public float LinearDamping;
    public float AngularDamping;
    public float GravityScale;

    public bool CanSleep;
    public bool IsAwake;

    /// <summary>Whether a force or torque was applied for the next step, so the body does not count it toward sleep.
    /// </summary>
    public bool IsPushed;
    public float SleepTime;
    public int IslandHead;
    public int IslandNext;

    public readonly bool IsDynamic => Kind == BodyKind.Dynamic;

    /// <summary>What it weighs, in kilograms; zero for one not dynamic.</summary>
    public readonly float WeighedMass => InverseMass > 0 ? 1f / InverseMass : 0;

    public readonly bool IsMoved => (Marks & BodyMarks.Moved) != 0;

    public readonly bool IsNew => (Marks & BodyMarks.New) != 0;

    public readonly bool IsPlaced => (Marks & BodyMarks.Placed) != 0;

    /// <summary>Whether the body moves this step: awake and not static.</summary>
    public readonly bool IsMoving => IsAwake && Kind != BodyKind.Static;

    /// <summary>Where the body's origin stands and how the body is turned.</summary>
    public readonly Pose OriginPose => new(Position - Vector3F.Transform(CenterOfMass, Rotation), Rotation);

    /// <summary>How far the center of mass moved since the step started.</summary>
    public readonly Vector3F MovedThisStep => Position.OffsetFrom(StartPosition);

    /// <summary>The inverse inertia around the world's axes, as the body is turned now.</summary>
    public readonly Matrix3x3F MeasureWorldInverseInertia() => InertiaMath.ToWorld(Rotation * InertiaRotation, InverseInertia);
}
