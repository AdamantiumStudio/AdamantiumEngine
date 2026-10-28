using System;
using System.Globalization;
using System.Numerics;
using System.Runtime.CompilerServices;
using System.Runtime.InteropServices;

namespace Adamantium.Mathematics.Typed;

/// <summary>
/// A 3x3 matrix of fractional numbers - <c>Matrix3x3&lt;float&gt;</c> or <c>Matrix3x3&lt;double&gt;</c> - stored as three
/// rows, for row vectors: a vector is transformed as <c>vector * matrix</c> and <c>a * b</c> applies <c>a</c> first.
/// Holds a rotation and scale in 3D, or a whole transform in 2D. Made with <see cref="Matrix3x3"/>'s factories.
/// Equality is exact; <see cref="NearEqual(Matrix3x3{T}, Matrix3x3{T})"/> compares within a tolerance.
/// </summary>
[StructLayout(LayoutKind.Sequential)]
public struct Matrix3x3<T> : IEquatable<Matrix3x3<T>>, IFormattable where T : unmanaged, IFloatingPointIeee754<T>
{
    /// <summary>The first row: M11, M12, M13.</summary>
    public Vector3<T> Row1;

    /// <summary>The second row: M21, M22, M23.</summary>
    public Vector3<T> Row2;

    /// <summary>The third row: M31, M32, M33.</summary>
    public Vector3<T> Row3;

    /// <summary>A matrix of the three rows.</summary>
    public Matrix3x3(Vector3<T> row1, Vector3<T> row2, Vector3<T> row3)
    {
        Row1 = row1;
        Row2 = row2;
        Row3 = row3;
    }

    /// <summary>A matrix of <paramref name="value"/> in every element.</summary>
    public Matrix3x3(T value) : this(new Vector3<T>(value), new Vector3<T>(value), new Vector3<T>(value))
    {
    }

    /// <summary>A matrix of the nine elements, row by row.</summary>
    public Matrix3x3(T m11, T m12, T m13, T m21, T m22, T m23, T m31, T m32, T m33) : this(new Vector3<T>(m11, m12, m13),
        new Vector3<T>(m21, m22, m23), new Vector3<T>(m31, m32, m33))
    {
    }

    /// <summary>A matrix of the first nine numbers in <paramref name="values"/>, row by row.</summary>
    /// <exception cref="ArgumentOutOfRangeException"><paramref name="values"/> holds fewer than nine numbers.</exception>
    public Matrix3x3(ReadOnlySpan<T> values)
    {
        if (values.Length < 9)
        {
            throw new ArgumentOutOfRangeException(nameof(values), "There must be nine input values for Matrix3x3.");
        }

        Row1 = new Vector3<T>(values[..3]);
        Row2 = new Vector3<T>(values[3..6]);
        Row3 = new Vector3<T>(values[6..9]);
    }

    /// <summary>The size of the matrix in bytes.</summary>
    public static int SizeInBytes => Unsafe.SizeOf<Matrix3x3<T>>();

    /// <summary>All zero.</summary>
    public static Matrix3x3<T> Zero => default;

    /// <summary>Ones on the diagonal, zero elsewhere: no transform.</summary>
    public static Matrix3x3<T> Identity => new(Vector3<T>.UnitX, Vector3<T>.UnitY, Vector3<T>.UnitZ);

    /// <summary>Row 1, column 1.</summary>
    public T M11 { readonly get => Row1.X; set => Row1.X = value; }

    /// <summary>Row 1, column 2.</summary>
    public T M12 { readonly get => Row1.Y; set => Row1.Y = value; }

    /// <summary>Row 1, column 3.</summary>
    public T M13 { readonly get => Row1.Z; set => Row1.Z = value; }

    /// <summary>Row 2, column 1.</summary>
    public T M21 { readonly get => Row2.X; set => Row2.X = value; }

    /// <summary>Row 2, column 2.</summary>
    public T M22 { readonly get => Row2.Y; set => Row2.Y = value; }

    /// <summary>Row 2, column 3.</summary>
    public T M23 { readonly get => Row2.Z; set => Row2.Z = value; }

