using System;

namespace Adamantium.DarkMatter;

/// <summary>How a body or a part of it rubs, rolls and bounces, how each meets another's, and what surface it is.
/// </summary>
public readonly record struct PhysicsMaterial
{
    public PhysicsMaterial()
    {
    }

    /// <summary>Friction 0.5 holding and sliding, no rolling resistance, no bounce, each averaged with the other's; no
    /// surface named.</summary>
    public static PhysicsMaterial Default => new();

    /// <summary>How hard the body holds on to what it stands on before it starts to slide; not below zero.</summary>
    public float StaticFriction { get; init; } = 0.5f;

    /// <summary>How hard the body rubs against what it slides on; not below zero, and above the static friction it
    /// rubs by the static.</summary>
    public float DynamicFriction { get; init; } = 0.5f;

    /// <summary>How hard the body is held back rolling over what it touches, as a share of how hard it is pressed - a
    /// tyre on asphalt about 0.01, on sand 0.3; not below zero. Met by <see cref="FrictionCombine"/>.</summary>
    public float RollingResistance { get; init; }

    /// <summary>How much of its speed the body keeps bouncing off: 0 not at all, 1 all of it.</summary>
    public float Restitution { get; init; }

    /// <summary>How its friction and rolling resistance meet the other's.</summary>
    public CombineRule FrictionCombine { get; init; }

    /// <summary>How its bounce meets the other's.</summary>
    public CombineRule RestitutionCombine { get; init; }

    /// <summary>What surface the material is - "Ice", "Asphalt", "Mud" - told with the contacts and hits it is part of,
    /// for a game's sounds, marks and grip; its spaces at the ends left out, null for none - a blank one too.</summary>
    public string Surface
    {
        get => _surface;
        init => _surface = string.IsNullOrWhiteSpace(value) ? null : value.Trim();
    }

    private readonly string _surface;

    // Throws for a friction, rolling resistance or bounce not finite or below zero, or a rule unknown.
    internal static void RequireUsable(in PhysicsMaterial material, string parameterName)
    {
        if (!IsAmount(material.StaticFriction) || !IsAmount(material.DynamicFriction)
            || !IsAmount(material.RollingResistance) || !IsAmount(material.Restitution))
            throw new ArgumentOutOfRangeException(parameterName,
                "Friction, rolling resistance and restitution must be finite, not below zero.");

        if (!Enum.IsDefined(material.FrictionCombine) || !Enum.IsDefined(material.RestitutionCombine))
            throw new ArgumentOutOfRangeException(parameterName, "Unknown rule to combine a material by.");
    }

    // The friction two materials touching hold by.
    internal static float CombineStaticFriction(in PhysicsMaterial first, in PhysicsMaterial second) =>
        Combine(first.StaticFriction, second.StaticFriction, FrictionRule(first, second));

    // The friction two materials touching slide by.
    internal static float CombineDynamicFriction(in PhysicsMaterial first, in PhysicsMaterial second) =>
        Combine(first.DynamicFriction, second.DynamicFriction, FrictionRule(first, second));

    // The rolling resistance two materials touching roll by.
    internal static float CombineRollingResistance(in PhysicsMaterial first, in PhysicsMaterial second) =>
        Combine(first.RollingResistance, second.RollingResistance, FrictionRule(first, second));

    // The bounce two materials touching bounce by.
    internal static float CombineRestitution(in PhysicsMaterial first, in PhysicsMaterial second) =>
        Combine(first.Restitution, second.Restitution, (CombineRule)Math.Max((int)first.RestitutionCombine,
            (int)second.RestitutionCombine));

    private static CombineRule FrictionRule(in PhysicsMaterial first, in PhysicsMaterial second) =>
        (CombineRule)Math.Max((int)first.FrictionCombine, (int)second.FrictionCombine);

    private static float Combine(float first, float second, CombineRule rule) => rule switch
    {
        CombineRule.Minimum => MathF.Min(first, second),
        CombineRule.Multiply => first * second,
        CombineRule.Maximum => MathF.Max(first, second),
        _ => (first + second) * 0.5f
    };

    private static bool IsAmount(float value) => float.IsFinite(value) && value >= 0;
}
