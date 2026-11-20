namespace Adamantium.DarkMatter;

/// <summary>How a body rubs and bounces: a pair rubs by the root of both frictions' product and bounces as the
/// bouncier.</summary>
public readonly record struct PhysicsMaterial
{
    public PhysicsMaterial()
    {
    }

    /// <summary>Friction 0.6, no bounce.</summary>
    public static PhysicsMaterial Default => new();

    /// <summary>How hard the body rubs against what it slides on; not below zero.</summary>
    public float Friction { get; init; } = 0.6f;

    /// <summary>How much of its speed the body keeps bouncing off: 0 not at all, 1 all of it.</summary>
    public float Restitution { get; init; }
}
