using System;
using System.Globalization;
using System.Numerics;
using System.Runtime.CompilerServices;
using System.Runtime.InteropServices;

namespace Adamantium.Mathematics;

/// <summary>
/// A 4x4 matrix of fractional numbers - <c>Matrix4x4&lt;float&gt;</c> or <c>Matrix4x4&lt;double&gt;</c> - stored as four
/// rows, for row vectors: a point is transformed as <c>point * matrix</c>, the translation is the fourth row, and
/// <c>a * b</c> applies <c>a</c> first. Made with <see cref="Matrix4x4"/>'s factories. Equality is exact;
/// <see cref="NearEqual(Matrix4x4{T}, Matrix4x4{T})"/> compares within a tolerance.
/// </summary>
[StructLayout(LayoutKind.Sequential)]
public struct Matrix4x4<T> : IEquatable<Matrix4x4<T>>, IFormattable where T : unmanaged, IFloatingPointIeee754<T>
{
    /// <summary>The first row: M11, M12, M13, M14.</summary>
    public Vector4<T> Row1;

    /// <summary>The second row: M21, M22, M23, M24.</summary>
    public Vector4<T> Row2;

    /// <summary>The third row: M31, M32, M33, M34.</summary>
    public Vector4<T> Row3;

    /// <summary>The fourth row: M41, M42, M43, M44.</summary>
    public Vector4<T> Row4;

    /// <summary>A matrix of the four rows.</summary>
    public Matrix4x4(Vector4<T> row1, Vector4<T> row2, Vector4<T> row3, Vector4<T> row4)
    {
        Row1 = row1;
        Row2 = row2;
        Row3 = row3;
        Row4 = row4;
    }

    /// <summary>A matrix of <paramref name="value"/> in every element.</summary>
    public Matrix4x4(T value) : this(new Vector4<T>(value), new Vector4<T>(value), new Vector4<T>(value), new Vector4<T>(value))
    {
    }

    /// <summary>A matrix of the sixteen elements, row by row.</summary>
    public Matrix4x4(T m11, T m12, T m13, T m14, T m21, T m22, T m23, T m24, T m31, T m32, T m33, T m34, T m41, T m42,
        T m43, T m44) : this(new Vector4<T>(m11, m12, m13, m14), new Vector4<T>(m21, m22, m23, m24),
        new Vector4<T>(m31, m32, m33, m34), new Vector4<T>(m41, m42, m43, m44))
    {
    }

    /// <summary>A matrix of the first sixteen numbers in <paramref name="values"/>, row by row.</summary>
    /// <exception cref="ArgumentOutOfRangeException"><paramref name="values"/> holds fewer than sixteen numbers.</exception>
    public Matrix4x4(ReadOnlySpan<T> values)
    {
        if (values.Length < 16)
        {
            throw new ArgumentOutOfRangeException(nameof(values), "There must be sixteen input values for Matrix4x4.");
        }

        Row1 = new Vector4<T>(values[..4]);
        Row2 = new Vector4<T>(values[4..8]);
        Row3 = new Vector4<T>(values[8..12]);
        Row4 = new Vector4<T>(values[12..16]);
    }

    /// <summary>The size of the matrix in bytes.</summary>
    public static int SizeInBytes => Unsafe.SizeOf<Matrix4x4<T>>();

    /// <summary>All zero.</summary>
    public static Matrix4x4<T> Zero => default;

    /// <summary>Ones on the diagonal, zero elsewhere: no transform.</summary>
    public static Matrix4x4<T> Identity => new(Vector4<T>.UnitX, Vector4<T>.UnitY, Vector4<T>.UnitZ, Vector4<T>.UnitW);

    /// <summary>Row 1, column 1.</summary>
    public T M11 { readonly get => Row1.X; set => Row1.X = value; }

    /// <summary>Row 1, column 2.</summary>
    public T M12 { readonly get => Row1.Y; set => Row1.Y = value; }

    /// <summary>Row 1, column 3.</summary>
    public T M13 { readonly get => Row1.Z; set => Row1.Z = value; }

    /// <summary>Row 1, column 4.</summary>
    public T M14 { readonly get => Row1.W; set => Row1.W = value; }

