using System;

namespace Adamantium.DarkMatter.CollisionDetection;

/// <summary>A vector's parts and the unit axes by number - 0 for X, 1 for Y, 2 for Z - and the choice between axes
/// that part two shapes.</summary>
internal static class AxisMath
{
    private const float RelativeTolerance = 0.05f;
    private const float AbsoluteTolerance = 0.0025f;

    /// <summary>Whether the separation along a candidate axis beats the one along the axis kept so far by more than a
    /// small tolerance, overlapping or apart, so a near tie keeps the kept axis and contacts do not flicker.</summary>
    public static bool IsClearlyGreater(float candidate, float kept) =>
        candidate > kept + RelativeTolerance * MathF.Abs(kept) + AbsoluteTolerance;

    public static Vector3F GetUnitAxis(int axis) => axis switch
    {
        0 => Vector3F.UnitX,
        1 => Vector3F.UnitY,
        _ => Vector3F.UnitZ
    };

    public static float GetComponent(Vector3F vector, int axis) => axis switch
    {
        0 => vector.X,
        1 => vector.Y,
        _ => vector.Z
    };

    /// <summary>A direction of length one at right angles to <paramref name="direction"/>, which must not be zero.</summary>
    public static Vector3F GetPerpendicular(Vector3F direction)
    {
        var other = MathF.Abs(direction.X) < 0.57735f ? Vector3F.UnitX : Vector3F.UnitY;
        return Vector3F.Normalize(Vector3F.Cross(direction, other));
    }

    /// <summary>The axis along which <paramref name="direction"/> reaches farthest, either way.</summary>
    public static int FindDominantAxis(Vector3F direction)
    {
        var absolute = Vector3F.Abs(direction);
        if (absolute.X >= absolute.Y && absolute.X >= absolute.Z)
            return 0;

        return absolute.Y >= absolute.Z ? 1 : 2;
    }
}
