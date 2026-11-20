namespace Adamantium.DarkMatter;

/// <summary>What a joint is when <see cref="PhysicsSimulation.AddJoint"/> adds it: its kind, its bodies, and where it
/// stands - in the world, or on each body. The simulation remembers the point and the axis on each body.</summary>
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

    /// <summary>Where its points and axis are given; in the world unless told.</summary>
    public JointSpace Space { get; init; } = JointSpace.World;

    /// <summary>In the world: the shared point, or a distance joint's end on the first body.</summary>
    public WorldPosition Point { get; init; }

    /// <summary>In the world: a distance joint's end on the second body; with none, its end in the world, in either
    /// space.</summary>
    public WorldPosition SecondPoint { get; init; }

    /// <summary>On the bodies: the shared point, or a distance joint's end, in the first body's axes.</summary>
    public Vector3F FirstAnchor { get; init; }

    /// <summary>On the bodies: a distance joint's end in the second body's axes.</summary>
    public Vector3F SecondAnchor { get; init; }

    /// <summary>The axis a hinge turns about or a slider slides along: in the world, or in the first body's axes.
    /// </summary>
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