    /// <summary>Row 2, column 1.</summary>
    public T M21 { readonly get => Row2.X; set => Row2.X = value; }

    /// <summary>Row 2, column 2.</summary>
    public T M22 { readonly get => Row2.Y; set => Row2.Y = value; }

    /// <summary>Row 2, column 3.</summary>
    public T M23 { readonly get => Row2.Z; set => Row2.Z = value; }

    /// <summary>Row 2, column 4.</summary>
    public T M24 { readonly get => Row2.W; set => Row2.W = value; }

    /// <summary>Row 3, column 1.</summary>
    public T M31 { readonly get => Row3.X; set => Row3.X = value; }

    /// <summary>Row 3, column 2.</summary>
    public T M32 { readonly get => Row3.Y; set => Row3.Y = value; }

    /// <summary>Row 3, column 3.</summary>
    public T M33 { readonly get => Row3.Z; set => Row3.Z = value; }

    /// <summary>Row 3, column 4.</summary>
    public T M34 { readonly get => Row3.W; set => Row3.W = value; }

    /// <summary>Row 4, column 1: the X translation.</summary>
    public T M41 { readonly get => Row4.X; set => Row4.X = value; }

    /// <summary>Row 4, column 2: the Y translation.</summary>
    public T M42 { readonly get => Row4.Y; set => Row4.Y = value; }

    /// <summary>Row 4, column 3: the Z translation.</summary>
    public T M43 { readonly get => Row4.Z; set => Row4.Z = value; }

    /// <summary>Row 4, column 4.</summary>
    public T M44 { readonly get => Row4.W; set => Row4.W = value; }

    /// <summary>The up direction: the second row's X, Y, Z.</summary>
    public Vector3<T> Up
    {
        readonly get => (Vector3<T>)Row2;
        set => Row2 = new Vector4<T>(value, Row2.W);
    }

    /// <summary>The down direction: the second row's X, Y, Z negated.</summary>
    public Vector3<T> Down
    {
        readonly get => -(Vector3<T>)Row2;
        set => Row2 = new Vector4<T>(-value, Row2.W);
    }

    /// <summary>The right direction: the first row's X, Y, Z.</summary>
    public Vector3<T> Right
    {
        readonly get => (Vector3<T>)Row1;
        set => Row1 = new Vector4<T>(value, Row1.W);
    }

    /// <summary>The left direction: the first row's X, Y, Z negated.</summary>
    public Vector3<T> Left
    {
        readonly get => -(Vector3<T>)Row1;
        set => Row1 = new Vector4<T>(-value, Row1.W);
    }

    /// <summary>The forward direction of a right-handed system: the third row's X, Y, Z negated.</summary>
    public Vector3<T> Forward
    {
        readonly get => -(Vector3<T>)Row3;
        set => Row3 = new Vector4<T>(-value, Row3.W);
    }

    /// <summary>The backward direction of a right-handed system: the third row's X, Y, Z.</summary>
    public Vector3<T> Backward
    {
        readonly get => (Vector3<T>)Row3;
        set => Row3 = new Vector4<T>(value, Row3.W);
    }

    /// <summary>The first column: M11, M21, M31, M41.</summary>
    public Vector4<T> Column1
    {
        readonly get => new(Row1.X, Row2.X, Row3.X, Row4.X);
        set => (Row1.X, Row2.X, Row3.X, Row4.X) = (value.X, value.Y, value.Z, value.W);
    }

    /// <summary>The second column: M12, M22, M32, M42.</summary>
    public Vector4<T> Column2
    {
        readonly get => new(Row1.Y, Row2.Y, Row3.Y, Row4.Y);
        set => (Row1.Y, Row2.Y, Row3.Y, Row4.Y) = (value.X, value.Y, value.Z, value.W);
    }

    /// <summary>The third column: M13, M23, M33, M43.</summary>
    public Vector4<T> Column3
    {
        readonly get => new(Row1.Z, Row2.Z, Row3.Z, Row4.Z);
        set => (Row1.Z, Row2.Z, Row3.Z, Row4.Z) = (value.X, value.Y, value.Z, value.W);
    }

