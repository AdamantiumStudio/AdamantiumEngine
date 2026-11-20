namespace Adamantium.DarkMatter;

/// <summary>A distance joint's spring, pulling toward the length it had when added: how stiff, as a frequency in hertz,
/// and how its swing dies, 1 not at all past its rest.</summary>
public readonly record struct JointSpring(float Hertz, float DampingRatio);
