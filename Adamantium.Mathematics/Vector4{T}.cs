using System;
using System.Globalization;
using System.Numerics;
using System.Runtime.CompilerServices;
using System.Runtime.InteropServices;

namespace Adamantium.Mathematics;

/// <summary>
/// A vector of four numbers of any type - <c>Vector4&lt;float&gt;</c>, <c>Vector4&lt;double&gt;</c>,
/// <c>Vector4&lt;int&gt;</c> - laid out X, Y, Z, W. What every number type can do is here; what needs fractions
/// (length, normalizing, blending) is in <see cref="Vector4"/>'s extensions, and it is made with
/// <see cref="Vector4.Create{T}(T, T, T, T)"/>. Equality is exact; <see cref="NearEqual(Vector4{T}, Vector4{T})"/>
/// compares within a tolerance.
/// </summary>
[StructLayout(LayoutKind.Sequential)]
public struct Vector4<T> : IEquatable<Vector4<T>>, IFormattable where T : unmanaged, INumber<T>
{
    /// <summary>The X component.</summary>
    public T X;

    /// <summary>The Y component.</summary>
    public T Y;

    /// <summary>The Z component.</summary>
    public T Z;

    /// <summary>The W component.</summary>
    public T W;

    /// <summary>A vector of <paramref name="x"/>, <paramref name="y"/>, <paramref name="z"/> and <paramref name="w"/>.</summary>
    public Vector4(T x, T y, T z, T w)
    {
        X = x;
        Y = y;
        Z = z;
        W = w;
    }

    /// <summary>A vector of <paramref name="value"/> in every component.</summary>
    public Vector4(T value) : this(value, value, value, value)
    {
    }

    /// <summary>A vector of <paramref name="value"/>'s X, Y and Z and <paramref name="w"/>.</summary>
    public Vector4(Vector3<T> value, T w) : this(value.X, value.Y, value.Z, w)
    {
    }

    /// <summary>A vector of <paramref name="value"/>'s X and Y and <paramref name="z"/> and <paramref name="w"/>.</summary>
    public Vector4(Vector2<T> value, T z, T w) : this(value.X, value.Y, z, w)
    {
    }

    /// <summary>A vector of <paramref name="value"/>'s X and Y, Z and W zero.</summary>
    public Vector4(Vector2<T> value) : this(value.X, value.Y, T.Zero, T.Zero)
    {
    }

    /// <summary>A vector of the four numbers in <paramref name="values"/>.</summary>
    /// <exception cref="ArgumentOutOfRangeException"><paramref name="values"/> does not hold four numbers.</exception>
    public Vector4(ReadOnlySpan<T> values)
    {
        if (values.Length != 4)
        {
            throw new ArgumentOutOfRangeException(nameof(values), "There must be four input values for Vector4.");
        }

        X = values[0];
        Y = values[1];
        Z = values[2];
        W = values[3];
    }

    /// <summary>The size of the vector in bytes.</summary>
    public static int SizeInBytes => Unsafe.SizeOf<Vector4<T>>();

    /// <summary>All zero.</summary>
    public static Vector4<T> Zero => default;

    /// <summary>All one.</summary>
    public static Vector4<T> One => new(T.One);

    /// <summary>The unit X axis.</summary>
    public static Vector4<T> UnitX => new(T.One, T.Zero, T.Zero, T.Zero);

    /// <summary>The unit Y axis.</summary>
    public static Vector4<T> UnitY => new(T.Zero, T.One, T.Zero, T.Zero);

    /// <summary>The unit Z axis.</summary>
    public static Vector4<T> UnitZ => new(T.Zero, T.Zero, T.One, T.Zero);

    /// <summary>The unit W axis.</summary>
    public static Vector4<T> UnitW => new(T.Zero, T.Zero, T.Zero, T.One);

    /// <summary>Whether every component is zero.</summary>
    public readonly bool IsZero => X == T.Zero && Y == T.Zero && Z == T.Zero && W == T.Zero;

