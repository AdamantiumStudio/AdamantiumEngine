using System;
using System.Globalization;
using System.Numerics;
using System.Runtime.CompilerServices;
using System.Runtime.InteropServices;

namespace Adamantium.Mathematics;

/// <summary>
/// A 2D transform as a 3x2 matrix of fractional numbers - <c>Matrix3x2&lt;float&gt;</c> or <c>Matrix3x2&lt;double&gt;</c> -
/// stored as three rows, for row vectors: the first two rows turn and scale, the third moves, a point is transformed as
/// <see cref="Vector2.Transform{T}(Vector2{T}, Matrix3x2{T})"/> and <c>a * b</c> applies <c>a</c> first. Made with
/// <see cref="Matrix3x2"/>'s factories. Equality is exact; <see cref="NearEqual(Matrix3x2{T}, Matrix3x2{T})"/> compares
/// within a tolerance.
/// </summary>
[StructLayout(LayoutKind.Sequential)]
public struct Matrix3x2<T> : IEquatable<Matrix3x2<T>>, IFormattable where T : unmanaged, IFloatingPointIeee754<T>
{
    /// <summary>The first row: M11, M12.</summary>
    public Vector2<T> Row1;

    /// <summary>The second row: M21, M22.</summary>
    public Vector2<T> Row2;

    /// <summary>The third row: M31, M32 - the translation.</summary>
    public Vector2<T> Row3;

    /// <summary>A matrix of the three rows.</summary>
    public Matrix3x2(Vector2<T> row1, Vector2<T> row2, Vector2<T> row3)
    {
        Row1 = row1;
        Row2 = row2;
        Row3 = row3;
    }

    /// <summary>A matrix of <paramref name="value"/> in every element.</summary>
    public Matrix3x2(T value) : this(new Vector2<T>(value), new Vector2<T>(value), new Vector2<T>(value))
    {
    }

    /// <summary>A matrix of the six elements, row by row.</summary>
    public Matrix3x2(T m11, T m12, T m21, T m22, T m31, T m32) : this(new Vector2<T>(m11, m12), new Vector2<T>(m21, m22),
        new Vector2<T>(m31, m32))
    {
    }

    /// <summary>A matrix of the first six numbers in <paramref name="values"/>, row by row.</summary>
    /// <exception cref="ArgumentOutOfRangeException"><paramref name="values"/> holds fewer than six numbers.</exception>
    public Matrix3x2(ReadOnlySpan<T> values)
    {
        if (values.Length < 6)
        {
            throw new ArgumentOutOfRangeException(nameof(values), "There must be six input values for Matrix3x2.");
        }

        Row1 = new Vector2<T>(values[..2]);
        Row2 = new Vector2<T>(values[2..4]);
        Row3 = new Vector2<T>(values[4..6]);
    }

    /// <summary>The size of the matrix in bytes.</summary>
    public static int SizeInBytes => Unsafe.SizeOf<Matrix3x2<T>>();

    /// <summary>All zero.</summary>
    public static Matrix3x2<T> Zero => default;

    /// <summary>No transform.</summary>
    public static Matrix3x2<T> Identity => new(Vector2<T>.UnitX, Vector2<T>.UnitY, Vector2<T>.Zero);

    /// <summary>Row 1, column 1.</summary>
    public T M11 { readonly get => Row1.X; set => Row1.X = value; }

    /// <summary>Row 1, column 2.</summary>
    public T M12 { readonly get => Row1.Y; set => Row1.Y = value; }

    /// <summary>Row 2, column 1.</summary>
    public T M21 { readonly get => Row2.X; set => Row2.X = value; }

    /// <summary>Row 2, column 2.</summary>
    public T M22 { readonly get => Row2.Y; set => Row2.Y = value; }

    /// <summary>Row 3, column 1: the X translation.</summary>
    public T M31 { readonly get => Row3.X; set => Row3.X = value; }

    /// <summary>Row 3, column 2: the Y translation.</summary>
    public T M32 { readonly get => Row3.Y; set => Row3.Y = value; }

    /// <summary>The first column: M11, M21, M31.</summary>
    public Vector3<T> Column1
    {
        readonly get => new(Row1.X, Row2.X, Row3.X);
        set => (Row1.X, Row2.X, Row3.X) = (value.X, value.Y, value.Z);
    }

