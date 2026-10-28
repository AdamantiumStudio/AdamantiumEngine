using System;
using System.Globalization;
using System.Numerics;
using System.Runtime.CompilerServices;
using System.Runtime.InteropServices;

namespace Adamantium.Mathematics.Typed;

/// <summary>
/// A rotation as a quaternion of fractional numbers - <c>Quaternion&lt;float&gt;</c> or <c>Quaternion&lt;double&gt;</c> -
/// laid out X, Y, Z, W, made with <see cref="Quaternion"/>'s factories. Equality is exact;
/// <see cref="NearEqual(Quaternion{T}, Quaternion{T})"/> compares within a tolerance.
/// </summary>
[StructLayout(LayoutKind.Sequential)]
public struct Quaternion<T> : IEquatable<Quaternion<T>>, IFormattable where T : unmanaged, IFloatingPointIeee754<T>
{
    /// <summary>The X component: the rotation axis times the sine of half the angle.</summary>
    public T X;

    /// <summary>The Y component: the rotation axis times the sine of half the angle.</summary>
    public T Y;

    /// <summary>The Z component: the rotation axis times the sine of half the angle.</summary>
    public T Z;

    /// <summary>The W component: the cosine of half the angle.</summary>
    public T W;

    /// <summary>A quaternion of <paramref name="x"/>, <paramref name="y"/>, <paramref name="z"/> and <paramref name="w"/>.</summary>
    public Quaternion(T x, T y, T z, T w)
    {
        X = x;
        Y = y;
        Z = z;
        W = w;
    }

    /// <summary>A quaternion of <paramref name="value"/> in every component.</summary>
    public Quaternion(T value) : this(value, value, value, value)
    {
    }

    /// <summary>A quaternion of <paramref name="value"/>'s components.</summary>
    public Quaternion(Vector4<T> value) : this(value.X, value.Y, value.Z, value.W)
    {
    }

    /// <summary>A quaternion of <paramref name="value"/>'s X, Y and Z and <paramref name="w"/>.</summary>
    public Quaternion(Vector3<T> value, T w) : this(value.X, value.Y, value.Z, w)
    {
    }

    /// <summary>A quaternion of <paramref name="value"/>'s X and Y and <paramref name="z"/> and <paramref name="w"/>.</summary>
    public Quaternion(Vector2<T> value, T z, T w) : this(value.X, value.Y, z, w)
    {
    }

    /// <summary>A quaternion of the four numbers in <paramref name="values"/>.</summary>
    /// <exception cref="ArgumentOutOfRangeException"><paramref name="values"/> does not hold four numbers.</exception>
    public Quaternion(ReadOnlySpan<T> values)
    {
        if (values.Length != 4)
        {
            throw new ArgumentOutOfRangeException(nameof(values), "There must be four input values for Quaternion.");
        }

        X = values[0];
        Y = values[1];
        Z = values[2];
        W = values[3];
    }

    /// <summary>The size of the quaternion in bytes.</summary>
    public static int SizeInBytes => Unsafe.SizeOf<Quaternion<T>>();

    /// <summary>All zero.</summary>
    public static Quaternion<T> Zero => default;

    /// <summary>All one.</summary>
    public static Quaternion<T> One => new(T.One);

    /// <summary>No rotation.</summary>
    public static Quaternion<T> Identity => new(T.Zero, T.Zero, T.Zero, T.One);

    /// <summary>Whether this is exactly <see cref="Identity"/>.</summary>
    public readonly bool IsIdentity => this == Identity;

    /// <summary>Whether the length is one within <see cref="Scalar.ZeroTolerance{T}"/>.</summary>
    public readonly bool IsNormalized => Scalar.IsZero(LengthSquared() - T.One);

    /// <summary>The rotation angle in radians; zero when the axis part is within <see cref="Scalar.ZeroTolerance{T}"/>
    /// of zero.</summary>
    public readonly T Angle
    {
        get
        {
            if (Scalar.IsZero(X * X + Y * Y + Z * Z))
            {
                return T.Zero;
            }

            return T.CreateTruncating(2) * T.Acos(T.Clamp(W, -T.One, T.One));
        }
    }

