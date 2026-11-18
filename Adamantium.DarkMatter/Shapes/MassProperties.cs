namespace Adamantium.DarkMatter.Shapes;

/// <summary>The mass of a shape, its center and its moments of inertia around their axes through that center.</summary>
public readonly struct MassProperties
{
    /// <summary>A mass centered on the shape, its moments around the shape's own axes.</summary>
    public MassProperties(float mass, Vector3F inertia) : this(mass, inertia, Vector3F.Zero, QuaternionF.Identity)
    {
    }

    public MassProperties(float mass, Vector3F inertia, Vector3F center, QuaternionF inertiaRotation)
    {
        Mass = mass;
        Inertia = inertia;
        Center = center;
        InertiaRotation = inertiaRotation;
    }

    /// <summary>The mass, in kilograms.</summary>
    public float Mass { get; }

    /// <summary>The moments of inertia around the axes <see cref="InertiaRotation"/> turns, in kilogram square meters.
    /// </summary>
    public Vector3F Inertia { get; }

    /// <summary>The center of mass, in the shape's own axes.</summary>
    public Vector3F Center { get; }

    /// <summary>How the axes of <see cref="Inertia"/> are turned against the shape's own.</summary>
    public QuaternionF InertiaRotation { get; }
}