    /// <summary>The fourth column: M14, M24, M34, M44.</summary>
    public Vector4<T> Column4
    {
        readonly get => new(Row1.W, Row2.W, Row3.W, Row4.W);
        set => (Row1.W, Row2.W, Row3.W, Row4.W) = (value.X, value.Y, value.Z, value.W);
    }

    /// <summary>The translation: M41, M42, M43.</summary>
    public Vector3<T> TranslationVector
    {
        readonly get => (Vector3<T>)Row4;
        set => Row4 = new Vector4<T>(value, Row4.W);
    }

    /// <summary>The diagonal's first three: M11, M22, M33 - the scale of a matrix without rotation.</summary>
    public Vector3<T> ScaleVector
    {
        readonly get => new(Row1.X, Row2.Y, Row3.Z);
        set => (Row1.X, Row2.Y, Row3.Z) = (value.X, value.Y, value.Z);
    }

    /// <summary>Whether this is exactly <see cref="Identity"/>.</summary>
    public readonly bool IsIdentity => this == Identity;

    /// <summary>The element at <paramref name="index"/>, row by row: 0 is M11, 4 is M21, 15 is M44.</summary>
    /// <exception cref="ArgumentOutOfRangeException"><paramref name="index"/> is not 0 to 15.</exception>
    public T this[int index]
    {
        readonly get
        {
            CheckIndex(index);
            return Unsafe.Add(ref Unsafe.As<Vector4<T>, T>(ref Unsafe.AsRef(in Row1)), index);
        }
        set
        {
            CheckIndex(index);
            Unsafe.Add(ref Unsafe.As<Vector4<T>, T>(ref Row1), index) = value;
        }
    }

    /// <summary>The element in <paramref name="row"/> and <paramref name="column"/>, both from 0 to 3.</summary>
    /// <exception cref="ArgumentOutOfRangeException"><paramref name="row"/> or <paramref name="column"/> is not 0 to 3.</exception>
    public T this[int row, int column]
    {
        readonly get
        {
            CheckRowAndColumn(row, column);
            return this[row * 4 + column];
        }
        set
        {
            CheckRowAndColumn(row, column);
            this[row * 4 + column] = value;
        }
    }

    /// <summary>The determinant.</summary>
    public readonly T Determinant()
    {
        var temp1 = M33 * M44 - M34 * M43;
        var temp2 = M32 * M44 - M34 * M42;
        var temp3 = M32 * M43 - M33 * M42;
        var temp4 = M31 * M44 - M34 * M41;
        var temp5 = M31 * M43 - M33 * M41;
        var temp6 = M31 * M42 - M32 * M41;
        return M11 * (M22 * temp1 - M23 * temp2 + M24 * temp3) - M12 * (M21 * temp1 - M23 * temp4 + M24 * temp5)
               + M13 * (M21 * temp2 - M22 * temp4 + M24 * temp6) - M14 * (M21 * temp3 - M22 * temp5 + M23 * temp6);
    }

    /// <summary>Makes this its inverse, or <see cref="Zero"/> when it is singular, as <see cref="Invert(Matrix4x4{T})"/>
    /// finds it.</summary>
    public void Invert() => this = Invert(this);

    /// <summary>Swaps rows and columns.</summary>
    public void Transpose() => this = Transpose(this);

    /// <summary>Makes the rows perpendicular to one another (modified Gram-Schmidt), keeping the first.</summary>
    public void Orthogonalize() => this = Orthogonalize(this);

    /// <summary>Makes the rows perpendicular to one another and of length one (modified Gram-Schmidt).</summary>
    public void Orthonormalize() => this = Orthonormalize(this);

    /// <summary>Splits the matrix into <paramref name="q"/>, orthonormal, times <paramref name="r"/>, upper
    /// triangular.</summary>
    public readonly void DecomposeQR(out Matrix4x4<T> q, out Matrix4x4<T> r)
    {
        q = Transpose(Orthonormalize(Transpose(this)));
        r = default;
        r.M11 = Vector4<T>.Dot(q.Column1, Column1);
        r.M12 = Vector4<T>.Dot(q.Column1, Column2);
        r.M13 = Vector4<T>.Dot(q.Column1, Column3);
        r.M14 = Vector4<T>.Dot(q.Column1, Column4);
        r.M22 = Vector4<T>.Dot(q.Column2, Column2);
        r.M23 = Vector4<T>.Dot(q.Column2, Column3);
        r.M24 = Vector4<T>.Dot(q.Column2, Column4);
        r.M33 = Vector4<T>.Dot(q.Column3, Column3);
        r.M34 = Vector4<T>.Dot(q.Column3, Column4);
        r.M44 = Vector4<T>.Dot(q.Column4, Column4);
    }

