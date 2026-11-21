namespace Adamantium.DarkMatter;

/// <summary>A joint told link by link on the caller's stack - its <see cref="JointSettings"/> gathered - and added by
/// <see cref="Add"/>, as <see cref="PhysicsSimulation.AddJoint"/> adds it.</summary>
public ref struct JointBuilder
{
    private readonly PhysicsSimulation _simulation;
    private JointSettings _settings;

    internal JointBuilder(PhysicsSimulation simulation, JointSettings settings)
    {
        _simulation = simulation;
        _settings = settings;
    }

    public JointBuilder Fixed() => With(_settings with { Kind = JointKind.Fixed });

    /// <summary>A ball in a socket: the default.</summary>
    public JointBuilder Ball() => With(_settings with { Kind = JointKind.Ball });

    public JointBuilder Hinge() => With(_settings with { Kind = JointKind.Hinge });

    public JointBuilder Slider() => With(_settings with { Kind = JointKind.Slider });

    public JointBuilder Distance() => With(_settings with { Kind = JointKind.Distance });

    /// <summary>The two bodies it holds; none for the second holds the first to the world.</summary>
    public JointBuilder Between(BodyId first, BodyId second = default) =>
        With(_settings with { First = first, Second = second });

    /// <summary>In the world: the shared point, or a distance joint's end on the first body.</summary>
    public JointBuilder At(WorldPosition point) => With(_settings with { Space = JointSpace.World, Point = point });

    /// <summary>In the world: a distance joint's end on the second body, or in the world with no second.</summary>
    public JointBuilder To(WorldPosition secondPoint) => With(_settings with { SecondPoint = secondPoint });

    /// <summary>On the bodies: the point, or a distance joint's ends, each in its body's own axes.</summary>
    public JointBuilder OnBodies(Vector3F firstAnchor, Vector3F secondAnchor = default) =>
        With(_settings with { Space = JointSpace.Bodies, FirstAnchor = firstAnchor, SecondAnchor = secondAnchor });

    /// <summary>The axis a hinge turns about or a slider slides along.</summary>
    public JointBuilder Along(Vector3F axis) => With(_settings with { Axis = axis });

    /// <summary>Its two bodies still collide with each other.</summary>
    public JointBuilder Colliding() => With(_settings with { CollideConnected = true });

    public JointBuilder WithLimits(JointLimits limits) => With(_settings with { Limits = limits });

    public JointBuilder WithMotor(JointMotor motor) => With(_settings with { Motor = motor });

    public JointBuilder WithSpring(JointSpring spring) => With(_settings with { Spring = spring });

    /// <summary>Adds the joint as told and gives its id.</summary>
    public JointId Add() => _simulation.AddJoint(_settings);

    private JointBuilder With(JointSettings settings)
    {
        _settings = settings;
        return this;
    }
}
