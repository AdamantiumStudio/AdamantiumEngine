using System;
using System.Globalization;
using System.Runtime.InteropServices;
using Adamantium.Core.TypeParsing;
using Adamantium.Mathematics.TypeParsing;

namespace Adamantium.Mathematics;

/// <summary>
/// A color of four bytes in the order red, green, blue, alpha - a pixel or a vertex color of the format R8G8B8A8 UNORM.
/// A fraction from 0 to 1 becomes a byte as UNORM does: clamped, times 255, rounded to the nearest, a half up; NaN
/// becomes 0. A packed color is read and written in the order of its name from the lowest byte up, as Vulkan names its
/// formats: <see cref="ToBgra"/> holds blue in its lowest byte and alpha in its highest - 0xAARRGGBB as a number, what
/// System.Drawing calls ARGB; <see cref="ToArgb"/> holds alpha in its lowest byte. In markup a color is a name or a
/// number, as <see cref="ColorParser"/> reads it. Equality is exact.
/// </summary>
[StructLayout(LayoutKind.Sequential, Size = 4)]
[TypeParser(typeof(ColorParser))]
public struct Color : IEquatable<Color>, IFormattable
{
    /// <summary>The red component.</summary>
    public byte R;

    /// <summary>The green component.</summary>
    public byte G;

    /// <summary>The blue component.</summary>
    public byte B;

    /// <summary>The alpha component: 0 clear, 255 opaque.</summary>
    public byte A;

    /// <summary>A color of <paramref name="red"/>, <paramref name="green"/>, <paramref name="blue"/> and
    /// <paramref name="alpha"/>.</summary>
    public Color(byte red, byte green, byte blue, byte alpha)
    {
        R = red;
        G = green;
        B = blue;
        A = alpha;
    }

    /// <summary>An opaque color of <paramref name="red"/>, <paramref name="green"/> and <paramref name="blue"/>.</summary>
    public Color(byte red, byte green, byte blue) : this(red, green, blue, byte.MaxValue)
    {
    }

    /// <summary>A color of the four numbers, each clamped to 0 to 255.</summary>
    public Color(int red, int green, int blue, int alpha) : this(byte.CreateSaturating(red), byte.CreateSaturating(green),
        byte.CreateSaturating(blue), byte.CreateSaturating(alpha))
    {
    }

    /// <summary>An opaque color of the three numbers, each clamped to 0 to 255.</summary>
    public Color(int red, int green, int blue) : this(red, green, blue, byte.MaxValue)
    {
    }

    /// <summary>A color of the fractions <paramref name="value"/> holds as red, green, blue and alpha.</summary>
    public Color(Vector4<float> value) : this(ToByte(value.X), ToByte(value.Y), ToByte(value.Z), ToByte(value.W))
    {
    }

    /// <summary>A color of the fractions <paramref name="value"/> holds as red, green and blue, and of
    /// <paramref name="alpha"/>.</summary>
    public Color(Vector3<float> value, float alpha) : this(ToByte(value.X), ToByte(value.Y), ToByte(value.Z), ToByte(alpha))
    {
    }

    /// <summary>An opaque color of the fractions <paramref name="value"/> holds as red, green and blue.</summary>
    public Color(Vector3<float> value) : this(value, 1f)
    {
    }

    /// <summary>The color of <paramref name="red"/>, <paramref name="green"/>, <paramref name="blue"/> and
    /// <paramref name="alpha"/>.</summary>
    public static Color FromRgba(byte red, byte green, byte blue, byte alpha) => new(red, green, blue, alpha);

    /// <summary>The opaque color of <paramref name="red"/>, <paramref name="green"/> and <paramref name="blue"/>.</summary>
    public static Color FromRgba(byte red, byte green, byte blue) => new(red, green, blue);

    /// <summary>The color packed red, green, blue, alpha from the lowest byte up.</summary>
    public static Color FromRgba(uint color) => new(Byte(color, 0), Byte(color, 1), Byte(color, 2), Byte(color, 3));