    /// <summary>Splits the matrix into <paramref name="l"/>, lower triangular, times <paramref name="q"/>,
    /// orthonormal.</summary>
    public readonly void DecomposeLQ(out Matrix4x4<T> l, out Matrix4x4<T> q)
    {
        q = Orthonormalize(this);
        l = default;
        l.M11 = Vector4<T>.Dot(q.Row1, Row1);
        l.M21 = Vector4<T>.Dot(q.Row1, Row2);
        l.M22 = Vector4<T>.Dot(q.Row2, Row2);
        l.M31 = Vector4<T>.Dot(q.Row1, Row3);
        l.M32 = Vector4<T>.Dot(q.Row2, Row3);
        l.M33 = Vector4<T>.Dot(q.Row3, Row3);
        l.M41 = Vector4<T>.Dot(q.Row1, Row4);
        l.M42 = Vector4<T>.Dot(q.Row2, Row4);
        l.M43 = Vector4<T>.Dot(q.Row3, Row4);
        l.M44 = Vector4<T>.Dot(q.Row4, Row4);
    }

    /// <summary>Splits a transform into scale, rotation and translation; a mirror - a negative determinant of the upper
    /// 3x3 - comes out as a negative X scale. False, with no rotation, when a scale is within
    /// <see cref="MathHelper.Scalar.ZeroTolerance{T}"/> of zero.</summary>
    public readonly bool Decompose(out Vector3<T> scale, out Quaternion<T> rotation, out Vector3<T> translation)
    {
        translation = TranslationVector;
        return ((Matrix3x3<T>)this).Decompose(out scale, out rotation);
    }

    /// <summary>Splits a transform of one scale on every axis into that scale, rotation and translation; a mirror comes
    /// out as a negative scale. False, with no rotation, when the scale is within
    /// <see cref="MathHelper.Scalar.ZeroTolerance{T}"/> of zero.</summary>
    public readonly bool DecomposeUniformScale(out T scale, out Quaternion<T> rotation, out Vector3<T> translation)
    {
        translation = TranslationVector;
        return ((Matrix3x3<T>)this).DecomposeUniformScale(out scale, out rotation);
    }

    /// <summary>Swaps two rows, each from 0 to 3.</summary>
    /// <exception cref="ArgumentOutOfRangeException">A row is not 0 to 3.</exception>
    public void ExchangeRows(int firstRow, int secondRow)
    {
        ArgumentOutOfRangeException.ThrowIfNegative(firstRow);
        ArgumentOutOfRangeException.ThrowIfGreaterThan(firstRow, 3);
        ArgumentOutOfRangeException.ThrowIfNegative(secondRow);
        ArgumentOutOfRangeException.ThrowIfGreaterThan(secondRow, 3);
        ref var rows = ref Row1;
        (Unsafe.Add(ref rows, firstRow), Unsafe.Add(ref rows, secondRow)) =
            (Unsafe.Add(ref rows, secondRow), Unsafe.Add(ref rows, firstRow));
    }

    /// <summary>Swaps two columns, each from 0 to 3.</summary>
    /// <exception cref="ArgumentOutOfRangeException">A column is not 0 to 3.</exception>
    public void ExchangeColumns(int firstColumn, int secondColumn)
    {
        ArgumentOutOfRangeException.ThrowIfNegative(firstColumn);
        ArgumentOutOfRangeException.ThrowIfGreaterThan(firstColumn, 3);
        ArgumentOutOfRangeException.ThrowIfNegative(secondColumn);
        ArgumentOutOfRangeException.ThrowIfGreaterThan(secondColumn, 3);
        for (var row = 0; row < 4; row++)
        {
            (this[row, firstColumn], this[row, secondColumn]) = (this[row, secondColumn], this[row, firstColumn]);
        }
    }

