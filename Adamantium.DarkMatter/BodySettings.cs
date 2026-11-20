using System;
using Adamantium.DarkMatter.Shapes;

namespace Adamantium.DarkMatter;

/// <summary>What a body is when <see cref="PhysicsSimulation.AddBody"/> adds it, on the caller's stack - its parts a
/// span on the stack too; nothing of it is kept but what the body is made of.</summary>
public ref struct BodySettings
{
    public BodySettings()
    {
    }

    /// <summary>How the body moves; dynamic unless told.</summary>
    public BodyKind Kind { get; init; } = BodyKind.Dynamic;

    /// <summary>The shape, given around the body's origin; or <see cref="Parts"/>, for several.</summary>
    public CollisionShape Shape { get; init; }

    /// <summary>The shapes of a body of several, each with the caller's own; copied, the span is not kept.</summary>
    public ReadOnlySpan<BodyPart> Parts { get; init; }

    /// <summary>Where the body's origin stands.</summary>
    public WorldPosition Position { get; init; }

    /// <summary>How the body is turned.</summary>
    public QuaternionF Rotation { get; init; } = QuaternionF.Identity;

    /// <summary>The velocity its center of mass starts at, in meters per second.</summary>
    public Vector3F LinearVelocity { get; init; }

    /// <summary>The spin it starts at, in radians per second around each world axis.</summary>
    public Vector3F AngularVelocity { get; init; }

    /// <summary>The mass in kilograms; zero to weigh it by <see cref="Density"/>.</summary>
    public float Mass { get; init; }

    /// <summary>The density the mass follows from, in kilograms per cubic meter; water unless told.</summary>
    public float Density { get; init; } = 1000f;

    /// <summary>How the body rubs and bounces.</summary>
    public PhysicsMaterial Material { get; init; } = PhysicsMaterial.Default;

    /// <summary>How fast its velocity fades by itself, per second.</summary>
    public float LinearDamping { get; init; }

    /// <summary>How fast its spin fades by itself, per second.</summary>
    public float AngularDamping { get; init; }

    /// <summary>How much gravity pulls it: 1 as the simulation's, 0 not at all.</summary>
    public float GravityScale { get; init; } = 1f;

    /// <summary>Whether it may fall asleep at rest.</summary>
    public bool CanSleep { get; init; } = true;

    /// <summary>The caller's own, given back by <see cref="PhysicsSimulation.DataOf(BodyId)"/>: an entity, say.</summary>
    public object Data { get; init; }
}