    /// <summary>The component at <paramref name="index"/>: 0 for X, 1 for Y, 2 for Z, 3 for W.</summary>
    /// <exception cref="ArgumentOutOfRangeException"><paramref name="index"/> is not 0 to 3.</exception>
    public T this[int index]
    {
        readonly get => index switch
        {
            0 => X,
            1 => Y,
            2 => Z,
            3 => W,
            _ => throw new ArgumentOutOfRangeException(nameof(index), "Indices for Vector4 run from 0 to 3, inclusive.")
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
                case 2:
                    Z = value;
                    break;
                case 3:
                    W = value;
                    break;
                default:
                    throw new ArgumentOutOfRangeException(nameof(index), "Indices for Vector4 run from 0 to 3, inclusive.");
            }
        }
    }

    /// <summary>The square of the length: the dot product with itself.</summary>
    public readonly T LengthSquared() => X * X + Y * Y + Z * Z + W * W;

    /// <summary>The components as an array: X, Y, Z, W.</summary>
    public readonly T[] ToArray() => [X, Y, Z, W];

    /// <summary>The vector in another number type, each component converted as a cast would.</summary>
    public readonly Vector4<TOther> As<TOther>() where TOther : unmanaged, INumber<TOther> =>
        new(TOther.CreateTruncating(X), TOther.CreateTruncating(Y), TOther.CreateTruncating(Z), TOther.CreateTruncating(W));

    /// <summary>The sum of two vectors.</summary>
    public static Vector4<T> Add(Vector4<T> left, Vector4<T> right) => left + right;

    /// <summary><paramref name="right"/> added to every component.</summary>
    public static Vector4<T> Add(Vector4<T> left, T right) => left + right;

    /// <summary>The difference of two vectors.</summary>
    public static Vector4<T> Subtract(Vector4<T> left, Vector4<T> right) => left - right;

    /// <summary><paramref name="right"/> taken from every component.</summary>
    public static Vector4<T> Subtract(Vector4<T> left, T right) => left - right;

    /// <summary>Every component taken from <paramref name="left"/>.</summary>
    public static Vector4<T> Subtract(T left, Vector4<T> right) => left - right;

    /// <summary>Every component times <paramref name="scale"/>.</summary>
    public static Vector4<T> Multiply(Vector4<T> value, T scale) => value * scale;

    /// <summary>The component-wise product.</summary>
    public static Vector4<T> Multiply(Vector4<T> left, Vector4<T> right) => left * right;

    /// <summary>Every component divided by <paramref name="scale"/>.</summary>
    public static Vector4<T> Divide(Vector4<T> value, T scale) => value / scale;

    /// <summary><paramref name="scale"/> divided by every component.</summary>
    public static Vector4<T> Divide(T scale, Vector4<T> value) => scale / value;

    /// <summary>Every component negated.</summary>
    public static Vector4<T> Negate(Vector4<T> value) => -value;

    /// <summary>The dot product.</summary>
    public static T Dot(Vector4<T> left, Vector4<T> right) =>
        left.X * right.X + left.Y * right.Y + left.Z * right.Z + left.W * right.W;

    /// <summary>The square of the distance between two points.</summary>
    public static T DistanceSquared(Vector4<T> value1, Vector4<T> value2) => (value1 - value2).LengthSquared();

    /// <summary>The point at <paramref name="amount1"/> along the second corner and <paramref name="amount2"/> along
    /// the third of the triangle <paramref name="value1"/>, <paramref name="value2"/>, <paramref name="value3"/>.</summary>
    public static Vector4<T> Barycentric(Vector4<T> value1, Vector4<T> value2, Vector4<T> value3, T amount1, T amount2) =>
        new(value1.X + amount1 * (value2.X - value1.X) + amount2 * (value3.X - value1.X),
            value1.Y + amount1 * (value2.Y - value1.Y) + amount2 * (value3.Y - value1.Y),
            value1.Z + amount1 * (value2.Z - value1.Z) + amount2 * (value3.Z - value1.Z),
            value1.W + amount1 * (value2.W - value1.W) + amount2 * (value3.W - value1.W));

    /// <summary>Every component held between those of <paramref name="min"/> and <paramref name="max"/>.</summary>
    public static Vector4<T> Clamp(Vector4<T> value, Vector4<T> min, Vector4<T> max) =>
        new(ClampOne(value.X, min.X, max.X), ClampOne(value.Y, min.Y, max.Y), ClampOne(value.Z, min.Z, max.Z),
            ClampOne(value.W, min.W, max.W));

    /// <summary>The larger of each pair of components.</summary>
    public static Vector4<T> Max(Vector4<T> left, Vector4<T> right) =>
        new(left.X > right.X ? left.X : right.X, left.Y > right.Y ? left.Y : right.Y, left.Z > right.Z ? left.Z : right.Z,
            left.W > right.W ? left.W : right.W);

    /// <summary>The smaller of each pair of components.</summary>
    public static Vector4<T> Min(Vector4<T> left, Vector4<T> right) =>
        new(left.X < right.X ? left.X : right.X, left.Y < right.Y ? left.Y : right.Y, left.Z < right.Z ? left.Z : right.Z,
            left.W < right.W ? left.W : right.W);

    /// <summary>The absolute value of every component.</summary>
    public static Vector4<T> Abs(Vector4<T> value) => new(T.Abs(value.X), T.Abs(value.Y), T.Abs(value.Z), T.Abs(value.W));

    /// <summary>Whether each component of the two is within the same component of <paramref name="epsilon"/>, as
    /// <see cref="MathHelper.Scalar.WithinEpsilon{T}"/> measures it.</summary>
    public static bool NearEqual(Vector4<T> left, Vector4<T> right, Vector4<T> epsilon) =>
        MathHelper.Scalar.WithinEpsilon(left.X, right.X, epsilon.X) && MathHelper.Scalar.WithinEpsilon(left.Y, right.Y, epsilon.Y)
                                                         && MathHelper.Scalar.WithinEpsilon(left.Z, right.Z, epsilon.Z)
                                                         && MathHelper.Scalar.WithinEpsilon(left.W, right.W, epsilon.W);

    /// <summary>Whether each component of the two is within <see cref="MathHelper.Scalar.NearEqual{T}"/>.</summary>
    public static bool NearEqual(Vector4<T> left, Vector4<T> right) =>
        MathHelper.Scalar.NearEqual(left.X, right.X) && MathHelper.Scalar.NearEqual(left.Y, right.Y) && MathHelper.Scalar.NearEqual(left.Z, right.Z)
        && MathHelper.Scalar.NearEqual(left.W, right.W);

    /// <summary>The sum of two vectors.</summary>
    public static Vector4<T> operator +(Vector4<T> left, Vector4<T> right) =>
        new(left.X + right.X, left.Y + right.Y, left.Z + right.Z, left.W + right.W);

    /// <summary><paramref name="scalar"/> added to every component.</summary>
    public static Vector4<T> operator +(Vector4<T> value, T scalar) =>
        new(value.X + scalar, value.Y + scalar, value.Z + scalar, value.W + scalar);

    /// <summary><paramref name="scalar"/> added to every component.</summary>
    public static Vector4<T> operator +(T scalar, Vector4<T> value) => value + scalar;

    /// <summary>The vector itself.</summary>
    public static Vector4<T> operator +(Vector4<T> value) => value;

    /// <summary>The difference of two vectors.</summary>
    public static Vector4<T> operator -(Vector4<T> left, Vector4<T> right) =>
        new(left.X - right.X, left.Y - right.Y, left.Z - right.Z, left.W - right.W);

    /// <summary><paramref name="scalar"/> taken from every component.</summary>
    public static Vector4<T> operator -(Vector4<T> value, T scalar) =>
        new(value.X - scalar, value.Y - scalar, value.Z - scalar, value.W - scalar);

    /// <summary>Every component taken from <paramref name="scalar"/>.</summary>
    public static Vector4<T> operator -(T scalar, Vector4<T> value) =>
        new(scalar - value.X, scalar - value.Y, scalar - value.Z, scalar - value.W);

    /// <summary>Every component negated.</summary>
    public static Vector4<T> operator -(Vector4<T> value) => new(-value.X, -value.Y, -value.Z, -value.W);

    /// <summary>The component-wise product.</summary>
    public static Vector4<T> operator *(Vector4<T> left, Vector4<T> right) =>
        new(left.X * right.X, left.Y * right.Y, left.Z * right.Z, left.W * right.W);

    /// <summary>Every component times <paramref name="scale"/>.</summary>
    public static Vector4<T> operator *(Vector4<T> value, T scale) =>
        new(value.X * scale, value.Y * scale, value.Z * scale, value.W * scale);

    /// <summary>Every component times <paramref name="scale"/>.</summary>
    public static Vector4<T> operator *(T scale, Vector4<T> value) => value * scale;

    /// <summary>Every component divided by <paramref name="scale"/>.</summary>
    public static Vector4<T> operator /(Vector4<T> value, T scale) =>
        new(value.X / scale, value.Y / scale, value.Z / scale, value.W / scale);

    /// <summary><paramref name="scale"/> divided by every component.</summary>
    public static Vector4<T> operator /(T scale, Vector4<T> value) =>
        new(scale / value.X, scale / value.Y, scale / value.Z, scale / value.W);

    /// <summary>The component-wise quotient.</summary>
    public static Vector4<T> operator /(Vector4<T> value, Vector4<T> scale) =>
        new(value.X / scale.X, value.Y / scale.Y, value.Z / scale.Z, value.W / scale.W);

    /// <summary>Whether the components are equal; NaN equals nothing.</summary>
    public static bool operator ==(Vector4<T> left, Vector4<T> right) =>
        left.X == right.X && left.Y == right.Y && left.Z == right.Z && left.W == right.W;

    /// <summary>Whether any component differs; NaN differs from everything.</summary>
    public static bool operator !=(Vector4<T> left, Vector4<T> right) => !(left == right);

    /// <summary>X and Y.</summary>
    public static explicit operator Vector2<T>(Vector4<T> value) => new(value.X, value.Y);

    /// <summary>X, Y and Z.</summary>
    public static explicit operator Vector3<T>(Vector4<T> value) => new(value.X, value.Y, value.Z);

    /// <summary>Whether the components are equal, a NaN to a NaN too - as a dictionary key needs.</summary>
    public readonly bool Equals(Vector4<T> other) =>
        X.Equals(other.X) && Y.Equals(other.Y) && Z.Equals(other.Z) && W.Equals(other.W);

    /// <inheritdoc />
    public override readonly bool Equals(object obj) => obj is Vector4<T> other && Equals(other);

    /// <inheritdoc />
    public override readonly int GetHashCode() => HashCode.Combine(X, Y, Z, W);

    /// <inheritdoc />
    public override readonly string ToString() => ToString(null, CultureInfo.CurrentCulture);

    /// <summary>The vector with each component written in <paramref name="format"/>.</summary>
    public readonly string ToString(string format) => ToString(format, CultureInfo.CurrentCulture);

    /// <summary>The vector written for <paramref name="formatProvider"/>.</summary>
    public readonly string ToString(IFormatProvider formatProvider) => ToString(null, formatProvider);

    /// <inheritdoc />
    public readonly string ToString(string format, IFormatProvider formatProvider) =>
        string.Format(formatProvider, "X:{0} Y:{1} Z:{2} W:{3}", X.ToString(format, formatProvider),
            Y.ToString(format, formatProvider), Z.ToString(format, formatProvider), W.ToString(format, formatProvider));

    private static T ClampOne(T value, T min, T max)
    {
        value = value > max ? max : value;
        return value < min ? min : value;
    }
}
