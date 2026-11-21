using System;
using Adamantium.DarkMatter.Shapes;

namespace Adamantium.DarkMatter;

/// <summary>A body told link by link on the caller's stack - its <see cref="BodySettings"/> gathered, nothing kept -
/// and added by <see cref="Add"/>, as <see cref="PhysicsSimulation.AddBody"/> adds it.</summary>
public ref struct BodyBuilder
{
    private readonly PhysicsSimulation _simulation;
    private BodySettings _settings;

    internal BodyBuilder(PhysicsSimulation simulation, BodySettings settings)
    {
        _simulation = simulation;
        _settings = settings;
    }

    /// <summary>Moved by forces and what it meets: the default.</summary>
    public BodyBuilder Dynamic() => With(_settings with { Kind = BodyKind.Dynamic });

    /// <summary>Moved only by the caller, pushing what it meets.</summary>
    public BodyBuilder Kinematic() => With(_settings with { Kind = BodyKind.Kinematic });

    /// <summary>Never moved.</summary>
    public BodyBuilder Static() => With(_settings with { Kind = BodyKind.Static });

    public BodyBuilder WithShape(CollisionShape shape) => With(_settings with { Shape = shape });

    /// <summary>The shapes of a body of several, copied when added; the span is not kept past that.</summary>
    public BodyBuilder WithParts(ReadOnlySpan<BodyPart> parts) => With(_settings with { Parts = parts });

    /// <summary>Where its origin stands.</summary>
    public BodyBuilder At(WorldPosition position) => With(_settings with { Position = position });

    public BodyBuilder Turned(QuaternionF rotation) => With(_settings with { Rotation = rotation });

    public BodyBuilder Moving(Vector3F linearVelocity) => With(_settings with { LinearVelocity = linearVelocity });

    public BodyBuilder Spinning(Vector3F angularVelocity) => With(_settings with { AngularVelocity = angularVelocity });

    /// <summary>Weighed by its mass in kilograms.</summary>
    public BodyBuilder WithMass(float mass) => With(_settings with { Mass = mass });

    /// <summary>Weighed by its density in kilograms per cubic meter, with no mass told.</summary>
    public BodyBuilder WithDensity(float density) => With(_settings with { Density = density });

    public BodyBuilder WithMaterial(in PhysicsMaterial material) => With(_settings with { Material = material });

    public BodyBuilder OnLayer(Layer layer) => With(_settings with { Layer = layer });

    public BodyBuilder WithDamping(float linearDamping, float angularDamping) =>
        With(_settings with { LinearDamping = linearDamping, AngularDamping = angularDamping });

    public BodyBuilder WithGravityScale(float gravityScale) => With(_settings with { GravityScale = gravityScale });

    /// <summary>Kept awake at rest.</summary>
    public BodyBuilder NeverSleeping() => With(_settings with { CanSleep = false });

    /// <summary>The caller's own, given back by <see cref="PhysicsSimulation.DataOf(BodyId)"/>.</summary>
    public BodyBuilder WithData(object data) => With(_settings with { Data = data });

    /// <summary>Adds the body as told and gives its id.</summary>
    public BodyId Add() => _simulation.AddBody(_settings);

    private BodyBuilder With(BodySettings settings)
    {
        _settings = settings;
        return this;
    }
}