    /// <summary>Row 3, column 1.</summary>
    public T M31 { readonly get => Row3.X; set => Row3.X = value; }

    /// <summary>Row 3, column 2.</summary>
    public T M32 { readonly get => Row3.Y; set => Row3.Y = value; }

    /// <summary>Row 3, column 3.</summary>
    public T M33 { readonly get => Row3.Z; set => Row3.Z = value; }

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

    /// <summary>The third column: M13, M23, M33.</summary>
    public Vector3<T> Column3
    {
        readonly get => new(Row1.Z, Row2.Z, Row3.Z);
        set => (Row1.Z, Row2.Z, Row3.Z) = (value.X, value.Y, value.Z);
    }

    /// <summary>The diagonal: M11, M22, M33 - the scale of a matrix without rotation.</summary>
    public Vector3<T> ScaleVector
    {
        readonly get => new(Row1.X, Row2.Y, Row3.Z);
        set => (Row1.X, Row2.Y, Row3.Z) = (value.X, value.Y, value.Z);
    }

    /// <summary>Whether this is exactly <see cref="Identity"/>.</summary>
    public readonly bool IsIdentity => this == Identity;

    /// <summary>The element at <paramref name="index"/>, row by row: 0 is M11, 3 is M21, 8 is M33.</summary>
    /// <exception cref="ArgumentOutOfRangeException"><paramref name="index"/> is not 0 to 8.</exception>
    public T this[int index]
    {
        readonly get
        {
            CheckIndex(index);
            return Unsafe.Add(ref Unsafe.As<Vector3<T>, T>(ref Unsafe.AsRef(in Row1)), index);
        }
        set
        {
            CheckIndex(index);
            Unsafe.Add(ref Unsafe.As<Vector3<T>, T>(ref Row1), index) = value;
        }
    }

    /// <summary>The element in <paramref name="row"/> and <paramref name="column"/>, both from 0 to 2.</summary>
    /// <exception cref="ArgumentOutOfRangeException"><paramref name="row"/> or <paramref name="column"/> is not 0 to 2.</exception>
    public T this[int row, int column]
    {
        readonly get
        {
            CheckRowAndColumn(row, column);
            return this[row * 3 + column];
        }
        set
        {
            CheckRowAndColumn(row, column);
            this[row * 3 + column] = value;
        }
    }

    /// <summary>The determinant.</summary>
    public readonly T Determinant() =>
        M11 * (M22 * M33 - M23 * M32) - M12 * (M21 * M33 - M23 * M31) + M13 * (M21 * M32 - M22 * M31);

    /// <summary>Makes this its inverse, or <see cref="Zero"/> when the determinant is zero; a nearly singular matrix
    /// gives large elements.</summary>
    public void Invert() => this = Invert(this);

    /// <summary>Swaps rows and columns.</summary>
    public void Transpose() => this = Transpose(this);

    /// <summary>Makes the rows perpendicular to one another (modified Gram-Schmidt), keeping the first.</summary>
    public void Orthogonalize() => this = Orthogonalize(this);

    /// <summary>Makes the rows perpendicular to one another and of length one (modified Gram-Schmidt).</summary>
    public void Orthonormalize() => this = Orthonormalize(this);

    /// <summary>Splits the matrix into <paramref name="q"/>, orthonormal, times <paramref name="r"/>, upper
    /// triangular.</summary>
    public readonly void DecomposeQR(out Matrix3x3<T> q, out Matrix3x3<T> r)
    {
        q = Transpose(Orthonormalize(Transpose(this)));
        r = default;
        r.M11 = Vector3<T>.Dot(q.Column1, Column1);
        r.M12 = Vector3<T>.Dot(q.Column1, Column2);
        r.M13 = Vector3<T>.Dot(q.Column1, Column3);
        r.M22 = Vector3<T>.Dot(q.Column2, Column2);
        r.M23 = Vector3<T>.Dot(q.Column2, Column3);
        r.M33 = Vector3<T>.Dot(q.Column3, Column3);
    }