    /// <summary>The elements as an array, row by row.</summary>
    public readonly T[] ToArray() =>
        [M11, M12, M13, M14, M21, M22, M23, M24, M31, M32, M33, M34, M41, M42, M43, M44];

    /// <summary>The matrix in another number type, each element converted as a cast would.</summary>
    public readonly Matrix4x4<TOther> As<TOther>() where TOther : unmanaged, IFloatingPointIeee754<TOther> =>
        new(Row1.As<TOther>(), Row2.As<TOther>(), Row3.As<TOther>(), Row4.As<TOther>());

    /// <summary>The element-wise sum.</summary>
    public static Matrix4x4<T> Add(Matrix4x4<T> left, Matrix4x4<T> right) => left + right;

    /// <summary>The element-wise difference.</summary>
    public static Matrix4x4<T> Subtract(Matrix4x4<T> left, Matrix4x4<T> right) => left - right;

    /// <summary>Every element times <paramref name="right"/>.</summary>
    public static Matrix4x4<T> Multiply(Matrix4x4<T> left, T right) => left * right;

    /// <summary>The product: <paramref name="left"/> applied first, then <paramref name="right"/>.</summary>
    public static Matrix4x4<T> Multiply(Matrix4x4<T> left, Matrix4x4<T> right) => left * right;

    /// <summary>Every element divided by <paramref name="right"/>.</summary>
    public static Matrix4x4<T> Divide(Matrix4x4<T> left, T right) => left / right;

    /// <summary>The element-wise quotient.</summary>
    public static Matrix4x4<T> Divide(Matrix4x4<T> left, Matrix4x4<T> right) => left / right;

    /// <summary>Every element negated.</summary>
    public static Matrix4x4<T> Negate(Matrix4x4<T> value) => -value;

    /// <summary><paramref name="value"/> multiplied by itself <paramref name="exponent"/> times; identity for zero.</summary>
    /// <exception cref="ArgumentOutOfRangeException"><paramref name="exponent"/> is negative.</exception>
    public static Matrix4x4<T> Exponent(Matrix4x4<T> value, int exponent)
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
    public static Matrix4x4<T> Lerp(Matrix4x4<T> start, Matrix4x4<T> end, T amount) =>
        new(Vector4<T>.Lerp(start.Row1, end.Row1, amount), Vector4<T>.Lerp(start.Row2, end.Row2, amount),
            Vector4<T>.Lerp(start.Row3, end.Row3, amount), Vector4<T>.Lerp(start.Row4, end.Row4, amount));

    /// <summary>The element-wise blend of <paramref name="start"/> and <paramref name="end"/> at
    /// <paramref name="amount"/> eased in and out by <see cref="MathHelper.Scalar.SmoothStep{T}"/>.</summary>
    public static Matrix4x4<T> SmoothStep(Matrix4x4<T> start, Matrix4x4<T> end, T amount) =>
        Lerp(start, end, MathHelper.Scalar.SmoothStep(amount));

    /// <summary>Rows and columns swapped.</summary>
    public static Matrix4x4<T> Transpose(Matrix4x4<T> value) =>
        new(value.Column1, value.Column2, value.Column3, value.Column4);

    /// <summary>The inverse, or <see cref="Zero"/> when the matrix is singular; a nearly singular matrix gives large
    /// elements. Where the cofactor formula's determinant leaves the normal range - elements far from one or far apart -
    /// the rows and columns are first scaled near one by powers of two, which is exact, and the scale taken back out.</summary>
    public static Matrix4x4<T> Invert(Matrix4x4<T> value)
    {
        var result = InvertByCofactors(value, out var determinant);
        var sum = result.Row1 + result.Row2 + result.Row3 + result.Row4;
        return T.IsNormal(determinant) && T.IsFinite(sum.X + sum.Y + sum.Z + sum.W) ? result : InvertScaled(value);
    }

    /// <summary>The rows made perpendicular to one another (modified Gram-Schmidt), the first kept.</summary>
    public static Matrix4x4<T> Orthogonalize(Matrix4x4<T> value)
    {
        Span<Vector4<T>> rows = [value.Row1, value.Row2, value.Row3, value.Row4];
        Vector4<T>.Orthogonalize(rows, rows);
        return new Matrix4x4<T>(rows[0], rows[1], rows[2], rows[3]);
    }

