using System;
using System.Globalization;
using System.Numerics;
using System.Runtime.CompilerServices;
using System.Runtime.InteropServices;

namespace Adamantium.Mathematics.Typed;

/// <summary>
/// A vector of three numbers of any type - <c>Vector3&lt;float&gt;</c>, <c>Vector3&lt;double&gt;</c>,
/// <c>Vector3&lt;int&gt;</c> - laid out X, Y, Z. What every number type can do is here; what needs fractions (length,
/// normalizing, blending) is in <see cref="Vector3"/>'s extensions, and it is made with <see cref="Vector3.Create{T}(T, T, T)"/>.
/// Equality is exact; <see cref="NearEqual(Vector3{T}, Vector3{T})"/> compares within a tolerance.
/// </summary>
[StructLayout(LayoutKind.Sequential)]
public struct Vector3<T> : IEquatable<Vector3<T>>, IFormattable where T : unmanaged, INumber<T>
{
    /// <summary>The X component.</summary>
    public T X;

    /// <summary>The Y component.</summary>
    public T Y;

    /// <summary>The Z component.</summary>
    public T Z;

    /// <summary>A vector of <paramref name="x"/>, <paramref name="y"/> and <paramref name="z"/>.</summary>
    public Vector3(T x, T y, T z)
    {
        X = x;
        Y = y;
        Z = z;
    }

    /// <summary>A vector of <paramref name="value"/> in every component.</summary>
    public Vector3(T value) : this(value, value, value)
    {
    }

    /// <summary>A vector of <paramref name="x"/> and <paramref name="y"/>, Z zero.</summary>
    public Vector3(T x, T y) : this(x, y, T.Zero)
    {
    }

    /// <summary>A vector of <paramref name="value"/>'s X and Y and <paramref name="z"/>.</summary>
    public Vector3(Vector2<T> value, T z) : this(value.X, value.Y, z)
    {
    }

    /// <summary>A vector of <paramref name="value"/>'s X and Y, Z zero.</summary>
    public Vector3(Vector2<T> value) : this(value.X, value.Y, T.Zero)
    {
    }

    /// <summary>A vector of the three numbers in <paramref name="values"/>.</summary>
    /// <exception cref="ArgumentOutOfRangeException"><paramref name="values"/> does not hold three numbers.</exception>
    public Vector3(ReadOnlySpan<T> values)
    {
        if (values.Length != 3)
        {
            throw new ArgumentOutOfRangeException(nameof(values), "There must be three input values for Vector3.");
        }

        X = values[0];
        Y = values[1];
        Z = values[2];
    }

    /// <summary>The size of the vector in bytes.</summary>
    public static int SizeInBytes => Unsafe.SizeOf<Vector3<T>>();

    /// <summary>All zero.</summary>
    public static Vector3<T> Zero => default;

    /// <summary>All one.</summary>
    public static Vector3<T> One => new(T.One);

    /// <summary>The unit X axis.</summary>
    public static Vector3<T> UnitX => new(T.One, T.Zero, T.Zero);

    /// <summary>The unit Y axis.</summary>
    public static Vector3<T> UnitY => new(T.Zero, T.One, T.Zero);

    /// <summary>The unit Z axis.</summary>
    public static Vector3<T> UnitZ => new(T.Zero, T.Zero, T.One);

    /// <summary>Up: positive Y.</summary>
    public static Vector3<T> Up => UnitY;

    /// <summary>Right: positive X.</summary>
    public static Vector3<T> Right => UnitX;

    /// <summary>Forward in a left-handed system: positive Z.</summary>
    public static Vector3<T> ForwardLH => UnitZ;

    /// <summary>Backward in a right-handed system: positive Z.</summary>
    public static Vector3<T> BackwardRH => UnitZ;

    /// <summary>Whether every component is zero.</summary>
    public readonly bool IsZero => X == T.Zero && Y == T.Zero && Z == T.Zero;