    /// <summary>Splits the matrix into <paramref name="l"/>, lower triangular, times <paramref name="q"/>,
    /// orthonormal.</summary>
    public readonly void DecomposeLQ(out Matrix3x3<T> l, out Matrix3x3<T> q)
    {
        q = Orthonormalize(this);
        l = default;
        l.M11 = Vector3<T>.Dot(q.Row1, Row1);
        l.M21 = Vector3<T>.Dot(q.Row1, Row2);
        l.M22 = Vector3<T>.Dot(q.Row2, Row2);
        l.M31 = Vector3<T>.Dot(q.Row1, Row3);
        l.M32 = Vector3<T>.Dot(q.Row2, Row3);
        l.M33 = Vector3<T>.Dot(q.Row3, Row3);
    }

    /// <summary>Splits a rotation and scale into the two; a mirror - a negative determinant - comes out as a negative X
    /// scale. False, with no rotation, when a scale is within <see cref="Scalar.ZeroTolerance{T}"/> of zero.</summary>
    public readonly bool Decompose(out Vector3<T> scale, out Quaternion<T> rotation)
    {
        scale = new Vector3<T>(Row1.Length(), Row2.Length(), Row3.Length());
        if (Scalar.IsZero(scale.X) || Scalar.IsZero(scale.Y) || Scalar.IsZero(scale.Z))
        {
            rotation = Quaternion<T>.Identity;
            return false;
        }

        if (Determinant() < T.Zero)
        {
            scale.X = -scale.X;
        }

        rotation = Quaternion.CreateFromRotationMatrix(new Matrix3x3<T>(Row1 / scale.X, Row2 / scale.Y, Row3 / scale.Z));
        return true;
    }

    /// <summary>Splits a rotation and one scale on every axis into the two; a mirror - a negative determinant - comes
    /// out as a negative scale. False, with no rotation, when the scale is within
    /// <see cref="Scalar.ZeroTolerance{T}"/> of zero.</summary>
    public readonly bool DecomposeUniformScale(out T scale, out Quaternion<T> rotation)
    {
        scale = Row1.Length();
        if (T.Abs(scale) < Scalar.ZeroTolerance<T>())
        {
            rotation = Quaternion<T>.Identity;
            return false;
        }

        if (Determinant() < T.Zero)
        {
            scale = -scale;
        }

        rotation = Quaternion.CreateFromRotationMatrix(this * (T.One / scale));
        return true;
    }

    /// <summary>Swaps two rows, each from 0 to 2.</summary>
    /// <exception cref="ArgumentOutOfRangeException">A row is not 0 to 2.</exception>
    public void ExchangeRows(int firstRow, int secondRow)
    {
        ArgumentOutOfRangeException.ThrowIfNegative(firstRow);
        ArgumentOutOfRangeException.ThrowIfGreaterThan(firstRow, 2);
        ArgumentOutOfRangeException.ThrowIfNegative(secondRow);
        ArgumentOutOfRangeException.ThrowIfGreaterThan(secondRow, 2);
        ref var rows = ref Row1;
        (Unsafe.Add(ref rows, firstRow), Unsafe.Add(ref rows, secondRow)) =
            (Unsafe.Add(ref rows, secondRow), Unsafe.Add(ref rows, firstRow));
    }

    /// <summary>Swaps two columns, each from 0 to 2.</summary>
    /// <exception cref="ArgumentOutOfRangeException">A column is not 0 to 2.</exception>
    public void ExchangeColumns(int firstColumn, int secondColumn)
    {
        ArgumentOutOfRangeException.ThrowIfNegative(firstColumn);
        ArgumentOutOfRangeException.ThrowIfGreaterThan(firstColumn, 2);
        ArgumentOutOfRangeException.ThrowIfNegative(secondColumn);
        ArgumentOutOfRangeException.ThrowIfGreaterThan(secondColumn, 2);
        for (var row = 0; row < 3; row++)
        {
            (this[row, firstColumn], this[row, secondColumn]) = (this[row, secondColumn], this[row, firstColumn]);
        }
    }

    /// <summary>The elements as an array, row by row.</summary>
    public readonly T[] ToArray() => [M11, M12, M13, M21, M22, M23, M31, M32, M33];