    /// <summary>The rows made perpendicular to one another and of length one (modified Gram-Schmidt).</summary>
    public static Matrix4x4<T> Orthonormalize(Matrix4x4<T> value)
    {
        Span<Vector4<T>> rows = [value.Row1, value.Row2, value.Row3, value.Row4];
        Vector4<T>.Orthonormalize(rows, rows);
        return new Matrix4x4<T>(rows[0], rows[1], rows[2], rows[3]);
    }

    /// <summary>The matrix brought to upper triangular form by Gaussian elimination.</summary>
    public static Matrix4x4<T> UpperTriangularForm(Matrix4x4<T> value)
    {
        Eliminate(ref value, false);
        return value;
    }

    /// <summary>The matrix brought to lower triangular form by Gaussian elimination.</summary>
    public static Matrix4x4<T> LowerTriangularForm(Matrix4x4<T> value)
    {
        var result = Transpose(value);
        Eliminate(ref result, false);
        return Transpose(result);
    }

    /// <summary>The matrix brought to row echelon form by Gaussian elimination: each leading element one.</summary>
    public static Matrix4x4<T> RowEchelonForm(Matrix4x4<T> value)
    {
        Eliminate(ref value, true);
        return value;
    }

    /// <summary>The matrix, with <paramref name="augment"/> as a fifth column, brought to reduced row echelon form by
    /// Gauss-Jordan elimination with the largest pivots; for an invertible matrix the augment becomes the solution of
    /// <c>matrix * x = augment</c> taken by columns.</summary>
    public static (Matrix4x4<T> Matrix, Vector4<T> Augment) ReducedRowEchelonForm(Matrix4x4<T> value, Vector4<T> augment)
    {
        Span<T> rows = stackalloc T[20];
        for (var r = 0; r < 4; r++)
        {
            for (var c = 0; c < 4; c++)
            {
                rows[r * 5 + c] = value[r, c];
            }

            rows[r * 5 + 4] = augment[r];
        }

        GaussianElimination.Eliminate(rows, 4, 5, true, true);
        return (new Matrix4x4<T>(rows[0], rows[1], rows[2], rows[3], rows[5], rows[6], rows[7], rows[8], rows[10], rows[11],
            rows[12], rows[13], rows[15], rows[16], rows[17], rows[18]), new Vector4<T>(rows[4], rows[9], rows[14], rows[19]));
    }

    /// <summary>The element-wise sum.</summary>
    public static Matrix4x4<T> operator +(Matrix4x4<T> left, Matrix4x4<T> right) =>
        new(left.Row1 + right.Row1, left.Row2 + right.Row2, left.Row3 + right.Row3, left.Row4 + right.Row4);

    /// <summary>The matrix itself.</summary>
    public static Matrix4x4<T> operator +(Matrix4x4<T> value) => value;

    /// <summary>The element-wise difference.</summary>
    public static Matrix4x4<T> operator -(Matrix4x4<T> left, Matrix4x4<T> right) =>
        new(left.Row1 - right.Row1, left.Row2 - right.Row2, left.Row3 - right.Row3, left.Row4 - right.Row4);

    /// <summary>Every element negated.</summary>
    public static Matrix4x4<T> operator -(Matrix4x4<T> value) => new(-value.Row1, -value.Row2, -value.Row3, -value.Row4);

    /// <summary>Every element times <paramref name="left"/>.</summary>
    public static Matrix4x4<T> operator *(T left, Matrix4x4<T> right) => right * left;

    /// <summary>Every element times <paramref name="right"/>.</summary>
    public static Matrix4x4<T> operator *(Matrix4x4<T> left, T right) =>
        new(left.Row1 * right, left.Row2 * right, left.Row3 * right, left.Row4 * right);

    /// <summary>The product: <paramref name="left"/> applied first, then <paramref name="right"/>.</summary>
    public static Matrix4x4<T> operator *(Matrix4x4<T> left, Matrix4x4<T> right) => Matrix4x4Simd.Multiply(left, right);

