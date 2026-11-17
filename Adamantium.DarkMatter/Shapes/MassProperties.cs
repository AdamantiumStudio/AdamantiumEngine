namespace Adamantium.DarkMatter.Shapes;

/// <summary>The mass of a shape and its moments of inertia around its own axes, through its center.</summary>
public readonly struct MassProperties
{
    public MassProperties(float mass, Vector3F inertia)
    {
        Mass = mass;
        Inertia = inertia;
    }

    /// <summary>The mass, in kilograms.</summary>
    public float Mass { get; }

    /// <summary>The moments of inertia around the shape's X, Y and Z axes, in kilogram square meters.</summary>
    public Vector3F Inertia { get; }

    /// <summary>The same shape at <paramref name="mass"/>: the moments scale with it.</summary>
    public MassProperties ScaleTo(float mass) => Mass > 0 ? new MassProperties(mass, Inertia * (mass / Mass)) : this;
}