    /// <summary>The matrix in another number type, each element converted as a cast would.</summary>
    public readonly Matrix3x3<TOther> As<TOther>() where TOther : unmanaged, IFloatingPointIeee754<TOther> =>
        new(Row1.As<TOther>(), Row2.As<TOther>(), Row3.As<TOther>());

    /// <summary>The element-wise sum.</summary>
    public static Matrix3x3<T> Add(Matrix3x3<T> left, Matrix3x3<T> right) => left + right;

    /// <summary>The element-wise difference.</summary>
    public static Matrix3x3<T> Subtract(Matrix3x3<T> left, Matrix3x3<T> right) => left - right;

    /// <summary>Every element times <paramref name="right"/>.</summary>
    public static Matrix3x3<T> Multiply(Matrix3x3<T> left, T right) => left * right;

    /// <summary>The product: <paramref name="left"/> applied first, then <paramref name="right"/>.</summary>
    public static Matrix3x3<T> Multiply(Matrix3x3<T> left, Matrix3x3<T> right) => left * right;

    /// <summary>Every element divided by <paramref name="right"/>.</summary>
    public static Matrix3x3<T> Divide(Matrix3x3<T> left, T right) => left / right;

    /// <summary>The element-wise quotient.</summary>
    public static Matrix3x3<T> Divide(Matrix3x3<T> left, Matrix3x3<T> right) => left / right;

    /// <summary>Every element negated.</summary>
    public static Matrix3x3<T> Negate(Matrix3x3<T> value) => -value;

    /// <summary><paramref name="value"/> multiplied by itself <paramref name="exponent"/> times; identity for zero.</summary>
    /// <exception cref="ArgumentOutOfRangeException"><paramref name="exponent"/> is negative.</exception>
    public static Matrix3x3<T> Exponent(Matrix3x3<T> value, int exponent)
    {
        ArgumentOutOfRangeException.ThrowIfNegative(exponent);
        var result = Identity;
        if (exponent == 0)
        {
            return result;
        }

        if (exponent == 1)
        {
            return value;
        }

        while (true)
        {
            if ((exponent & 1) != 0)
            {
                result *= value;
            }

            exponent /= 2;
            if (exponent == 0)
            {
                return result;
            }

            value *= value;
        }
    }

    /// <summary>The element-wise blend of <paramref name="start"/> and <paramref name="end"/> at
    /// <paramref name="amount"/>: start at 0, end at 1.</summary>
    public static Matrix3x3<T> Lerp(Matrix3x3<T> start, Matrix3x3<T> end, T amount) =>
        new(Vector3<T>.Lerp(start.Row1, end.Row1, amount), Vector3<T>.Lerp(start.Row2, end.Row2, amount),
            Vector3<T>.Lerp(start.Row3, end.Row3, amount));

    /// <summary>The element-wise blend of <paramref name="start"/> and <paramref name="end"/> at
    /// <paramref name="amount"/> eased in and out by <see cref="Scalar.SmoothStep{T}"/>.</summary>
    public static Matrix3x3<T> SmoothStep(Matrix3x3<T> start, Matrix3x3<T> end, T amount) =>
        Lerp(start, end, Scalar.SmoothStep(amount));

    /// <summary>Rows and columns swapped.</summary>
    public static Matrix3x3<T> Transpose(Matrix3x3<T> value) => new(value.Column1, value.Column2, value.Column3);

