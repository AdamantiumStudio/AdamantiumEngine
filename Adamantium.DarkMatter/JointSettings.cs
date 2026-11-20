namespace Adamantium.DarkMatter;

/// <summary>What a joint is when <see cref="PhysicsSimulation.AddJoint"/> adds it: its kind, its bodies, and where it
/// stands in the world then. The simulation remembers the point and the axis on each body.</summary>
public readonly struct JointSettings
{
    public JointSettings()
    {
    }

    /// <summary>How it holds; a ball unless told.</summary>
    public JointKind Kind { get; init; } = JointKind.Ball;

    /// <summary>The first body.</summary>
    public BodyId First { get; init; }

    /// <summary>The second body; none holds the first to the world.</summary>
    public BodyId Second { get; init; }

    /// <summary>Where it stands when added: the shared point, or a distance joint's end on the first body.</summary>
    public WorldPosition Point { get; init; }

    /// <summary>A distance joint's end on the second body, in the world when added.</summary>
    public WorldPosition SecondPoint { get; init; }

    /// <summary>The axis a hinge turns about or a slider slides along, in the world when added.</summary>
    public Vector3F Axis { get; init; } = Vector3F.UnitY;

    /// <summary>Whether its two bodies still collide with each other; they do not unless told.</summary>
    public bool CollideConnected { get; init; }

    /// <summary>How far it may go; none for freely.</summary>
    public JointLimits? Limits { get; init; }

    /// <summary>What drives a hinge or a slider; none for nothing.</summary>
    public JointMotor? Motor { get; init; }

    /// <summary>A distance joint's spring; none for a rigid rod.</summary>
    public JointSpring? Spring { get; init; }
}