    /// <summary>Every element divided by <paramref name="right"/>.</summary>
    public static Matrix4x4<T> operator /(Matrix4x4<T> left, T right) => left * (T.One / right);

    /// <summary>The element-wise quotient.</summary>
    public static Matrix4x4<T> operator /(Matrix4x4<T> left, Matrix4x4<T> right) =>
        new(left.Row1 / right.Row1, left.Row2 / right.Row2, left.Row3 / right.Row3, left.Row4 / right.Row4);

    /// <summary>Whether the elements are equal; NaN equals nothing.</summary>
    public static bool operator ==(Matrix4x4<T> left, Matrix4x4<T> right) =>
        left.Row1 == right.Row1 && left.Row2 == right.Row2 && left.Row3 == right.Row3 && left.Row4 == right.Row4;

    /// <summary>Whether any element differs; NaN differs from everything.</summary>
    public static bool operator !=(Matrix4x4<T> left, Matrix4x4<T> right) => !(left == right);

    /// <summary>Whether each element of the two is within <see cref="MathHelper.Scalar.NearEqual{T}"/>.</summary>
    public static bool NearEqual(Matrix4x4<T> left, Matrix4x4<T> right) =>
        Vector4<T>.NearEqual(left.Row1, right.Row1) && Vector4<T>.NearEqual(left.Row2, right.Row2)
                                                    && Vector4<T>.NearEqual(left.Row3, right.Row3)
                                                    && Vector4<T>.NearEqual(left.Row4, right.Row4);

    /// <summary>Whether the elements are equal, a NaN to a NaN too - as a dictionary key needs.</summary>
    public readonly bool Equals(Matrix4x4<T> other) =>
        Row1.Equals(other.Row1) && Row2.Equals(other.Row2) && Row3.Equals(other.Row3) && Row4.Equals(other.Row4);

    /// <inheritdoc />
    public override readonly bool Equals(object obj) => obj is Matrix4x4<T> other && Equals(other);

    /// <inheritdoc />
    public override readonly int GetHashCode() => HashCode.Combine(Row1, Row2, Row3, Row4);

    /// <inheritdoc />
    public override readonly string ToString() => ToString(null, CultureInfo.CurrentCulture);

    /// <summary>The matrix with each element written in <paramref name="format"/>.</summary>
    public readonly string ToString(string format) => ToString(format, CultureInfo.CurrentCulture);

    /// <summary>The matrix written for <paramref name="formatProvider"/>.</summary>
    public readonly string ToString(IFormatProvider formatProvider) => ToString(null, formatProvider);

    /// <inheritdoc />
    public readonly string ToString(string format, IFormatProvider formatProvider) =>
        string.Format(formatProvider,
            "[M11:{0} M12:{1} M13:{2} M14:{3}] [M21:{4} M22:{5} M23:{6} M24:{7}] [M31:{8} M32:{9} M33:{10} M34:{11}] [M41:{12} M42:{13} M43:{14} M44:{15}]",
            M11.ToString(format, formatProvider), M12.ToString(format, formatProvider), M13.ToString(format, formatProvider),
            M14.ToString(format, formatProvider), M21.ToString(format, formatProvider), M22.ToString(format, formatProvider),
            M23.ToString(format, formatProvider), M24.ToString(format, formatProvider), M31.ToString(format, formatProvider),
            M32.ToString(format, formatProvider), M33.ToString(format, formatProvider), M34.ToString(format, formatProvider),
            M41.ToString(format, formatProvider), M42.ToString(format, formatProvider), M43.ToString(format, formatProvider),
            M44.ToString(format, formatProvider));

    [MethodImpl(MethodImplOptions.NoInlining)]
    private static Matrix4x4<T> InvertScaled(Matrix4x4<T> value)
    {
        Span<int> rowExponents = stackalloc int[4];
        Span<int> columnExponents = stackalloc int[4];
        PowerOfTwoScaling.Equilibrate(Elements(ref value), 4, rowExponents, columnExponents);
        var result = InvertByCofactors(value, out _);
        PowerOfTwoScaling.UnscaleInverse(Elements(ref result), 4, rowExponents, columnExponents);
        return result;
    }

