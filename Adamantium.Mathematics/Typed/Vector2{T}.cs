using System;
using System.Globalization;
using System.Numerics;
using System.Runtime.CompilerServices;
using System.Runtime.InteropServices;

namespace Adamantium.Mathematics.Typed;

/// <summary>
/// A vector of two numbers of any type - <c>Vector2&lt;float&gt;</c>, <c>Vector2&lt;double&gt;</c>,
/// <c>Vector2&lt;int&gt;</c> - laid out X, Y. What every number type can do is here; what needs fractions (length,
/// normalizing, blending) is in <see cref="Vector2"/>'s extensions, and it is made with <see cref="Vector2.Create{T}(T, T)"/>.
/// Equality is exact; <see cref="NearEqual(Vector2{T}, Vector2{T})"/> compares within a tolerance.
/// </summary>
[StructLayout(LayoutKind.Sequential)]
public struct Vector2<T> : IEquatable<Vector2<T>>, IFormattable where T : unmanaged, INumber<T>
{
    /// <summary>The X component.</summary>
    public T X;

    /// <summary>The Y component.</summary>
    public T Y;

    /// <summary>A vector of <paramref name="x"/> and <paramref name="y"/>.</summary>
    public Vector2(T x, T y)
    {
        X = x;
        Y = y;
    }

    /// <summary>A vector of <paramref name="value"/> in every component.</summary>
    public Vector2(T value) : this(value, value)
    {
    }

    /// <summary>A vector of the two numbers in <paramref name="values"/>.</summary>
    /// <exception cref="ArgumentOutOfRangeException"><paramref name="values"/> does not hold two numbers.</exception>
    public Vector2(ReadOnlySpan<T> values)
    {
        if (values.Length != 2)
        {
            throw new ArgumentOutOfRangeException(nameof(values), "There must be two input values for Vector2.");
        }

        X = values[0];
        Y = values[1];
    }

    /// <summary>The size of the vector in bytes.</summary>
    public static int SizeInBytes => Unsafe.SizeOf<Vector2<T>>();

    /// <summary>All zero.</summary>
    public static Vector2<T> Zero => default;

    /// <summary>All one.</summary>
    public static Vector2<T> One => new(T.One);

    /// <summary>The unit X axis.</summary>
    public static Vector2<T> UnitX => new(T.One, T.Zero);

    /// <summary>The unit Y axis.</summary>
    public static Vector2<T> UnitY => new(T.Zero, T.One);

    /// <summary>Whether every component is zero.</summary>
    public readonly bool IsZero => X == T.Zero && Y == T.Zero;

    /// <summary>The component at <paramref name="index"/>: 0 for X, 1 for Y.</summary>
    /// <exception cref="ArgumentOutOfRangeException"><paramref name="index"/> is not 0 or 1.</exception>
    public T this[int index]
    {
        readonly get => index switch
        {
            0 => X,
            1 => Y,
            _ => throw new ArgumentOutOfRangeException(nameof(index), "Indices for Vector2 run from 0 to 1, inclusive.")
        };
        set
        {
            switch (index)
            {
                case 0:
                    X = value;
                    break;
                case 1:
                    Y = value;
                    break;
                default:
                    throw new ArgumentOutOfRangeException(nameof(index), "Indices for Vector2 run from 0 to 1, inclusive.");
            }
        }
    }

    /// <summary>The square of the length: the dot product with itself.</summary>
    public readonly T LengthSquared() => X * X + Y * Y;

    /// <summary>Holds every component between zero and one.</summary>
    public void Saturate()
    {
        X = SaturateOne(X);
        Y = SaturateOne(Y);
    }

    /// <summary>The components as an array: X, Y.</summary>
    public readonly T[] ToArray() => [X, Y];

    /// <summary>The vector in another number type, each component converted as a cast would.</summary>
    public readonly Vector2<TOther> As<TOther>() where TOther : unmanaged, INumber<TOther> =>
        new(TOther.CreateTruncating(X), TOther.CreateTruncating(Y));

    /// <summary>The sum of two vectors.</summary>
    public static Vector2<T> Add(Vector2<T> left, Vector2<T> right) => left + right;

    /// <summary><paramref name="right"/> added to every component.</summary>
    public static Vector2<T> Add(Vector2<T> left, T right) => left + right;

    /// <summary>The difference of two vectors.</summary>
    public static Vector2<T> Subtract(Vector2<T> left, Vector2<T> right) => left - right;

    /// <summary><paramref name="right"/> taken from every component.</summary>
    public static Vector2<T> Subtract(Vector2<T> left, T right) => left - right;