    /// <summary>The inverse, or <see cref="Zero"/> when the determinant is zero; a nearly singular matrix gives large
    /// elements.</summary>
    public static Matrix3x3<T> Invert(Matrix3x3<T> value)
    {
        Span<int> exponents = stackalloc int[3];
        PowerOfTwoScaling.ScaleRowsNearOne(Elements(ref value), 3, 3, exponents);
        var c11 = value.M22 * value.M33 - value.M23 * value.M32;
        var c12 = value.M23 * value.M31 - value.M21 * value.M33;
        var c13 = value.M21 * value.M32 - value.M22 * value.M31;
        var determinant = value.M11 * c11 + value.M12 * c12 + value.M13 * c13;
        if (determinant == T.Zero)
        {
            return Zero;
        }

        var inverse = T.One / determinant;
        var result = new Matrix3x3<T>(c11 * inverse, (value.M13 * value.M32 - value.M12 * value.M33) * inverse,
            (value.M12 * value.M23 - value.M13 * value.M22) * inverse,
            c12 * inverse, (value.M11 * value.M33 - value.M13 * value.M31) * inverse,
            (value.M13 * value.M21 - value.M11 * value.M23) * inverse,
            c13 * inverse, (value.M12 * value.M31 - value.M11 * value.M32) * inverse,
            (value.M11 * value.M22 - value.M12 * value.M21) * inverse);
        PowerOfTwoScaling.UnscaleInverseColumns(Elements(ref result), 3, 3, exponents);
        return result;
    }

    /// <summary>The rows made perpendicular to one another (modified Gram-Schmidt), the first kept.</summary>
    public static Matrix3x3<T> Orthogonalize(Matrix3x3<T> value)
    {
        Span<Vector3<T>> rows = [value.Row1, value.Row2, value.Row3];
        Vector3<T>.Orthogonalize(rows, rows);
        return new Matrix3x3<T>(rows[0], rows[1], rows[2]);
    }

    /// <summary>The rows made perpendicular to one another and of length one (modified Gram-Schmidt).</summary>
    public static Matrix3x3<T> Orthonormalize(Matrix3x3<T> value)
    {
        Span<Vector3<T>> rows = [value.Row1, value.Row2, value.Row3];
        Vector3<T>.Orthonormalize(rows, rows);
        return new Matrix3x3<T>(rows[0], rows[1], rows[2]);
    }

    /// <summary>The matrix brought to upper triangular form by Gaussian elimination.</summary>
    public static Matrix3x3<T> UpperTriangularForm(Matrix3x3<T> value)
    {
        Eliminate(ref value, false);
        return value;
    }

    /// <summary>The matrix brought to lower triangular form by Gaussian elimination.</summary>
    public static Matrix3x3<T> LowerTriangularForm(Matrix3x3<T> value)
    {
        var result = Transpose(value);
        Eliminate(ref result, false);
        return Transpose(result);
    }

    /// <summary>The matrix brought to row echelon form by Gaussian elimination: each leading element one.</summary>
    public static Matrix3x3<T> RowEchelonForm(Matrix3x3<T> value)
    {
        Eliminate(ref value, true);
        return value;
    }

    /// <summary>The element-wise sum.</summary>
    public static Matrix3x3<T> operator +(Matrix3x3<T> left, Matrix3x3<T> right) =>
        new(left.Row1 + right.Row1, left.Row2 + right.Row2, left.Row3 + right.Row3);

    /// <summary>The matrix itself.</summary>
    public static Matrix3x3<T> operator +(Matrix3x3<T> value) => value;

    /// <summary>The element-wise difference.</summary>
    public static Matrix3x3<T> operator -(Matrix3x3<T> left, Matrix3x3<T> right) =>
        new(left.Row1 - right.Row1, left.Row2 - right.Row2, left.Row3 - right.Row3);

    /// <summary>Every element negated.</summary>
    public static Matrix3x3<T> operator -(Matrix3x3<T> value) => new(-value.Row1, -value.Row2, -value.Row3);

    /// <summary>Every element times <paramref name="left"/>.</summary>
    public static Matrix3x3<T> operator *(T left, Matrix3x3<T> right) => right * left;

    /// <summary>Every element times <paramref name="right"/>.</summary>
    public static Matrix3x3<T> operator *(Matrix3x3<T> left, T right) => new(left.Row1 * right, left.Row2 * right, left.Row3 * right);

    /// <summary>The product: <paramref name="left"/> applied first, then <paramref name="right"/>.</summary>
    public static Matrix3x3<T> operator *(Matrix3x3<T> left, Matrix3x3<T> right) =>
        new(Vector3<T>.Transform(left.Row1, right), Vector3<T>.Transform(left.Row2, right), Vector3<T>.Transform(left.Row3, right));

