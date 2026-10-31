using System;
using System.Globalization;
using System.Runtime.InteropServices;

namespace Adamantium.Mathematics;

/// <summary>
/// A color of four bytes in the order blue, green, red, alpha - a pixel or a vertex color of the format B8G8R8A8 UNORM.
/// The same color as a <see cref="Color"/>, its bytes in another order: the two convert into each other unchanged.
/// Equality is exact.
/// </summary>
[StructLayout(LayoutKind.Sequential, Size = 4)]
public struct ColorBGRA : IEquatable<ColorBGRA>, IFormattable
{
    /// <summary>The blue component.</summary>
    public byte B;

    /// <summary>The green component.</summary>
    public byte G;

    /// <summary>The red component.</summary>
    public byte R;

    /// <summary>The alpha component: 0 clear, 255 opaque.</summary>
    public byte A;

    /// <summary>A color of <paramref name="red"/>, <paramref name="green"/>, <paramref name="blue"/> and
    /// <paramref name="alpha"/>.</summary>
    public ColorBGRA(byte red, byte green, byte blue, byte alpha)
    {
        B = blue;
        G = green;
        R = red;
        A = alpha;
    }

    /// <summary>The color packed blue, green, red, alpha from the lowest byte up: 0xAARRGGBB.</summary>
    public static ColorBGRA FromBgra(uint color) => Color.FromBgra(color);

    /// <summary>The color packed red, green, blue, alpha from the lowest byte up.</summary>
    public static ColorBGRA FromRgba(uint color) => Color.FromRgba(color);

    /// <summary>The color packed blue, green, red, alpha from the lowest byte up: 0xAARRGGBB.</summary>
    public readonly uint ToBgra() => ((Color)this).ToBgra();

    /// <summary>The color premultiplied by its alpha: red, green and blue each times alpha over 255, rounded.</summary>
    public readonly ColorBGRA ToPremultiplied() => ((Color)this).ToPremultiplied();

    /// <summary>Writes <paramref name="straight"/> premultiplied by their alpha into <paramref name="premultiplied"/>,
    /// which is as long - a row of pixels at a time, as a compositor takes them.</summary>
    public static void Premultiply(ReadOnlySpan<ColorBGRA> straight, Span<ColorBGRA> premultiplied) =>
        AlphaPremultiplication.Convert(MemoryMarshal.Cast<ColorBGRA, uint>(straight),
            MemoryMarshal.Cast<ColorBGRA, uint>(premultiplied));

    /// <summary>The color packed red, green, blue, alpha from the lowest byte up.</summary>
    public readonly uint ToRgba() => ((Color)this).ToRgba();

    /// <summary>Red, green and blue as fractions from 0 to 1.</summary>
    public readonly Vector3<float> ToVector3() => ((Color)this).ToVector3();

    /// <summary>Red, green, blue and alpha as fractions from 0 to 1.</summary>
    public readonly Vector4<float> ToVector4() => ((Color)this).ToVector4();

    /// <summary>The same color, its bytes in the order red, green, blue, alpha.</summary>
    public static implicit operator Color(ColorBGRA color) => new(color.R, color.G, color.B, color.A);

    /// <summary>The same color, its bytes in the order blue, green, red, alpha.</summary>
    public static implicit operator ColorBGRA(Color color) => new(color.R, color.G, color.B, color.A);

    /// <summary>Whether the components are equal.</summary>
    public static bool operator ==(ColorBGRA left, ColorBGRA right) => left.ToBgra() == right.ToBgra();

    /// <summary>Whether a component differs.</summary>
    public static bool operator !=(ColorBGRA left, ColorBGRA right) => !(left == right);

    /// <summary>Whether the components are equal.</summary>
    public readonly bool Equals(ColorBGRA other) => this == other;

    /// <inheritdoc />
    public override readonly bool Equals(object obj) => obj is ColorBGRA other && Equals(other);

    /// <inheritdoc />
    public override readonly int GetHashCode() => (int)ToBgra();

    /// <inheritdoc />
    public override readonly string ToString() => ToString(null, CultureInfo.CurrentCulture);

    /// <inheritdoc />
    public readonly string ToString(string format, IFormatProvider formatProvider) => ((Color)this).ToString(format, formatProvider);
}
