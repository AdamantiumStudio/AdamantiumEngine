namespace Adamantium.MathTests.Golden;

/// <summary>One recorded call: the numbers it was given, whether it threw, and the numbers it answered - the result,
/// then each argument passed by reference as the call left it, then the instance as the call left it.</summary>
public sealed record GoldenCase(double[] Inputs, bool Threw, double[] Outputs);
