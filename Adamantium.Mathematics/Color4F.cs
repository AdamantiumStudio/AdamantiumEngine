using System;
using System.Globalization;
using System.Runtime.InteropServices;

namespace Adamantium.Mathematics;

/// <summary>
/// A color of four fractions - red, green, blue, alpha - a vertex color of the format R32G32B32A32 SFLOAT; 0 is none, 1
/// full, and nothing keeps them in between. Made a <see cref="Color"/> as UNORM makes a byte: clamped, rounded to the
/// nearest. Equality is exact; <see cref="NearEqual"/> compares within a tolerance.
/// </summary>
[StructLayout(LayoutKind.Sequential)]
public struct Color4F : IEquatable<Color4F>, IFormattable
{
    /// <summary>The red component.</summary>
    public float Red;

    /// <summary>The green component.</summary>
    public float Green;

    /// <summary>The blue component.</summary>
    public float Blue;

    /// <summary>The alpha component: 0 clear, 1 opaque.</summary>
    public float Alpha;

    /// <summary>A color of <paramref name="red"/>, <paramref name="green"/>, <paramref name="blue"/> and
    /// <paramref name="alpha"/>.</summary>
    public Color4F(float red, float green, float blue, float alpha)
    {
        Red = red;
        Green = green;
        Blue = blue;
        Alpha = alpha;
    }

    /// <summary>A color of the components <paramref name="value"/> holds as red, green, blue and alpha.</summary>
    public Color4F(Vector4<float> value) : this(value.X, value.Y, value.Z, value.W)
    {
    }

    /// <summary>A color of the components <paramref name="value"/> holds as red, green and blue, and of
    /// <paramref name="alpha"/>.</summary>
    public Color4F(Vector3<float> value, float alpha) : this(value.X, value.Y, value.Z, alpha)
    {
    }

    /// <summary>The fractions of <paramref name="color"/>'s bytes.</summary>
    public Color4F(Color color) : this(color.ToVector4())
    {
    }

    /// <summary>Red, green, blue and alpha.</summary>
    public readonly Vector4<float> ToVector4() => new(Red, Green, Blue, Alpha);

    /// <summary>Red, green and blue.</summary>
    public readonly Vector3<float> ToVector3() => new(Red, Green, Blue);

    /// <summary>The color in bytes, packed red, green, blue, alpha from the lowest byte up.</summary>
    public readonly uint ToRgba() => ((Color)this).ToRgba();

    /// <summary>The color in bytes, packed blue, green, red, alpha from the lowest byte up: 0xAARRGGBB.</summary>
    public readonly uint ToBgra() => ((Color)this).ToBgra();

    /// <summary>The color <paramref name="amount"/> of the way from <paramref name="start"/> to
    /// <paramref name="end"/>.</summary>
    public static Color4F Lerp(Color4F start, Color4F end, float amount) =>
        new(Vector4<float>.Lerp(start.ToVector4(), end.ToVector4(), amount));

    /// <summary>Whether the components are within <see cref="MathHelper.Scalar.NearEqual{T}"/>.</summary>
    public static bool NearEqual(Color4F left, Color4F right) => Vector4<float>.NearEqual(left.ToVector4(), right.ToVector4());

    /// <summary>The fractions of <paramref name="color"/>'s bytes.</summary>
    public static implicit operator Color4F(Color color) => new(color);

    /// <summary>The color in bytes, each fraction clamped and rounded to the nearest.</summary>
    public static explicit operator Color(Color4F color) => new(color.ToVector4());

    /// <summary>Whether the components are equal; NaN equals nothing.</summary>
    public static bool operator ==(Color4F left, Color4F right) => left.ToVector4() == right.ToVector4();

    /// <summary>Whether a component differs; NaN differs from everything.</summary>
    public static bool operator !=(Color4F left, Color4F right) => !(left == right);

    /// <summary>Whether the components are equal, a NaN to a NaN too.</summary>
    public readonly bool Equals(Color4F other) => ToVector4().Equals(other.ToVector4());

    /// <inheritdoc />
    public override readonly bool Equals(object obj) => obj is Color4F other && Equals(other);

    /// <inheritdoc />
    public override readonly int GetHashCode() => ToVector4().GetHashCode();

    /// <inheritdoc />
    public override readonly string ToString() => ToString(null, CultureInfo.CurrentCulture);

    /// <inheritdoc />
    public readonly string ToString(string format, IFormatProvider formatProvider) =>
        string.Format(formatProvider, "Red:{0} Green:{1} Blue:{2} Alpha:{3}", Red.ToString(format, formatProvider),
            Green.ToString(format, formatProvider), Blue.ToString(format, formatProvider), Alpha.ToString(format, formatProvider));
}