    /// <summary>Every component taken from <paramref name="left"/>.</summary>
    public static Vector2<T> Subtract(T left, Vector2<T> right) => left - right;

    /// <summary>Every component times <paramref name="scale"/>.</summary>
    public static Vector2<T> Multiply(Vector2<T> value, T scale) => value * scale;

    /// <summary>The component-wise product.</summary>
    public static Vector2<T> Multiply(Vector2<T> left, Vector2<T> right) => left * right;

    /// <summary>Every component divided by <paramref name="scale"/>.</summary>
    public static Vector2<T> Divide(Vector2<T> value, T scale) => value / scale;

    /// <summary><paramref name="scale"/> divided by every component.</summary>
    public static Vector2<T> Divide(T scale, Vector2<T> value) => scale / value;

    /// <summary>Every component negated.</summary>
    public static Vector2<T> Negate(Vector2<T> value) => -value;

    /// <summary>The dot product.</summary>
    public static T Dot(Vector2<T> left, Vector2<T> right) => left.X * right.X + left.Y * right.Y;

    /// <summary>The determinant of the two as matrix rows: the Z of their cross product in 3D, positive when
    /// <paramref name="right"/> turns counter-clockwise from <paramref name="left"/>.</summary>
    public static T Determinant(Vector2<T> left, Vector2<T> right) => left.X * right.Y - left.Y * right.X;

    /// <summary><paramref name="value"/> turned a quarter clockwise: (Y, -X).</summary>
    public static Vector2<T> Perpendicular(Vector2<T> value) => new(value.Y, -value.X);

    /// <summary>The square of the distance between two points.</summary>
    public static T DistanceSquared(Vector2<T> value1, Vector2<T> value2) => (value1 - value2).LengthSquared();

    /// <summary>The point at <paramref name="amount1"/> along the second corner and <paramref name="amount2"/> along
    /// the third of the triangle <paramref name="value1"/>, <paramref name="value2"/>, <paramref name="value3"/>.</summary>
    public static Vector2<T> Barycentric(Vector2<T> value1, Vector2<T> value2, Vector2<T> value3, T amount1, T amount2) =>
        new(value1.X + amount1 * (value2.X - value1.X) + amount2 * (value3.X - value1.X),
            value1.Y + amount1 * (value2.Y - value1.Y) + amount2 * (value3.Y - value1.Y));

    /// <summary>Every component held between those of <paramref name="min"/> and <paramref name="max"/>.</summary>
    public static Vector2<T> Clamp(Vector2<T> value, Vector2<T> min, Vector2<T> max) =>
        new(ClampOne(value.X, min.X, max.X), ClampOne(value.Y, min.Y, max.Y));

    /// <summary>The larger of each pair of components.</summary>
    public static Vector2<T> Max(Vector2<T> left, Vector2<T> right) =>
        new(left.X > right.X ? left.X : right.X, left.Y > right.Y ? left.Y : right.Y);

    /// <summary>The smaller of each pair of components.</summary>
    public static Vector2<T> Min(Vector2<T> left, Vector2<T> right) =>
        new(left.X < right.X ? left.X : right.X, left.Y < right.Y ? left.Y : right.Y);

    /// <summary>The largest component; X when it is NaN, as a component only replaces it by comparing larger.</summary>
    public static T Max(Vector2<T> value) => value.Y > value.X ? value.Y : value.X;

    /// <summary>The smallest component; X when it is NaN, as a component only replaces it by comparing smaller.</summary>
    public static T Min(Vector2<T> value) => value.Y < value.X ? value.Y : value.X;

    /// <summary>The absolute value of every component.</summary>
    public static Vector2<T> Abs(Vector2<T> value) => new(T.Abs(value.X), T.Abs(value.Y));

    /// <summary>Whether each component of the two is within the same component of <paramref name="epsilon"/>, as
    /// <see cref="Scalar.WithinEpsilon{T}"/> measures it.</summary>
    public static bool NearEqual(Vector2<T> left, Vector2<T> right, Vector2<T> epsilon) =>
        Scalar.WithinEpsilon(left.X, right.X, epsilon.X) && Scalar.WithinEpsilon(left.Y, right.Y, epsilon.Y);

    /// <summary>Whether each component of the two is within <see cref="Scalar.NearEqual{T}"/>.</summary>
    public static bool NearEqual(Vector2<T> left, Vector2<T> right) =>
        Scalar.NearEqual(left.X, right.X) && Scalar.NearEqual(left.Y, right.Y);