    /// <summary>The second column: M12, M22, M32.</summary>
    public Vector3<T> Column2
    {
        readonly get => new(Row1.Y, Row2.Y, Row3.Y);
        set => (Row1.Y, Row2.Y, Row3.Y) = (value.X, value.Y, value.Z);
    }

    /// <summary>The translation: M31, M32.</summary>
    public Vector2<T> TranslationVector
    {
        readonly get => Row3;
        set => Row3 = value;
    }

    /// <summary>The diagonal: M11, M22 - the scale of a matrix without rotation.</summary>
    public Vector2<T> ScaleVector
    {
        readonly get => new(Row1.X, Row2.Y);
        set => (Row1.X, Row2.Y) = (value.X, value.Y);
    }

    /// <summary>Whether this is exactly <see cref="Identity"/>.</summary>
    public readonly bool IsIdentity => this == Identity;

    /// <summary>The element at <paramref name="index"/>, row by row: 0 is M11, 2 is M21, 5 is M32.</summary>
    /// <exception cref="ArgumentOutOfRangeException"><paramref name="index"/> is not 0 to 5.</exception>
    public T this[int index]
    {
        readonly get
        {
            CheckIndex(index);
            return Unsafe.Add(ref Unsafe.As<Vector2<T>, T>(ref Unsafe.AsRef(in Row1)), index);
        }
        set
        {
            CheckIndex(index);
            Unsafe.Add(ref Unsafe.As<Vector2<T>, T>(ref Row1), index) = value;
        }
    }

    /// <summary>The element in <paramref name="row"/>, from 0 to 2, and <paramref name="column"/>, from 0 to 1.</summary>
    /// <exception cref="ArgumentOutOfRangeException"><paramref name="row"/> is not 0 to 2 or <paramref name="column"/> is
    /// not 0 to 1.</exception>
    public T this[int row, int column]
    {
        readonly get
        {
            CheckRowAndColumn(row, column);
            return this[row * 2 + column];
        }
        set
        {
            CheckRowAndColumn(row, column);
            this[row * 2 + column] = value;
        }
    }

    /// <summary>The determinant of the turn and scale: how the transform scales areas.</summary>
    public readonly T Determinant() => M11 * M22 - M12 * M21;

    /// <summary>Makes this its inverse, or <see cref="Identity"/> when the turn and scale are singular.</summary>
    public void Invert() => this = Invert(this);

    /// <summary>The elements as an array, row by row.</summary>
    public readonly T[] ToArray() => [M11, M12, M21, M22, M31, M32];

    /// <summary>The matrix in another number type, each element converted as a cast would.</summary>
    public readonly Matrix3x2<TOther> As<TOther>() where TOther : unmanaged, IFloatingPointIeee754<TOther> =>
        new(Row1.As<TOther>(), Row2.As<TOther>(), Row3.As<TOther>());

    /// <summary>The element-wise sum.</summary>
    public static Matrix3x2<T> Add(Matrix3x2<T> left, Matrix3x2<T> right) => left + right;

    /// <summary>The element-wise difference.</summary>
    public static Matrix3x2<T> Subtract(Matrix3x2<T> left, Matrix3x2<T> right) => left - right;

    /// <summary>Every element times <paramref name="right"/>.</summary>
    public static Matrix3x2<T> Multiply(Matrix3x2<T> left, T right) => left * right;

    /// <summary>The product: <paramref name="left"/> applied first, then <paramref name="right"/>.</summary>
    public static Matrix3x2<T> Multiply(Matrix3x2<T> left, Matrix3x2<T> right) => left * right;

    /// <summary>Every element divided by <paramref name="right"/>.</summary>
    public static Matrix3x2<T> Divide(Matrix3x2<T> left, T right) => left / right;

    /// <summary>The element-wise quotient.</summary>
    public static Matrix3x2<T> Divide(Matrix3x2<T> left, Matrix3x2<T> right) => left / right;

    /// <summary>Every element negated.</summary>
    public static Matrix3x2<T> Negate(Matrix3x2<T> value) => -value;