    /// <summary>The component at <paramref name="index"/>: 0 for X, 1 for Y, 2 for Z.</summary>
    /// <exception cref="ArgumentOutOfRangeException"><paramref name="index"/> is not 0, 1 or 2.</exception>
    public T this[int index]
    {
        readonly get => index switch
        {
            0 => X,
            1 => Y,
            2 => Z,
            _ => throw new ArgumentOutOfRangeException(nameof(index), "Indices for Vector3 run from 0 to 2, inclusive.")
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
                default:
                    throw new ArgumentOutOfRangeException(nameof(index), "Indices for Vector3 run from 0 to 2, inclusive.");
            }
        }
    }

    /// <summary>The square of the length: the dot product with itself.</summary>
    public readonly T LengthSquared() => X * X + Y * Y + Z * Z;

    /// <summary>The components as an array: X, Y, Z.</summary>
    public readonly T[] ToArray() => [X, Y, Z];

    /// <summary>The vector in another number type, each component converted as a cast would.</summary>
    public readonly Vector3<TOther> As<TOther>() where TOther : unmanaged, INumber<TOther> =>
        new(TOther.CreateTruncating(X), TOther.CreateTruncating(Y), TOther.CreateTruncating(Z));

    /// <summary>The sum of two vectors.</summary>
    public static Vector3<T> Add(Vector3<T> left, Vector3<T> right) => left + right;

    /// <summary><paramref name="right"/> added to every component.</summary>
    public static Vector3<T> Add(Vector3<T> left, T right) => left + right;

    /// <summary>The difference of two vectors.</summary>
    public static Vector3<T> Subtract(Vector3<T> left, Vector3<T> right) => left - right;

    /// <summary><paramref name="right"/> taken from every component.</summary>
    public static Vector3<T> Subtract(Vector3<T> left, T right) => left - right;

    /// <summary>Every component taken from <paramref name="left"/>.</summary>
    public static Vector3<T> Subtract(T left, Vector3<T> right) => left - right;

    /// <summary>Every component times <paramref name="scale"/>.</summary>
    public static Vector3<T> Multiply(Vector3<T> value, T scale) => value * scale;

    /// <summary>The component-wise product.</summary>
    public static Vector3<T> Multiply(Vector3<T> left, Vector3<T> right) => left * right;

    /// <summary>Every component divided by <paramref name="scale"/>.</summary>
    public static Vector3<T> Divide(Vector3<T> value, T scale) => value / scale;

    /// <summary><paramref name="scale"/> divided by every component.</summary>
    public static Vector3<T> Divide(T scale, Vector3<T> value) => scale / value;

    /// <summary>Every component negated.</summary>
    public static Vector3<T> Negate(Vector3<T> value) => -value;

    /// <summary>The dot product.</summary>
    public static T Dot(Vector3<T> left, Vector3<T> right) => left.X * right.X + left.Y * right.Y + left.Z * right.Z;

    /// <summary>The cross product: perpendicular to both, by the right-hand rule.</summary>
    public static Vector3<T> Cross(Vector3<T> left, Vector3<T> right) =>
        new(left.Y * right.Z - left.Z * right.Y, left.Z * right.X - left.X * right.Z, left.X * right.Y - left.Y * right.X);

    /// <summary>The square of the distance between two points.</summary>
    public static T DistanceSquared(Vector3<T> value1, Vector3<T> value2) => (value1 - value2).LengthSquared();

    /// <summary>The point at <paramref name="amount1"/> along the second corner and <paramref name="amount2"/> along
    /// the third of the triangle <paramref name="value1"/>, <paramref name="value2"/>, <paramref name="value3"/>.</summary>
    public static Vector3<T> Barycentric(Vector3<T> value1, Vector3<T> value2, Vector3<T> value3, T amount1, T amount2) =>
        new(value1.X + amount1 * (value2.X - value1.X) + amount2 * (value3.X - value1.X),
            value1.Y + amount1 * (value2.Y - value1.Y) + amount2 * (value3.Y - value1.Y),
            value1.Z + amount1 * (value2.Z - value1.Z) + amount2 * (value3.Z - value1.Z));

    /// <summary>Every component held between those of <paramref name="min"/> and <paramref name="max"/>.</summary>
    public static Vector3<T> Clamp(Vector3<T> value, Vector3<T> min, Vector3<T> max) =>
        new(ClampOne(value.X, min.X, max.X), ClampOne(value.Y, min.Y, max.Y), ClampOne(value.Z, min.Z, max.Z));

    /// <summary>The larger of each pair of components.</summary>
    public static Vector3<T> Max(Vector3<T> left, Vector3<T> right) =>
        new(left.X > right.X ? left.X : right.X, left.Y > right.Y ? left.Y : right.Y, left.Z > right.Z ? left.Z : right.Z);

    /// <summary>The smaller of each pair of components.</summary>
    public static Vector3<T> Min(Vector3<T> left, Vector3<T> right) =>
        new(left.X < right.X ? left.X : right.X, left.Y < right.Y ? left.Y : right.Y, left.Z < right.Z ? left.Z : right.Z);

    /// <summary>The largest component, walking X, Y, Z: a component replaces the one so far only by comparing larger,
    /// so a NaN is skipped unless it is X.</summary>
    public static T Max(Vector3<T> value)
    {
        var max = value.Y > value.X ? value.Y : value.X;
        return value.Z > max ? value.Z : max;
    }

    /// <summary>The smallest component, walking X, Y, Z: a component replaces the one so far only by comparing smaller,
    /// so a NaN is skipped unless it is X.</summary>
    public static T Min(Vector3<T> value)
    {
        var min = value.Y < value.X ? value.Y : value.X;
        return value.Z < min ? value.Z : min;
    }

    /// <summary>The absolute value of every component.</summary>
    public static Vector3<T> Abs(Vector3<T> value) => new(T.Abs(value.X), T.Abs(value.Y), T.Abs(value.Z));

    /// <summary>Whether each component of the two is within the same component of <paramref name="epsilon"/>, as
    /// <see cref="MathHelper.Scalar.WithinEpsilon{T}"/> measures it.</summary>
    public static bool NearEqual(Vector3<T> left, Vector3<T> right, Vector3<T> epsilon) =>
        MathHelper.Scalar.WithinEpsilon(left.X, right.X, epsilon.X) && MathHelper.Scalar.WithinEpsilon(left.Y, right.Y, epsilon.Y)
                                                         && MathHelper.Scalar.WithinEpsilon(left.Z, right.Z, epsilon.Z);

    /// <summary>Whether each component of the two is within <see cref="MathHelper.Scalar.NearEqual{T}"/>.</summary>
    public static bool NearEqual(Vector3<T> left, Vector3<T> right) =>
        MathHelper.Scalar.NearEqual(left.X, right.X) && MathHelper.Scalar.NearEqual(left.Y, right.Y) && MathHelper.Scalar.NearEqual(left.Z, right.Z);

    /// <summary>The sum of two vectors.</summary>
    public static Vector3<T> operator +(Vector3<T> left, Vector3<T> right) =>
        new(left.X + right.X, left.Y + right.Y, left.Z + right.Z);

    /// <summary><paramref name="scalar"/> added to every component.</summary>
    public static Vector3<T> operator +(Vector3<T> value, T scalar) => new(value.X + scalar, value.Y + scalar, value.Z + scalar);

    /// <summary><paramref name="scalar"/> added to every component.</summary>
    public static Vector3<T> operator +(T scalar, Vector3<T> value) => value + scalar;

    /// <summary>The vector itself.</summary>
    public static Vector3<T> operator +(Vector3<T> value) => value;

    /// <summary>The difference of two vectors.</summary>
    public static Vector3<T> operator -(Vector3<T> left, Vector3<T> right) =>
        new(left.X - right.X, left.Y - right.Y, left.Z - right.Z);

    /// <summary><paramref name="scalar"/> taken from every component.</summary>
    public static Vector3<T> operator -(Vector3<T> value, T scalar) => new(value.X - scalar, value.Y - scalar, value.Z - scalar);

    /// <summary>Every component taken from <paramref name="scalar"/>.</summary>
    public static Vector3<T> operator -(T scalar, Vector3<T> value) => new(scalar - value.X, scalar - value.Y, scalar - value.Z);

    /// <summary>Every component negated.</summary>
    public static Vector3<T> operator -(Vector3<T> value) => new(-value.X, -value.Y, -value.Z);

    /// <summary>The component-wise product.</summary>
    public static Vector3<T> operator *(Vector3<T> left, Vector3<T> right) =>
        new(left.X * right.X, left.Y * right.Y, left.Z * right.Z);

    /// <summary>Every component times <paramref name="scale"/>.</summary>
    public static Vector3<T> operator *(Vector3<T> value, T scale) => new(value.X * scale, value.Y * scale, value.Z * scale);

    /// <summary>Every component times <paramref name="scale"/>.</summary>
    public static Vector3<T> operator *(T scale, Vector3<T> value) => value * scale;

    /// <summary>Every component divided by <paramref name="scale"/>.</summary>
    public static Vector3<T> operator /(Vector3<T> value, T scale) => new(value.X / scale, value.Y / scale, value.Z / scale);

    /// <summary><paramref name="scale"/> divided by every component.</summary>
    public static Vector3<T> operator /(T scale, Vector3<T> value) => new(scale / value.X, scale / value.Y, scale / value.Z);

    /// <summary>The component-wise quotient.</summary>
    public static Vector3<T> operator /(Vector3<T> value, Vector3<T> scale) =>
        new(value.X / scale.X, value.Y / scale.Y, value.Z / scale.Z);

    /// <summary>Whether the components are equal; NaN equals nothing.</summary>
    public static bool operator ==(Vector3<T> left, Vector3<T> right) =>
        left.X == right.X && left.Y == right.Y && left.Z == right.Z;

    /// <summary>Whether any component differs; NaN differs from everything.</summary>
    public static bool operator !=(Vector3<T> left, Vector3<T> right) => !(left == right);

    /// <summary>X and Y.</summary>
    public static explicit operator Vector2<T>(Vector3<T> value) => new(value.X, value.Y);

    /// <summary>X, Y, Z and a W of zero.</summary>
    public static explicit operator Vector4<T>(Vector3<T> value) => new(value, T.Zero);

    /// <summary>Whether the components are equal, a NaN to a NaN too - as a dictionary key needs.</summary>
    public readonly bool Equals(Vector3<T> other) => X.Equals(other.X) && Y.Equals(other.Y) && Z.Equals(other.Z);

    /// <inheritdoc />
    public override readonly bool Equals(object obj) => obj is Vector3<T> other && Equals(other);

    /// <inheritdoc />
    public override readonly int GetHashCode() => HashCode.Combine(X, Y, Z);

    /// <inheritdoc />
    public override readonly string ToString() => ToString(null, CultureInfo.CurrentCulture);

    /// <summary>The vector with each component written in <paramref name="format"/>.</summary>
    public readonly string ToString(string format) => ToString(format, CultureInfo.CurrentCulture);

    /// <summary>The vector written for <paramref name="formatProvider"/>.</summary>
    public readonly string ToString(IFormatProvider formatProvider) => ToString(null, formatProvider);

    /// <inheritdoc />
    public readonly string ToString(string format, IFormatProvider formatProvider) =>
        string.Format(formatProvider, "X:{0} Y:{1} Z:{2}", X.ToString(format, formatProvider),
            Y.ToString(format, formatProvider), Z.ToString(format, formatProvider));

    private static T ClampOne(T value, T min, T max)
    {
        value = value > max ? max : value;
        return value < min ? min : value;
    }
}