    /// <summary>The color packed blue, green, red, alpha from the lowest byte up: 0xAARRGGBB.</summary>
    public static Color FromBgra(uint color) => new(Byte(color, 2), Byte(color, 1), Byte(color, 0), Byte(color, 3));

    /// <summary>The color packed alpha, red, green, blue from the lowest byte up.</summary>
    public static Color FromArgb(uint color) => new(Byte(color, 1), Byte(color, 2), Byte(color, 3), Byte(color, 0));

    /// <summary>The color packed alpha, blue, green, red from the lowest byte up.</summary>
    public static Color FromAbgr(uint color) => new(Byte(color, 3), Byte(color, 2), Byte(color, 1), Byte(color, 0));

    /// <summary>The color packed red, green, blue, alpha from the lowest byte up.</summary>
    public readonly uint ToRgba() => Pack(R, G, B, A);

    /// <summary>The color packed blue, green, red, alpha from the lowest byte up: 0xAARRGGBB.</summary>
    public readonly uint ToBgra() => Pack(B, G, R, A);

    /// <summary>The color packed alpha, red, green, blue from the lowest byte up.</summary>
    public readonly uint ToArgb() => Pack(A, R, G, B);

    /// <summary>The color packed alpha, blue, green, red from the lowest byte up.</summary>
    public readonly uint ToAbgr() => Pack(A, B, G, R);

    /// <summary>Red, green and blue as fractions from 0 to 1.</summary>
    public readonly Vector3<float> ToVector3() => new(R / 255f, G / 255f, B / 255f);

    /// <summary>Red, green, blue and alpha as fractions from 0 to 1.</summary>
    public readonly Vector4<float> ToVector4() => new(R / 255f, G / 255f, B / 255f, A / 255f);

    /// <summary>The color as four fractions.</summary>
    public readonly Color4F ToColor4() => new(this);

    /// <summary>The color <paramref name="amount"/> of the way from <paramref name="start"/> to <paramref name="end"/>,
    /// each component rounded to the nearest and clamped to 0 to 255; a component the two share stays whatever the
    /// amount, and one they do not becomes 0 for an amount of NaN.</summary>
    public static Color Lerp(Color start, Color end, float amount) =>
        new(Blend(start.R, end.R, amount), Blend(start.G, end.G, amount), Blend(start.B, end.B, amount), Blend(start.A, end.A, amount));

    /// <summary>Whether the components are equal.</summary>
    public static bool operator ==(Color left, Color right) => left.ToRgba() == right.ToRgba();

    /// <summary>Whether a component differs.</summary>
    public static bool operator !=(Color left, Color right) => !(left == right);

    /// <summary>Whether the components are equal.</summary>
    public readonly bool Equals(Color other) => this == other;

    /// <inheritdoc />
    public override readonly bool Equals(object obj) => obj is Color other && Equals(other);

    /// <inheritdoc />
    public override readonly int GetHashCode() => (int)ToRgba();

    /// <inheritdoc />
    public override readonly string ToString() => ToString(null, CultureInfo.CurrentCulture);

    /// <inheritdoc />
    public readonly string ToString(string format, IFormatProvider formatProvider) =>
        string.Format(formatProvider, "R:{0} G:{1} B:{2} A:{3}", R.ToString(format, formatProvider), G.ToString(format, formatProvider),
            B.ToString(format, formatProvider), A.ToString(format, formatProvider));

    private static byte ToByte(float fraction) =>
        float.IsNaN(fraction) ? (byte)0 : (byte)Math.Round(Math.Clamp(fraction, 0f, 1f) * 255.0, MidpointRounding.AwayFromZero);

    private static byte Blend(byte start, byte end, float amount) =>
        start == end ? start
        : float.IsNaN(amount) ? (byte)0
        : byte.CreateSaturating(Math.Round(start + (end - start) * (double)amount, MidpointRounding.AwayFromZero));

    private static byte Byte(uint color, int index) => (byte)(color >> (index * 8));

    private static uint Pack(byte lowest, byte second, byte third, byte highest) =>
        lowest | ((uint)second << 8) | ((uint)third << 16) | ((uint)highest << 24);
}
