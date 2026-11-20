namespace Adamantium.DarkMatter;

/// <summary>What drives a hinge or a slider: the speed, in radians or meters per second, and the most torque or force it
/// gives, in newton meters or newtons.</summary>
public readonly record struct JointMotor(float Speed, float MaxForce);
