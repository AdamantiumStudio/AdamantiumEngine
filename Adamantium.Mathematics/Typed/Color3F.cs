using System;
using System.Globalization;
using System.Runtime.InteropServices;

namespace Adamantium.Mathematics.Typed;

/// <summary>
/// A color of three fractions - red, green, blue - a vertex color of the format R32G32B32 SFLOAT; 0 is none, 1 full,
/// and nothing keeps them in between. Made a <see cref="Color"/> as UNORM makes a byte: clamped, rounded to the
/// nearest, opaque. Equality is exact; <see cref="NearEqual"/> compares within a tolerance.
/// </summary>
[StructLayout(LayoutKind.Sequential)]
public struct Color3F : IEquatable<Color3F>, IFormattable
{
    /// <summary>The red component.</summary>
    public float Red;

    /// <summary>The green component.</summary>
    public float Green;

    /// <summary>The blue component.</summary>
    public float Blue;

    /// <summary>A color of <paramref name="red"/>, <paramref name="green"/> and <paramref name="blue"/>.</summary>
    public Color3F(float red, float green, float blue)
    {
        Red = red;
        Green = green;
        Blue = blue;
    }

    /// <summary>A color of the components <paramref name="value"/> holds as red, green and blue.</summary>
    public Color3F(Vector3<float> value) : this(value.X, value.Y, value.Z)
    {
    }

    /// <summary>Red, green and blue.</summary>
    public readonly Vector3<float> ToVector3() => new(Red, Green, Blue);

    /// <summary>The opaque color in bytes, packed red, green, blue, alpha from the lowest byte up.</summary>
    public readonly uint ToRgba() => new Color(ToVector3()).ToRgba();

    /// <summary>The opaque color in bytes, packed blue, green, red, alpha from the lowest byte up: 0xAARRGGBB.</summary>
    public readonly uint ToBgra() => new Color(ToVector3()).ToBgra();

    /// <summary>The color <paramref name="amount"/> of the way from <paramref name="start"/> to
    /// <paramref name="end"/>.</summary>
    public static Color3F Lerp(Color3F start, Color3F end, float amount) =>
        new(Vector3<float>.Lerp(start.ToVector3(), end.ToVector3(), amount));

    /// <summary>Whether the components are within <see cref="MathHelper.Scalar.NearEqual{T}"/>.</summary>
    public static bool NearEqual(Color3F left, Color3F right) => Vector3<float>.NearEqual(left.ToVector3(), right.ToVector3());

    /// <summary>Whether the components are equal; NaN equals nothing.</summary>
    public static bool operator ==(Color3F left, Color3F right) => left.ToVector3() == right.ToVector3();

    /// <summary>Whether a component differs; NaN differs from everything.</summary>
    public static bool operator !=(Color3F left, Color3F right) => !(left == right);

    /// <summary>Whether the components are equal, a NaN to a NaN too.</summary>
    public readonly bool Equals(Color3F other) => ToVector3().Equals(other.ToVector3());

    /// <inheritdoc />
    public override readonly bool Equals(object obj) => obj is Color3F other && Equals(other);

    /// <inheritdoc />
    public override readonly int GetHashCode() => ToVector3().GetHashCode();

    /// <inheritdoc />
    public override readonly string ToString() => ToString(null, CultureInfo.CurrentCulture);

    /// <inheritdoc />
    public readonly string ToString(string format, IFormatProvider formatProvider) =>
        string.Format(formatProvider, "Red:{0} Green:{1} Blue:{2}", Red.ToString(format, formatProvider),
            Green.ToString(format, formatProvider), Blue.ToString(format, formatProvider));
}