    /// <summary>Every element divided by <paramref name="right"/>.</summary>
    public static Matrix3x3<T> operator /(Matrix3x3<T> left, T right) => left * (T.One / right);

    /// <summary>The element-wise quotient.</summary>
    public static Matrix3x3<T> operator /(Matrix3x3<T> left, Matrix3x3<T> right) =>
        new(left.Row1 / right.Row1, left.Row2 / right.Row2, left.Row3 / right.Row3);

    /// <summary>Whether the elements are equal; NaN equals nothing.</summary>
    public static bool operator ==(Matrix3x3<T> left, Matrix3x3<T> right) =>
        left.Row1 == right.Row1 && left.Row2 == right.Row2 && left.Row3 == right.Row3;

    /// <summary>Whether any element differs; NaN differs from everything.</summary>
    public static bool operator !=(Matrix3x3<T> left, Matrix3x3<T> right) => !(left == right);

    /// <summary>The matrix in the upper left of a 4x4 one, with no translation.</summary>
    public static explicit operator Matrix4x4<T>(Matrix3x3<T> value) =>
        new(new Vector4<T>(value.Row1, T.Zero), new Vector4<T>(value.Row2, T.Zero), new Vector4<T>(value.Row3, T.Zero),
            Vector4<T>.UnitW);

    /// <summary>The upper left 3x3 of a 4x4 matrix: its rotation and scale.</summary>
    public static explicit operator Matrix3x3<T>(Matrix4x4<T> value) =>
        new((Vector3<T>)value.Row1, (Vector3<T>)value.Row2, (Vector3<T>)value.Row3);

    /// <summary>Whether each element of the two is within <see cref="Scalar.NearEqual{T}"/>.</summary>
    public static bool NearEqual(Matrix3x3<T> left, Matrix3x3<T> right) =>
        Vector3<T>.NearEqual(left.Row1, right.Row1) && Vector3<T>.NearEqual(left.Row2, right.Row2)
                                                    && Vector3<T>.NearEqual(left.Row3, right.Row3);

    /// <summary>Whether the elements are equal, a NaN to a NaN too - as a dictionary key needs.</summary>
    public readonly bool Equals(Matrix3x3<T> other) => Row1.Equals(other.Row1) && Row2.Equals(other.Row2) && Row3.Equals(other.Row3);

    /// <inheritdoc />
    public override readonly bool Equals(object obj) => obj is Matrix3x3<T> other && Equals(other);

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
        string.Format(formatProvider, "[M11:{0} M12:{1} M13:{2}] [M21:{3} M22:{4} M23:{5}] [M31:{6} M32:{7} M33:{8}]",
            M11.ToString(format, formatProvider), M12.ToString(format, formatProvider), M13.ToString(format, formatProvider),
            M21.ToString(format, formatProvider), M22.ToString(format, formatProvider), M23.ToString(format, formatProvider),
            M31.ToString(format, formatProvider), M32.ToString(format, formatProvider), M33.ToString(format, formatProvider));

    private static void Eliminate(ref Matrix3x3<T> result, bool unitLeads) =>
        GaussianElimination.Eliminate(Elements(ref result), 3, 3, unitLeads, false);

    private static Span<T> Elements(ref Matrix3x3<T> value) =>
        MemoryMarshal.CreateSpan(ref Unsafe.As<Vector3<T>, T>(ref value.Row1), 9);

    private static void CheckIndex(int index)
    {
        if ((uint)index > 8)
        {
            throw new ArgumentOutOfRangeException(nameof(index), "Indices for Matrix3x3 run from 0 to 8, inclusive.");
        }
    }

    private static void CheckRowAndColumn(int row, int column)
    {
        if ((uint)row > 2)
        {
            throw new ArgumentOutOfRangeException(nameof(row), "Rows and columns for matrices run from 0 to 2, inclusive.");
        }

        if ((uint)column > 2)
        {
            throw new ArgumentOutOfRangeException(nameof(column), "Rows and columns for matrices run from 0 to 2, inclusive.");
        }
    }
}
