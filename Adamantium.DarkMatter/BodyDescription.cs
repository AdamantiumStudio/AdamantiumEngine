using Adamantium.DarkMatter.Shapes;

namespace Adamantium.DarkMatter;

/// <summary>What a body is when it is added to a world: its shape, how it moves, where it stands and what it is made
/// of. Read once by <see cref="IPhysicsWorld.Add"/>, so one description can add many bodies.</summary>
internal sealed class BodyDescription
{
    /// <summary>How the body moves.</summary>
    public BodyKind Kind { get; set; } = BodyKind.Dynamic;

    /// <summary>The shape the body collides with, given around the body's origin; a
    /// <see cref="CompoundCollisionShape"/> for a body of many.</summary>
    public CollisionShape Shape { get; set; }

    /// <summary>Where the body's origin stands.</summary>
    public WorldPosition Position { get; set; }

    /// <summary>How the body is turned.</summary>
    public QuaternionF Rotation { get; set; } = QuaternionF.Identity;

    /// <summary>The velocity the body's center of mass starts at, in meters per second; a dynamic body added asleep
    /// starts still.</summary>
    public Vector3F LinearVelocity { get; set; }

    /// <summary>The spin the body starts at, in radians per second around each world axis, cut to the world's
    /// <see cref="IPhysicsWorld.MaxAngularSpeed"/>; a dynamic body added asleep starts still.</summary>
    public Vector3F AngularVelocity { get; set; }

    /// <summary>The density the mass follows from, in kilograms per cubic meter; water by default.</summary>
    public float Density { get; set; } = 1000f;

    /// <summary>The mass in kilograms, in place of the one <see cref="Density"/> gives; zero to use the density.</summary>
    public float Mass { get; set; }

    /// <summary>The friction against other bodies; a pair uses the square root of the two bodies' product.</summary>
    public float Friction { get; set; } = 0.6f;

    /// <summary>How much of its speed the body keeps bouncing off: 0 not at all, 1 all of it; a pair uses the greater.
    /// </summary>
    public float Restitution { get; set; }

    /// <summary>How fast the body's velocity fades by itself, per second.</summary>
    public float LinearDamping { get; set; }

    /// <summary>How fast the body's spin fades by itself, per second.</summary>
    public float AngularDamping { get; set; }

    /// <summary>How much gravity pulls the body: 1 as the world's, 0 not at all.</summary>
    public float GravityScale { get; set; } = 1f;

    /// <summary>Whether the body may fall asleep when it comes to rest.</summary>
    public bool CanSleep { get; set; } = true;

    /// <summary>Whether a dynamic body starts awake. A kinematic body is awake while it moves, a static one never.
    /// </summary>
    public bool IsAwake { get; set; } = true;

    /// <summary>Whether two masses and densities weigh a body alike: the same mass, or with no mass the same density -
    /// a density does not count beside a mass.</summary>
    public static bool AreWeighedAlike(float firstMass, float firstDensity, float secondMass, float secondDensity) =>
        firstMass > 0 || secondMass > 0 ? firstMass == secondMass : firstDensity == secondDensity;
}
