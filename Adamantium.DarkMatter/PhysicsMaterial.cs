using System;

namespace Adamantium.DarkMatter;

/// <summary>How a body or a part of it rubs and bounces, and how its friction and bounce meet another's.</summary>
public readonly record struct PhysicsMaterial
{
    public PhysicsMaterial()
    {
    }

    /// <summary>Friction 0.5, no bounce, each averaged with the other's.</summary>
    public static PhysicsMaterial Default => new();

    /// <summary>How hard the body rubs against what it slides on; not below zero.</summary>
    public float Friction { get; init; } = 0.5f;

    /// <summary>How much of its speed the body keeps bouncing off: 0 not at all, 1 all of it.</summary>
    public float Restitution { get; init; }

    /// <summary>How its friction meets the other's.</summary>
    public CombineRule FrictionCombine { get; init; }

    /// <summary>How its bounce meets the other's.</summary>
    public CombineRule RestitutionCombine { get; init; }

    // Throws for a friction or bounce not finite or below zero, or a rule unknown.
    internal static void RequireUsable(in PhysicsMaterial material, string parameterName)
    {
        if (!float.IsFinite(material.Friction) || material.Friction < 0 || !float.IsFinite(material.Restitution)
            || material.Restitution < 0)
            throw new ArgumentOutOfRangeException(parameterName, "Friction and restitution must be finite, not below zero.");

        if (!Enum.IsDefined(material.FrictionCombine) || !Enum.IsDefined(material.RestitutionCombine))
            throw new ArgumentOutOfRangeException(parameterName, "Unknown rule to combine a material by.");
    }

    // The friction two materials touching rub by.
    internal static float CombineFriction(in PhysicsMaterial first, in PhysicsMaterial second) =>
        Combine(first.Friction, second.Friction, (CombineRule)Math.Max((int)first.FrictionCombine,
            (int)second.FrictionCombine));

    // The bounce two materials touching bounce by.
    internal static float CombineRestitution(in PhysicsMaterial first, in PhysicsMaterial second) =>
        Combine(first.Restitution, second.Restitution, (CombineRule)Math.Max((int)first.RestitutionCombine,
            (int)second.RestitutionCombine));

    private static float Combine(float first, float second, CombineRule rule) => rule switch
    {
        CombineRule.Minimum => MathF.Min(first, second),
        CombineRule.Multiply => first * second,
        CombineRule.Maximum => MathF.Max(first, second),
        _ => (first + second) * 0.5f
    };
}
