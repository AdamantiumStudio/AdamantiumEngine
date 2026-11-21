using Adamantium.DarkMatter;
using Adamantium.Mathematics;

namespace Adamantium.Engine.Physics;

/// <summary>The body a rigid body and its colliders ask for, as the service read them: compared with what it read
/// before to see what changed.</summary>
internal sealed class BodyLayout
{
    public BodyKind Kind { get; set; }

    /// <summary>The colliders' shapes, a part each, around the body's origin.</summary>
    public BodyPart[] Parts { get; set; }

    /// <summary>Where the body's origin stands.</summary>
    public WorldPosition Position { get; set; }

    public QuaternionF Rotation { get; set; }

    /// <summary>The mass in kilograms; zero to weigh it by <see cref="Density"/>.</summary>
    public float Mass { get; set; }

    public float Density { get; set; }

    public float Friction { get; set; }

    public float Restitution { get; set; }

    public float LinearDamping { get; set; }

    public float AngularDamping { get; set; }

    public float GravityScale { get; set; }

    public bool CanSleep { get; set; }

    /// <summary>Whether two weigh a body alike: by their masses when either has one, by their densities when not.
    /// </summary>
    public bool IsWeighedAs(BodyLayout other) =>
        Mass > 0 || other.Mass > 0 ? Mass == other.Mass : Density == other.Density;
}
