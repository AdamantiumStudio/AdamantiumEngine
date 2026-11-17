using Adamantium.DarkMatter.Shapes;

namespace Adamantium.DarkMatter.Dynamics;

/// <summary>Everything a world keeps about one body, in one slot of its array.</summary>
internal struct BodyState
{
    public bool IsUsed;
    public int Generation;
    public BodyKind Kind;
    public CollisionShape Shape;
    public int Proxy;
    public BodyMarks Marks;
    public Contact Contacts;

    public Vector3F Position;
    public QuaternionF Rotation;
    public Vector3F StartPosition;
    public QuaternionF StartRotation;
    public Vector3F LinearVelocity;
    public Vector3F AngularVelocity;
    public Vector3F Force;
    public Vector3F Torque;

    public float InverseMass;
    public Vector3F InverseInertia;
    public Matrix3x3F WorldInverseInertia;

    public float Friction;
    public float Restitution;
    public float LinearDamping;
    public float AngularDamping;
    public float GravityScale;

    public bool CanSleep;
    public bool IsAwake;
    public float SleepTime;
    public int IslandHead;
    public int IslandNext;

    public readonly bool IsDynamic => Kind == BodyKind.Dynamic;

    public readonly bool IsMoved => (Marks & BodyMarks.Moved) != 0;

    public readonly bool IsNew => (Marks & BodyMarks.New) != 0;

    public readonly bool IsPlaced => (Marks & BodyMarks.Placed) != 0;

    /// <summary>Whether the body moves this step: awake and not static.</summary>
    public readonly bool IsMoving => IsAwake && Kind != BodyKind.Static;

    public readonly Pose Pose => new(Position, Rotation);
}
