namespace Adamantium.DarkMatter;

/// <summary>How far a joint may go: a hinge's angle in radians, a slider's offset or a distance joint's length in
/// meters - from where it stood when added, a distance joint's from zero.</summary>
public readonly record struct JointLimits(float Lower, float Upper);