    /// <summary>The sum of two vectors.</summary>
    public static Vector2<T> operator +(Vector2<T> left, Vector2<T> right) => new(left.X + right.X, left.Y + right.Y);

    /// <summary><paramref name="scalar"/> added to every component.</summary>
    public static Vector2<T> operator +(Vector2<T> value, T scalar) => new(value.X + scalar, value.Y + scalar);

    /// <summary><paramref name="scalar"/> added to every component.</summary>
    public static Vector2<T> operator +(T scalar, Vector2<T> value) => value + scalar;

    /// <summary>The vector itself.</summary>
    public static Vector2<T> operator +(Vector2<T> value) => value;

    /// <summary>The difference of two vectors.</summary>
    public static Vector2<T> operator -(Vector2<T> left, Vector2<T> right) => new(left.X - right.X, left.Y - right.Y);

    /// <summary><paramref name="scalar"/> taken from every component.</summary>
    public static Vector2<T> operator -(Vector2<T> value, T scalar) => new(value.X - scalar, value.Y - scalar);

    /// <summary>Every component taken from <paramref name="scalar"/>.</summary>
    public static Vector2<T> operator -(T scalar, Vector2<T> value) => new(scalar - value.X, scalar - value.Y);

    /// <summary>Every component negated.</summary>
    public static Vector2<T> operator -(Vector2<T> value) => new(-value.X, -value.Y);

    /// <summary>The component-wise product.</summary>
    public static Vector2<T> operator *(Vector2<T> left, Vector2<T> right) => new(left.X * right.X, left.Y * right.Y);

    /// <summary>Every component times <paramref name="scale"/>.</summary>
    public static Vector2<T> operator *(Vector2<T> value, T scale) => new(value.X * scale, value.Y * scale);

    /// <summary>Every component times <paramref name="scale"/>.</summary>
    public static Vector2<T> operator *(T scale, Vector2<T> value) => value * scale;

    /// <summary>Every component divided by <paramref name="scale"/>.</summary>
    public static Vector2<T> operator /(Vector2<T> value, T scale) => new(value.X / scale, value.Y / scale);

    /// <summary><paramref name="scale"/> divided by every component.</summary>
    public static Vector2<T> operator /(T scale, Vector2<T> value) => new(scale / value.X, scale / value.Y);

    /// <summary>The component-wise quotient.</summary>
    public static Vector2<T> operator /(Vector2<T> value, Vector2<T> scale) => new(value.X / scale.X, value.Y / scale.Y);

    /// <summary>Whether the components are equal; NaN equals nothing.</summary>
    public static bool operator ==(Vector2<T> left, Vector2<T> right) => left.X == right.X && left.Y == right.Y;

    /// <summary>Whether any component differs; NaN differs from everything.</summary>
    public static bool operator !=(Vector2<T> left, Vector2<T> right) => !(left == right);

    /// <summary>X, Y and a Z of zero.</summary>
    public static explicit operator Vector3<T>(Vector2<T> value) => new(value);

    /// <summary>X, Y and a Z and W of zero.</summary>
    public static explicit operator Vector4<T>(Vector2<T> value) => new(value);

    /// <summary>Whether the components are equal, a NaN to a NaN too - as a dictionary key needs.</summary>
    public readonly bool Equals(Vector2<T> other) => X.Equals(other.X) && Y.Equals(other.Y);

    /// <inheritdoc />
    public override readonly bool Equals(object obj) => obj is Vector2<T> other && Equals(other);

    /// <inheritdoc />
    public override readonly int GetHashCode() => HashCode.Combine(X, Y);

    /// <inheritdoc />
    public override readonly string ToString() => ToString(null, CultureInfo.CurrentCulture);

    /// <summary>The vector with each component written in <paramref name="format"/>.</summary>
    public readonly string ToString(string format) => ToString(format, CultureInfo.CurrentCulture);

    /// <summary>The vector written for <paramref name="formatProvider"/>.</summary>
    public readonly string ToString(IFormatProvider formatProvider) => ToString(null, formatProvider);

    /// <inheritdoc />
    public readonly string ToString(string format, IFormatProvider formatProvider) =>
        string.Format(formatProvider, "X:{0} Y:{1}", X.ToString(format, formatProvider), Y.ToString(format, formatProvider));

    private static T ClampOne(T value, T min, T max)
    {
        value = value > max ? max : value;
        return value < min ? min : value;
    }

    private static T SaturateOne(T value)
    {
        if (value < T.Zero)
        {
            return T.Zero;
        }

        return value > T.One ? T.One : value;
    }
}
