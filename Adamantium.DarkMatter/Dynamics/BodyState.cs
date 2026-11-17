using Adamantium.DarkMatter.Shapes;

namespace Adamantium.DarkMatter.Dynamics;

/// <summary>Everything a world keeps about one body, in one slot of its array.</summary>
internal struct BodyState
{
    public bool IsUsed;
    public int Generation;
    public BodyKind Kind;
    public Shape Shape;
    public int Proxy;
    public bool IsMoved;
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

    /// <summary>Whether the body moves this step: awake and not static.</summary>
    public readonly bool IsMoving => IsAwake && Kind != BodyKind.Static;

    public readonly Pose Pose => new(Position, Rotation);
}
