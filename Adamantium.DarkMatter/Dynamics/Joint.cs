namespace Adamantium.DarkMatter.Dynamics;

/// <summary>A joint as a world keeps it: its bodies, and its points and axes in each body's own axes, from the body's
/// origin - so a body weighed anew, its center of mass moved, keeps them where they were.</summary>
internal sealed class Joint
{
    /// <summary>The most constraints a joint solves: a slider's two sideways, three turns, a limit each way and a
    /// motor.</summary>
    public const int ConstraintCapacity = 8;

    /// <summary>The constraint a hinge's or a slider's motor holds its impulse in.</summary>
    public const int MotorConstraint = 0;

    /// <summary>The constraints a hinge's, a slider's or a distance joint's lower and upper limits hold their impulses
    /// in.</summary>
    public const int LowerLimitConstraint = 1;
    public const int UpperLimitConstraint = 2;

    public bool IsUsed;
    public int Generation;
    public JointKind Kind;
    public int First;

    /// <summary>The second body; <see cref="PhysicsWorld"/>'s no-body for the world.</summary>
    public int Second;
    public bool CollideConnected;

    /// <summary>The joint's point on each body, in its own axes from its origin; the world's in the world.</summary>
    public Vector3F PointOnFirst;
    public Vector3F PointOnSecond;

    /// <summary>The axis on each body, in its own axes, of length one.</summary>
    public Vector3F AxisOnFirst;
    public Vector3F AxisOnSecond;

    /// <summary>A direction square to the axis on each body, the same in the world when added: a hinge's angle is the
    /// turn between them.</summary>
    public Vector3F ReferenceOnFirst;
    public Vector3F ReferenceOnSecond;

    /// <summary>How the second body was turned against the first when added: a weld and a slider keep it.</summary>
    public QuaternionF RestTurn;

    /// <summary>A distance joint's length when added.</summary>
    public float RestLength;
    public bool HasLimits;
    public float LowerLimit;
    public float UpperLimit;
    public float MotorSpeed;
    public float MaxMotorForce;
    public float SpringHertz;
    public float SpringDampingRatio;

    /// <summary>The impulse each constraint ended the last substep with, to start the next.</summary>
    public readonly float[] Impulses = new float[ConstraintCapacity];

    public int ListIndex;

    /// <summary>Where the world keeps it: its handle's index.</summary>
    public int Slot;
}