    /// <summary>The element-wise blend of <paramref name="start"/> and <paramref name="end"/> at
    /// <paramref name="amount"/>: start at 0, end at 1.</summary>
    public static Matrix3x2<T> Lerp(Matrix3x2<T> start, Matrix3x2<T> end, T amount) =>
        new(Vector2<T>.Lerp(start.Row1, end.Row1, amount), Vector2<T>.Lerp(start.Row2, end.Row2, amount),
            Vector2<T>.Lerp(start.Row3, end.Row3, amount));

    /// <summary>The element-wise blend of <paramref name="start"/> and <paramref name="end"/> at
    /// <paramref name="amount"/> eased in and out by <see cref="MathHelper.Scalar.SmoothStep{T}"/>.</summary>
    public static Matrix3x2<T> SmoothStep(Matrix3x2<T> start, Matrix3x2<T> end, T amount) =>
        Lerp(start, end, MathHelper.Scalar.SmoothStep(amount));

    /// <summary>The inverse; <see cref="Identity"/> when the turn and scale are singular, so a transform squashed flat
    /// undoes to nothing rather than to infinities. Where the determinant leaves the normal range - elements far from one
    /// or far apart - the turn and scale are first scaled near one by powers of two, which is exact, and the scale taken
    /// back out.</summary>
    public static Matrix3x2<T> Invert(Matrix3x2<T> value)
    {
        var result = InvertTurn(value, out var determinant);
        var sum = result.Row1 + result.Row2;
        if (!T.IsNormal(determinant) || !T.IsFinite(sum.X + sum.Y))
        {
            if (!InvertTurnScaled(value, out result))
            {
                return Identity;
            }
        }

        result.Row3 = -(value.M31 * result.Row1 + value.M32 * result.Row2);
        return result;
    }

    /// <summary>The element-wise sum.</summary>
    public static Matrix3x2<T> operator +(Matrix3x2<T> left, Matrix3x2<T> right) =>
        new(left.Row1 + right.Row1, left.Row2 + right.Row2, left.Row3 + right.Row3);

    /// <summary>The matrix itself.</summary>
    public static Matrix3x2<T> operator +(Matrix3x2<T> value) => value;

    /// <summary>The element-wise difference.</summary>
    public static Matrix3x2<T> operator -(Matrix3x2<T> left, Matrix3x2<T> right) =>
        new(left.Row1 - right.Row1, left.Row2 - right.Row2, left.Row3 - right.Row3);

    /// <summary>Every element negated.</summary>
    public static Matrix3x2<T> operator -(Matrix3x2<T> value) => new(-value.Row1, -value.Row2, -value.Row3);

    /// <summary>Every element times <paramref name="left"/>.</summary>
    public static Matrix3x2<T> operator *(T left, Matrix3x2<T> right) => right * left;

    /// <summary>Every element times <paramref name="right"/>.</summary>
    public static Matrix3x2<T> operator *(Matrix3x2<T> left, T right) => new(left.Row1 * right, left.Row2 * right, left.Row3 * right);

    /// <summary>The product: <paramref name="left"/> applied first, then <paramref name="right"/>.</summary>
    public static Matrix3x2<T> operator *(Matrix3x2<T> left, Matrix3x2<T> right) =>
        new(left.M11 * right.Row1 + left.M12 * right.Row2, left.M21 * right.Row1 + left.M22 * right.Row2,
            left.M31 * right.Row1 + left.M32 * right.Row2 + right.Row3);

    /// <summary>Every element divided by <paramref name="right"/>.</summary>
    public static Matrix3x2<T> operator /(Matrix3x2<T> left, T right) => left * (T.One / right);

    /// <summary>The element-wise quotient.</summary>
    public static Matrix3x2<T> operator /(Matrix3x2<T> left, Matrix3x2<T> right) =>
        new(left.Row1 / right.Row1, left.Row2 / right.Row2, left.Row3 / right.Row3);

    /// <summary>Whether the elements are equal; NaN equals nothing.</summary>
    public static bool operator ==(Matrix3x2<T> left, Matrix3x2<T> right) =>
        left.Row1 == right.Row1 && left.Row2 == right.Row2 && left.Row3 == right.Row3;

    /// <summary>Whether any element differs; NaN differs from everything.</summary>
    public static bool operator !=(Matrix3x2<T> left, Matrix3x2<T> right) => !(left == right);

