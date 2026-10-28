using System;
using System.Globalization;
using System.Numerics;
using System.Runtime.InteropServices;

namespace Adamantium.Mathematics.Typed;

/// <summary>
/// A width and a height of any number type - <c>Size&lt;double&gt;</c> for layout, <c>Size&lt;int&gt;</c> for pixels.
/// Made with <see cref="Size.Create{T}(T, T)"/>; what needs fractions is in <see cref="Size"/>'s extensions. Equality is
/// exact; <see cref="NearEqual(Size{T}, Size{T})"/> compares within a tolerance.
/// </summary>
[StructLayout(LayoutKind.Sequential)]
public struct Size<T> : IEquatable<Size<T>>, IFormattable where T : unmanaged, INumber<T>
{
    /// <summary>A size of <paramref name="width"/> by <paramref name="height"/>.</summary>
    public Size(T width, T height)
    {
        Width = width;
        Height = height;
    }

    /// <summary>A square of <paramref name="value"/> each way.</summary>
    public Size(T value) : this(value, value)
    {
    }

    /// <summary>No width and no height.</summary>
    public static Size<T> Zero => default;

    /// <summary>The width.</summary>
    public T Width { get; set; }

    /// <summary>The height.</summary>
    public T Height { get; set; }

    /// <summary>Whether both the width and the height are zero.</summary>
    public readonly bool IsZero => Width == T.Zero && Height == T.Zero;

    /// <summary>The size with each side at most the same side of <paramref name="constraint"/>.</summary>
    public readonly Size<T> Constrain(Size<T> constraint) => new(T.Min(Width, constraint.Width), T.Min(Height, constraint.Height));

    /// <summary>The size in another number type, each side converted as a cast would.</summary>
    public readonly Size<TOther> As<TOther>() where TOther : unmanaged, INumber<TOther> =>
        new(TOther.CreateTruncating(Width), TOther.CreateTruncating(Height));

    /// <summary>Whether each side of the two is within <see cref="MathHelper.Scalar.NearEqual{T}"/>.</summary>
    public static bool NearEqual(Size<T> left, Size<T> right) =>
        MathHelper.Scalar.NearEqual(left.Width, right.Width) && MathHelper.Scalar.NearEqual(left.Height, right.Height);

    /// <summary>The side-by-side sum.</summary>
    public static Size<T> operator +(Size<T> left, Size<T> right) => new(left.Width + right.Width, left.Height + right.Height);

    /// <summary>The side-by-side difference.</summary>
    public static Size<T> operator -(Size<T> left, Size<T> right) => new(left.Width - right.Width, left.Height - right.Height);

    /// <summary>Both sides times <paramref name="scale"/>.</summary>
    public static Size<T> operator *(Size<T> size, T scale) => new(size.Width * scale, size.Height * scale);

    /// <summary>Both sides divided by <paramref name="scale"/>.</summary>
    public static Size<T> operator /(Size<T> size, T scale) => new(size.Width / scale, size.Height / scale);

    /// <summary>The width times <paramref name="scale"/>'s X, the height times its Y.</summary>
    public static Size<T> operator *(Size<T> size, Vector2<T> scale) => new(size.Width * scale.X, size.Height * scale.Y);

    /// <summary>The width divided by <paramref name="scale"/>'s X, the height by its Y.</summary>
    public static Size<T> operator /(Size<T> size, Vector2<T> scale) => new(size.Width / scale.X, size.Height / scale.Y);

    /// <summary>Whether the sides are equal; NaN equals nothing.</summary>
    public static bool operator ==(Size<T> left, Size<T> right) => left.Width == right.Width && left.Height == right.Height;

    /// <summary>Whether a side differs; NaN differs from everything.</summary>
    public static bool operator !=(Size<T> left, Size<T> right) => !(left == right);

    /// <summary>The width as X and the height as Y.</summary>
    public static explicit operator Vector2<T>(Size<T> size) => new(size.Width, size.Height);

    /// <summary>Whether the sides are equal, a NaN to a NaN too - as a dictionary key needs.</summary>
    public readonly bool Equals(Size<T> other) => Width.Equals(other.Width) && Height.Equals(other.Height);

    /// <inheritdoc />
    public override readonly bool Equals(object obj) => obj is Size<T> other && Equals(other);

    /// <inheritdoc />
    public override readonly int GetHashCode() => HashCode.Combine(Width, Height);

    /// <inheritdoc />
    public override readonly string ToString() => ToString(null, CultureInfo.CurrentCulture);

    /// <inheritdoc />
    public readonly string ToString(string format, IFormatProvider formatProvider) =>
        string.Format(formatProvider, "Width: {0} Height: {1}", Width.ToString(format, formatProvider),
            Height.ToString(format, formatProvider));
}