    /// <summary>The rotation axis, of length one; the X axis when the axis part is within
    /// <see cref="Scalar.ZeroTolerance{T}"/> of zero.</summary>
    public readonly Vector3<T> Axis
    {
        get
        {
            var lengthSquared = X * X + Y * Y + Z * Z;
            if (Scalar.IsZero(lengthSquared))
            {
                return Vector3<T>.UnitX;
            }

            var inverse = T.One / T.Sqrt(lengthSquared);
            return new Vector3<T>(X * inverse, Y * inverse, Z * inverse);
        }
    }

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
            _ => throw new ArgumentOutOfRangeException(nameof(index), "Indices for Quaternion run from 0 to 3, inclusive.")
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
                    throw new ArgumentOutOfRangeException(nameof(index), "Indices for Quaternion run from 0 to 3, inclusive.");
            }
        }
    }

    /// <summary>The length.</summary>
    public readonly T Length() => T.Sqrt(LengthSquared());

    /// <summary>The square of the length: the dot product with itself.</summary>
    public readonly T LengthSquared() => X * X + Y * Y + Z * Z + W * W;

    /// <summary>Negates the axis part: the opposite rotation of a normalized quaternion.</summary>
    public void Conjugate()
    {
        X = -X;
        Y = -Y;
        Z = -Z;
    }

    /// <summary>Makes this its inverse; leaves it as it is when its length is within
    /// <see cref="Scalar.ZeroTolerance{T}"/> of zero.</summary>
    public void Invert()
    {
        var lengthSquared = LengthSquared();
        if (!Scalar.IsZero(lengthSquared))
        {
            var inverse = T.One / lengthSquared;
            X = -X * inverse;
            Y = -Y * inverse;
            Z = -Z * inverse;
            W *= inverse;
        }
    }

    /// <summary>Scales this to a length of one; leaves it as it is when its length is within
    /// <see cref="Scalar.ZeroTolerance{T}"/> of zero.</summary>
    public void Normalize()
    {
        var length = Length();
        if (!Scalar.IsZero(length))
        {
            var inverse = T.One / length;
            X *= inverse;
            Y *= inverse;
            Z *= inverse;
            W *= inverse;
        }
    }

    /// <summary>The yaw (around Y), pitch (around X) and roll (around Z) in radians that
    /// <see cref="Quaternion.CreateFromYawPitchRoll{T}"/> turns back into this rotation; at straight up or down the
    /// turn goes to the yaw and the roll is zero.</summary>
    public readonly void ToYawPitchRoll(out T yaw, out T pitch, out T roll)
    {
        var (x, y, z, w) = (double.CreateTruncating(X), double.CreateTruncating(Y), double.CreateTruncating(Z),
            double.CreateTruncating(W));
        var largest = Math.Max(Math.Max(Math.Abs(x), Math.Abs(y)), Math.Max(Math.Abs(z), Math.Abs(w)));
        if (largest == 0)
        {
            yaw = pitch = roll = T.Zero;
            return;
        }

        (x, y, z, w) = (x / largest, y / largest, z / largest, w / largest);

        var up = Math.Sqrt((w + x) * (w + x) + (y - z) * (y - z));
        var down = Math.Sqrt((w - x) * (w - x) + (y + z) * (y + z));
        var halfSum = Math.Atan2(y + z, w - x);
        var halfDifference = Math.Atan2(y - z, w + x);
        var epsilon = double.CreateTruncating(T.BitIncrement(T.One) - T.One);
        if (down <= epsilon * up)
        {
            halfSum = halfDifference;
        }
        else if (up <= epsilon * down)
        {
            halfDifference = halfSum;
        }

        pitch = T.CreateTruncating(2 * Math.Atan2(up - down, up + down));
        yaw = T.CreateTruncating(Math.IEEERemainder(halfSum + halfDifference, 2 * Math.PI));
        roll = T.CreateTruncating(Math.IEEERemainder(halfSum - halfDifference, 2 * Math.PI));
    }

    /// <summary>The components as an array: X, Y, Z, W.</summary>
    public readonly T[] ToArray() => [X, Y, Z, W];

    /// <summary>The quaternion in another number type, each component converted as a cast would.</summary>
    public readonly Quaternion<TOther> As<TOther>() where TOther : unmanaged, IFloatingPointIeee754<TOther> =>
        new(TOther.CreateTruncating(X), TOther.CreateTruncating(Y), TOther.CreateTruncating(Z), TOther.CreateTruncating(W));

    /// <summary>The component-wise sum.</summary>
    public static Quaternion<T> Add(Quaternion<T> left, Quaternion<T> right) => left + right;

    /// <summary>The component-wise difference.</summary>
    public static Quaternion<T> Subtract(Quaternion<T> left, Quaternion<T> right) => left - right;

    /// <summary>Every component times <paramref name="scale"/>.</summary>
    public static Quaternion<T> Multiply(Quaternion<T> value, T scale) => value * scale;

    /// <summary>The product: the rotation <paramref name="right"/> applied first, then <paramref name="left"/>.</summary>
    public static Quaternion<T> Multiply(Quaternion<T> left, Quaternion<T> right) => left * right;

    /// <summary>Every component negated: the same rotation.</summary>
    public static Quaternion<T> Negate(Quaternion<T> value) => -value;

    /// <summary><paramref name="value"/> with its axis part negated.</summary>
    public static Quaternion<T> Conjugate(Quaternion<T> value)
    {
        value.Conjugate();
        return value;
    }

    /// <summary>The inverse of <paramref name="value"/>, or it as it is when its length is within
    /// <see cref="Scalar.ZeroTolerance{T}"/> of zero.</summary>
    public static Quaternion<T> Invert(Quaternion<T> value)
    {
        value.Invert();
        return value;
    }

    /// <summary><paramref name="value"/> scaled to a length of one, or as it is when its length is within
    /// <see cref="Scalar.ZeroTolerance{T}"/> of zero.</summary>
    public static Quaternion<T> Normalize(Quaternion<T> value)
    {
        value.Normalize();
        return value;
    }

    /// <summary>The dot product.</summary>
    public static T Dot(Quaternion<T> left, Quaternion<T> right) =>
        left.X * right.X + left.Y * right.Y + left.Z * right.Z + left.W * right.W;

    /// <summary>The rotation vector in degrees: <see cref="Axis"/> times <see cref="Angle"/>, each component turned
    /// into degrees.</summary>
    public static Vector3<T> Euler(Quaternion<T> rotation)
    {
        var turn = rotation.Axis * rotation.Angle;
        var toDegrees = T.CreateTruncating(180) / T.Pi;
        return new Vector3<T>(turn.X * toDegrees, turn.Y * toDegrees, turn.Z * toDegrees);
    }

    /// <summary>The exponential of a pure quaternion: the rotation <paramref name="value"/>'s axis part describes.</summary>
    public static Quaternion<T> Exponential(Quaternion<T> value)
    {
        var angle = T.Sqrt(value.X * value.X + value.Y * value.Y + value.Z * value.Z);
        var result = value;
        if (angle != T.Zero)
        {
            var coefficient = T.Sin(angle) / angle;
            result.X = coefficient * value.X;
            result.Y = coefficient * value.Y;
            result.Z = coefficient * value.Z;
        }

        result.W = T.Cos(angle);
        return result;
    }

    /// <summary>The logarithm of a unit quaternion: a pure quaternion of its axis times half its angle.</summary>
    public static Quaternion<T> Logarithm(Quaternion<T> value)
    {
        var result = value;
        var length = T.Sqrt(value.X * value.X + value.Y * value.Y + value.Z * value.Z);
        if (length != T.Zero)
        {
            var coefficient = T.Atan2(length, value.W) / length;
            result.X = value.X * coefficient;
            result.Y = value.Y * coefficient;
            result.Z = value.Z * coefficient;
        }

        result.W = T.Zero;
        return result;
    }

    /// <summary>The normalized straight blend of <paramref name="start"/> and <paramref name="end"/> at
    /// <paramref name="amount"/>, along the shorter way round.</summary>
    public static Quaternion<T> Lerp(Quaternion<T> start, Quaternion<T> end, T amount)
    {
        var inverse = T.One - amount;
        var toEnd = Dot(start, end) >= T.Zero ? amount : -amount;
        var result = new Quaternion<T>(inverse * start.X + toEnd * end.X, inverse * start.Y + toEnd * end.Y,
            inverse * start.Z + toEnd * end.Z, inverse * start.W + toEnd * end.W);
        result.Normalize();
        return result;
    }

    /// <summary>The blend of <paramref name="start"/> and <paramref name="end"/> at <paramref name="amount"/> at an even
    /// turning speed, along the shorter way round.</summary>
    public static Quaternion<T> Slerp(Quaternion<T> start, Quaternion<T> end, T amount)
    {
        T inverse;
        T opposite;
        var dot = Dot(start, end);
        var sign = dot < T.Zero ? -T.One : T.One;
        if (T.Abs(dot) > T.One - Scalar.ZeroTolerance<T>())
        {
            inverse = T.One - amount;
            opposite = amount * sign;
        }
        else
        {
            var acos = T.Acos(T.Abs(dot));
            var inverseSin = T.One / T.Sin(acos);
            inverse = T.Sin((T.One - amount) * acos) * inverseSin;
            opposite = T.Sin(amount * acos) * inverseSin * sign;
        }

        return new Quaternion<T>(inverse * start.X + opposite * end.X, inverse * start.Y + opposite * end.Y,
            inverse * start.Z + opposite * end.Z, inverse * start.W + opposite * end.W);
    }

    /// <summary>The rotation at <paramref name="amount1"/> towards <paramref name="value2"/> and
    /// <paramref name="amount2"/> towards <paramref name="value3"/> from <paramref name="value1"/>, by spherical
    /// blends.</summary>
    public static Quaternion<T> Barycentric(Quaternion<T> value1, Quaternion<T> value2, Quaternion<T> value3, T amount1,
        T amount2)
    {
        var total = amount1 + amount2;
        var start = Slerp(value1, value2, total);
        if (total == T.Zero)
        {
            return start;
        }

        var end = Slerp(value1, value3, total);
        return Slerp(start, end, amount2 / total);
    }

    /// <summary>The spherical quadrangle blend at <paramref name="amount"/>: the smooth curve from
    /// <paramref name="value1"/> to <paramref name="value4"/> shaped by <paramref name="value2"/> and
    /// <paramref name="value3"/>, as <see cref="SquadSetup"/> gives them.</summary>
    public static Quaternion<T> Squad(Quaternion<T> value1, Quaternion<T> value2, Quaternion<T> value3, Quaternion<T> value4,
        T amount)
    {
        var start = Slerp(value1, value4, amount);
        var end = Slerp(value2, value3, amount);
        return Slerp(start, end, T.CreateTruncating(2) * amount * (T.One - amount));
    }

    /// <summary>The control points for <see cref="Squad"/> between <paramref name="value2"/> and
    /// <paramref name="value3"/> of the four rotations in order.</summary>
    public static (Quaternion<T> Start, Quaternion<T> End, Quaternion<T> To) SquadSetup(Quaternion<T> value1,
        Quaternion<T> value2, Quaternion<T> value3, Quaternion<T> value4)
    {
        var q0 = (value1 + value2).LengthSquared() < (value1 - value2).LengthSquared() ? -value1 : value1;
        var q2 = (value2 + value3).LengthSquared() < (value2 - value3).LengthSquared() ? -value3 : value3;
        var q3 = (q2 + value4).LengthSquared() < (q2 - value4).LengthSquared() ? -value4 : value4;
        var q1 = value2;
        var q1Inverse = Invert(q1);
        var q2Inverse = Invert(q2);
        var quarter = T.CreateTruncating(-0.25);
        return (q1 * Exponential(quarter * (Logarithm(q1Inverse * q2) + Logarithm(q1Inverse * q0))),
            q2 * Exponential(quarter * (Logarithm(q2Inverse * q3) + Logarithm(q2Inverse * q1))), q2);
    }

    /// <summary>The component-wise sum.</summary>
    public static Quaternion<T> operator +(Quaternion<T> left, Quaternion<T> right) =>
        new(left.X + right.X, left.Y + right.Y, left.Z + right.Z, left.W + right.W);

    /// <summary>The component-wise difference.</summary>
    public static Quaternion<T> operator -(Quaternion<T> left, Quaternion<T> right) =>
        new(left.X - right.X, left.Y - right.Y, left.Z - right.Z, left.W - right.W);

    /// <summary>Every component negated: the same rotation.</summary>
    public static Quaternion<T> operator -(Quaternion<T> value) => new(-value.X, -value.Y, -value.Z, -value.W);

    /// <summary>Every component times <paramref name="scale"/>.</summary>
    public static Quaternion<T> operator *(Quaternion<T> value, T scale) =>
        new(value.X * scale, value.Y * scale, value.Z * scale, value.W * scale);

    /// <summary>Every component times <paramref name="scale"/>.</summary>
    public static Quaternion<T> operator *(T scale, Quaternion<T> value) => value * scale;

    /// <summary>The product: the rotation <paramref name="right"/> applied first, then <paramref name="left"/>.</summary>
    public static Quaternion<T> operator *(Quaternion<T> left, Quaternion<T> right)
    {
        var a = left.Y * right.Z - left.Z * right.Y;
        var b = left.Z * right.X - left.X * right.Z;
        var c = left.X * right.Y - left.Y * right.X;
        var d = left.X * right.X + left.Y * right.Y + left.Z * right.Z;
        return new Quaternion<T>(left.X * right.W + right.X * left.W + a, left.Y * right.W + right.Y * left.W + b,
            left.Z * right.W + right.Z * left.W + c, left.W * right.W - d);
    }

    /// <summary>Whether the components are equal; NaN equals nothing.</summary>
    public static bool operator ==(Quaternion<T> left, Quaternion<T> right) =>
        left.X == right.X && left.Y == right.Y && left.Z == right.Z && left.W == right.W;

    /// <summary>Whether any component differs; NaN differs from everything.</summary>
    public static bool operator !=(Quaternion<T> left, Quaternion<T> right) => !(left == right);

    /// <summary>Whether each component of the two is within <see cref="Scalar.NearEqual{T}"/>.</summary>
    public static bool NearEqual(Quaternion<T> left, Quaternion<T> right) =>
        Scalar.NearEqual(left.X, right.X) && Scalar.NearEqual(left.Y, right.Y) && Scalar.NearEqual(left.Z, right.Z)
        && Scalar.NearEqual(left.W, right.W);

    /// <summary>Whether the components are equal, a NaN to a NaN too - as a dictionary key needs.</summary>
    public readonly bool Equals(Quaternion<T> other) =>
        X.Equals(other.X) && Y.Equals(other.Y) && Z.Equals(other.Z) && W.Equals(other.W);

    /// <inheritdoc />
    public override readonly bool Equals(object obj) => obj is Quaternion<T> other && Equals(other);

    /// <inheritdoc />
    public override readonly int GetHashCode() => HashCode.Combine(X, Y, Z, W);

    /// <inheritdoc />
    public override readonly string ToString() => ToString(null, CultureInfo.CurrentCulture);

    /// <summary>The quaternion with each component written in <paramref name="format"/>.</summary>
    public readonly string ToString(string format) => ToString(format, CultureInfo.CurrentCulture);

    /// <summary>The quaternion written for <paramref name="formatProvider"/>.</summary>
    public readonly string ToString(IFormatProvider formatProvider) => ToString(null, formatProvider);

    /// <inheritdoc />
    public readonly string ToString(string format, IFormatProvider formatProvider) =>
        string.Format(formatProvider, "X:{0} Y:{1} Z:{2} W:{3}", X.ToString(format, formatProvider),
            Y.ToString(format, formatProvider), Z.ToString(format, formatProvider), W.ToString(format, formatProvider));
}