    /// <summary>The 2D part of a 4x4 transform: its upper left 2x2 and its X and Y translation.</summary>
    public static explicit operator Matrix3x2<T>(Matrix4x4<T> value) =>
        new(value.M11, value.M12, value.M21, value.M22, value.M41, value.M42);

    /// <summary>Whether each element of the two is within <see cref="MathHelper.Scalar.NearEqual{T}"/>.</summary>
    public static bool NearEqual(Matrix3x2<T> left, Matrix3x2<T> right) =>
        Vector2<T>.NearEqual(left.Row1, right.Row1) && Vector2<T>.NearEqual(left.Row2, right.Row2)
                                                    && Vector2<T>.NearEqual(left.Row3, right.Row3);

    /// <summary>Whether the elements are equal, a NaN to a NaN too - as a dictionary key needs.</summary>
    public readonly bool Equals(Matrix3x2<T> other) => Row1.Equals(other.Row1) && Row2.Equals(other.Row2) && Row3.Equals(other.Row3);

    /// <inheritdoc />
    public override readonly bool Equals(object obj) => obj is Matrix3x2<T> other && Equals(other);

    /// <inheritdoc />
    public override readonly int GetHashCode() => HashCode.Combine(Row1, Row2, Row3);

    /// <inheritdoc />
    public override readonly string ToString() => ToString(null, CultureInfo.CurrentCulture);

    /// <summary>The matrix with each element written in <paramref name="format"/>.</summary>
    public readonly string ToString(string format) => ToString(format, CultureInfo.CurrentCulture);

    /// <summary>The matrix written for <paramref name="formatProvider"/>.</summary>
    public readonly string ToString(IFormatProvider formatProvider) => ToString(null, formatProvider);

    /// <inheritdoc />
    public readonly string ToString(string format, IFormatProvider formatProvider) =>
        string.Format(formatProvider, "[M11:{0} M12:{1}] [M21:{2} M22:{3}] [M31:{4} M32:{5}]",
            M11.ToString(format, formatProvider), M12.ToString(format, formatProvider), M21.ToString(format, formatProvider),
            M22.ToString(format, formatProvider), M31.ToString(format, formatProvider), M32.ToString(format, formatProvider));

    [MethodImpl(MethodImplOptions.NoInlining)]
    private static bool InvertTurnScaled(Matrix3x2<T> value, out Matrix3x2<T> result)
    {
        Span<int> rowExponents = stackalloc int[2];
        Span<int> columnExponents = stackalloc int[2];
        PowerOfTwoScaling.Equilibrate(Elements(ref value)[..4], 2, rowExponents, columnExponents);
        result = InvertTurn(value, out var determinant);
        PowerOfTwoScaling.UnscaleInverse(Elements(ref result)[..4], 2, rowExponents, columnExponents);
        return determinant != T.Zero;
    }

    private static Matrix3x2<T> InvertTurn(Matrix3x2<T> value, out T determinant)
    {
        determinant = value.Determinant();
        if (determinant == T.Zero)
        {
            return Zero;
        }

        var inverse = T.One / determinant;
        return new Matrix3x2<T>(value.M22 * inverse, -value.M12 * inverse, -value.M21 * inverse, value.M11 * inverse, T.Zero,
            T.Zero);
    }

    private static Span<T> Elements(ref Matrix3x2<T> value) =>
        MemoryMarshal.CreateSpan(ref Unsafe.As<Vector2<T>, T>(ref value.Row1), 6);

    private static void CheckIndex(int index)
    {
        if ((uint)index > 5)
        {
            throw new ArgumentOutOfRangeException(nameof(index), "Indices for Matrix3x2 run from 0 to 5, inclusive.");
        }
    }

    private static void CheckRowAndColumn(int row, int column)
    {
        if ((uint)row > 2)
        {
            throw new ArgumentOutOfRangeException(nameof(row), "Rows for Matrix3x2 run from 0 to 2, inclusive.");
        }

        if ((uint)column > 1)
        {
            throw new ArgumentOutOfRangeException(nameof(column), "Columns for Matrix3x2 run from 0 to 1, inclusive.");
        }
    }
}
