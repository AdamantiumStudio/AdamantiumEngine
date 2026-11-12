using System;
using System.Globalization;
using System.Numerics;
using Adamantium.Mathematics;

namespace Adamantium.Engine.Scenes;

/// <summary>How a scene file writes its values: numbers in the invariant culture, a vector's or a quaternion's numbers
/// separated by ", ", an id as 32 hexadecimal digits.</summary>
internal static class SceneText
{
    public static string Format<T>(Vector2<T> value) where T : unmanaged, INumber<T> =>
        Join(value.X, value.Y);

    public static string Format<T>(Vector3<T> value) where T : unmanaged, INumber<T> =>
        Join(value.X, value.Y, value.Z);

    public static string Format<T>(Quaternion<T> value) where T : unmanaged, IFloatingPointIeee754<T> =>
        Join(value.X, value.Y, value.Z, value.W);

    public static string Format(UInt128 id) => id.ToString("x32", CultureInfo.InvariantCulture);

    /// <exception cref="FormatException"><paramref name="text"/> is not an id.</exception>
    public static UInt128 ParseId(string text) =>
        UInt128.Parse(text, NumberStyles.AllowHexSpecifier, CultureInfo.InvariantCulture);

    private static string Join<T>(params ReadOnlySpan<T> numbers) where T : INumber<T>
    {
        var parts = new string[numbers.Length];
        for (var i = 0; i < numbers.Length; i++)
        {
            parts[i] = numbers[i].ToString(null, CultureInfo.InvariantCulture);
        }

        return string.Join(", ", parts);
    }
}
