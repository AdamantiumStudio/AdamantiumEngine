namespace Adamantium.DarkMatter;

/// <summary>What a joint is when it is added: its kind, its bodies, and where it stands in the world then. The world
/// remembers the point and the axis on each body, so the joint holds them as they stood.</summary>
public sealed class JointDescription
{
    public JointKind Kind { get; set; } = JointKind.Ball;

    /// <summary>The first body.</summary>
    public BodyHandle First { get; set; }

    /// <summary>The second body; a default handle holds the first to the world.</summary>
    public BodyHandle Second { get; set; }

    /// <summary>Where the joint stands in the world when it is added: the shared point, or a distance joint's end on the
    /// first body.</summary>
    public Vector3F Point { get; set; }

    /// <summary>A distance joint's end on the second body, in the world when it is added.</summary>
    public Vector3F SecondPoint { get; set; }

    /// <summary>The axis a hinge turns about or a slider slides along, in the world when it is added.</summary>
    public Vector3F Axis { get; set; } = Vector3F.UnitY;

    /// <summary>Whether the two bodies still collide with each other; as Jolt has it, they do not.</summary>
    public bool CollideConnected { get; set; }

    /// <summary>Whether <see cref="LowerLimit"/> and <see cref="UpperLimit"/> hold: a hinge's angle in radians, a
    /// slider's offset or a distance joint's length in meters, from where it stood when added - a distance joint's
    /// from zero.</summary>
    public bool HasLimits { get; set; }

    public float LowerLimit { get; set; }

    public float UpperLimit { get; set; }

    /// <summary>The speed a motor drives a hinge at, in radians per second, or a slider at, in meters per second.
    /// </summary>
    public float MotorSpeed { get; set; }

    /// <summary>The most torque, in newton meters, or force, in newtons, the motor gives; zero for no motor.</summary>
    public float MaxMotorForce { get; set; }

    /// <summary>A distance joint's spring, in hertz, pulling toward the length it had when added; zero for a rigid rod.
    /// </summary>
    public float SpringHertz { get; set; }

    /// <summary>How the spring's swing dies: 1 not at all past its rest.</summary>
    public float SpringDampingRatio { get; set; } = 1;
}