    [MethodImpl(MethodImplOptions.AggressiveInlining)]
    private static Matrix4x4<T> InvertByCofactors(Matrix4x4<T> value, out T determinant)
    {
        var b0 = value.M31 * value.M42 - value.M32 * value.M41;
        var b1 = value.M31 * value.M43 - value.M33 * value.M41;
        var b2 = value.M34 * value.M41 - value.M31 * value.M44;
        var b3 = value.M32 * value.M43 - value.M33 * value.M42;
        var b4 = value.M34 * value.M42 - value.M32 * value.M44;
        var b5 = value.M33 * value.M44 - value.M34 * value.M43;
        var d11 = value.M22 * b5 + value.M23 * b4 + value.M24 * b3;
        var d12 = value.M21 * b5 + value.M23 * b2 + value.M24 * b1;
        var d13 = value.M21 * -b4 + value.M22 * b2 + value.M24 * b0;
        var d14 = value.M21 * b3 + value.M22 * -b1 + value.M23 * b0;
        determinant = value.M11 * d11 - value.M12 * d12 + value.M13 * d13 - value.M14 * d14;
        if (determinant == T.Zero)
        {
            return Zero;
        }

        var inverse = T.One / determinant;
        var a0 = value.M11 * value.M22 - value.M12 * value.M21;
        var a1 = value.M11 * value.M23 - value.M13 * value.M21;
        var a2 = value.M14 * value.M21 - value.M11 * value.M24;
        var a3 = value.M12 * value.M23 - value.M13 * value.M22;
        var a4 = value.M14 * value.M22 - value.M12 * value.M24;
        var a5 = value.M13 * value.M24 - value.M14 * value.M23;
        var d21 = value.M12 * b5 + value.M13 * b4 + value.M14 * b3;
        var d22 = value.M11 * b5 + value.M13 * b2 + value.M14 * b1;
        var d23 = value.M11 * -b4 + value.M12 * b2 + value.M14 * b0;
        var d24 = value.M11 * b3 + value.M12 * -b1 + value.M13 * b0;
        var d31 = value.M42 * a5 + value.M43 * a4 + value.M44 * a3;
        var d32 = value.M41 * a5 + value.M43 * a2 + value.M44 * a1;
        var d33 = value.M41 * -a4 + value.M42 * a2 + value.M44 * a0;
        var d34 = value.M41 * a3 + value.M42 * -a1 + value.M43 * a0;
        var d41 = value.M32 * a5 + value.M33 * a4 + value.M34 * a3;
        var d42 = value.M31 * a5 + value.M33 * a2 + value.M34 * a1;
        var d43 = value.M31 * -a4 + value.M32 * a2 + value.M34 * a0;
        var d44 = value.M31 * a3 + value.M32 * -a1 + value.M33 * a0;
        return new Matrix4x4<T>(d11 * inverse, -d21 * inverse, d31 * inverse, -d41 * inverse,
            -d12 * inverse, d22 * inverse, -d32 * inverse, d42 * inverse,
            d13 * inverse, -d23 * inverse, d33 * inverse, -d43 * inverse,
            -d14 * inverse, d24 * inverse, -d34 * inverse, d44 * inverse);
    }

    private static void Eliminate(ref Matrix4x4<T> result, bool unitLeads) =>
        GaussianElimination.Eliminate(Elements(ref result), 4, 4, unitLeads, false);

    private static Span<T> Elements(ref Matrix4x4<T> value) =>
        MemoryMarshal.CreateSpan(ref Unsafe.As<Vector4<T>, T>(ref value.Row1), 16);

    private static void CheckIndex(int index)
    {
        if ((uint)index > 15)
        {
            throw new ArgumentOutOfRangeException(nameof(index), "Indices for Matrix4x4 run from 0 to 15, inclusive.");
        }
    }

    private static void CheckRowAndColumn(int row, int column)
    {
        if ((uint)row > 3)
        {
            throw new ArgumentOutOfRangeException(nameof(row), "Rows and columns for matrices run from 0 to 3, inclusive.");
        }

        if ((uint)column > 3)
        {
            throw new ArgumentOutOfRangeException(nameof(column), "Rows and columns for matrices run from 0 to 3, inclusive.");
        }
    }
}
