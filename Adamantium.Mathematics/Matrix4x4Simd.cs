using System.Numerics;
using System.Runtime.CompilerServices;
using System.Runtime.Intrinsics;

namespace Adamantium.Mathematics;

/// <summary>
/// The 4x4 matrix work done a row at a time in SIMD registers, by what the processor has: float rows in 128 bits
/// (SSE, NEON); double rows in 256 bits (AVX) or as two 128-bit halves (SSE2, NEON). The matrices are reinterpreted
/// whole as <see cref="SimdRows{TVector}"/>, which the JIT keeps in registers - row by row conversions go through the
/// stack and cost half the speed. A row times a matrix is summed as two pairs of products, each rounded as the
/// element-by-element formula rounds it, so every path - and every processor - gives the same numbers, only where two
/// NaNs meet may a different one come out; fused
/// multiply-add would be about 7% faster for float and 5% for double, but its bits differ between processors. The
/// checks are JIT constants, so one path is left in the machine code.
/// </summary>
internal static class Matrix4x4Simd
{
    /// <summary>The product <paramref name="left"/> * <paramref name="right"/>.</summary>
    [MethodImpl(MethodImplOptions.AggressiveInlining)]
    public static Matrix4x4<T> Multiply<T>(Matrix4x4<T> left, Matrix4x4<T> right) where T : unmanaged, IFloatingPointIeee754<T>
    {
        if (typeof(T) == typeof(float) && Vector128.IsHardwareAccelerated)
        {
            var l = Unsafe.As<Matrix4x4<T>, SimdRows<Vector128<float>>>(ref left);
            var r = Unsafe.As<Matrix4x4<T>, SimdRows<Vector128<float>>>(ref right);
            SimdRows<Vector128<float>> result;
            result.Row1 = Row(l.Row1, r);
            result.Row2 = Row(l.Row2, r);
            result.Row3 = Row(l.Row3, r);
            result.Row4 = Row(l.Row4, r);
            return Unsafe.As<SimdRows<Vector128<float>>, Matrix4x4<T>>(ref result);
        }

        if (typeof(T) == typeof(double) && Vector256.IsHardwareAccelerated)
        {
            var l = Unsafe.As<Matrix4x4<T>, SimdRows<Vector256<double>>>(ref left);
            var r = Unsafe.As<Matrix4x4<T>, SimdRows<Vector256<double>>>(ref right);
            SimdRows<Vector256<double>> result;
            result.Row1 = Row(l.Row1, r);
            result.Row2 = Row(l.Row2, r);
            result.Row3 = Row(l.Row3, r);
            result.Row4 = Row(l.Row4, r);
            return Unsafe.As<SimdRows<Vector256<double>>, Matrix4x4<T>>(ref result);
        }

        if (typeof(T) == typeof(double) && Vector128.IsHardwareAccelerated)
        {
            var l = Unsafe.As<Matrix4x4<T>, SimdRows<DoubleRowHalves>>(ref left);
            var r = Unsafe.As<Matrix4x4<T>, SimdRows<DoubleRowHalves>>(ref right);
            SimdRows<DoubleRowHalves> result;
            result.Row1 = Row(l.Row1, r);
            result.Row2 = Row(l.Row2, r);
            result.Row3 = Row(l.Row3, r);
            result.Row4 = Row(l.Row4, r);
            return Unsafe.As<SimdRows<DoubleRowHalves>, Matrix4x4<T>>(ref result);
        }

        return new Matrix4x4<T>(Row(left.Row1, right), Row(left.Row2, right), Row(left.Row3, right), Row(left.Row4, right));
    }

    /// <summary>The point <paramref name="point"/>, with a W of one, * <paramref name="matrix"/>: the same numbers as
    /// <see cref="Transform{T}"/> gives - where two NaNs meet, which comes out is the processor's - but each coordinate
    /// spread across a register as it stands; a row put together from three numbers and read back whole would stall on
    /// its way through memory.</summary>
    [MethodImpl(MethodImplOptions.AggressiveInlining)]
    public static Vector4<T> TransformPoint<T>(Vector3<T> point, Matrix4x4<T> matrix) where T : unmanaged, IFloatingPointIeee754<T>
    {
        if (typeof(T) == typeof(float) && Vector128.IsHardwareAccelerated)
        {
            var m = Unsafe.As<Matrix4x4<T>, SimdRows<Vector128<float>>>(ref matrix);
            var result = Vector128.Create(float.CreateTruncating(point.X)) * m.Row1 + Vector128.Create(float.CreateTruncating(point.Y)) * m.Row2
                         + (Vector128.Create(float.CreateTruncating(point.Z)) * m.Row3 + m.Row4);
            return Unsafe.As<Vector128<float>, Vector4<T>>(ref result);
        }

        if (typeof(T) == typeof(double) && Vector256.IsHardwareAccelerated)
        {
            var m = Unsafe.As<Matrix4x4<T>, SimdRows<Vector256<double>>>(ref matrix);
            var result = Vector256.Create(double.CreateTruncating(point.X)) * m.Row1 + Vector256.Create(double.CreateTruncating(point.Y)) * m.Row2
                         + (Vector256.Create(double.CreateTruncating(point.Z)) * m.Row3 + m.Row4);
            return Unsafe.As<Vector256<double>, Vector4<T>>(ref result);
        }

        if (typeof(T) == typeof(double) && Vector128.IsHardwareAccelerated)
        {
            var m = Unsafe.As<Matrix4x4<T>, SimdRows<DoubleRowHalves>>(ref matrix);
            var (x, y, z) = (Vector128.Create(double.CreateTruncating(point.X)), Vector128.Create(double.CreateTruncating(point.Y)),
                Vector128.Create(double.CreateTruncating(point.Z)));
            var result = new DoubleRowHalves(x * m.Row1.Low + y * m.Row2.Low + (z * m.Row3.Low + m.Row4.Low),
                x * m.Row1.High + y * m.Row2.High + (z * m.Row3.High + m.Row4.High));
            return Unsafe.As<DoubleRowHalves, Vector4<T>>(ref result);
        }

        return Row(new Vector4<T>(point, T.One), matrix);
    }

    /// <summary>The row vector <paramref name="row"/> * <paramref name="matrix"/>.</summary>
    [MethodImpl(MethodImplOptions.AggressiveInlining)]
    public static Vector4<T> Transform<T>(Vector4<T> row, Matrix4x4<T> matrix) where T : unmanaged, IFloatingPointIeee754<T>
    {
        if (typeof(T) == typeof(float) && Vector128.IsHardwareAccelerated)
        {
            var result = Row(Unsafe.As<Vector4<T>, Vector128<float>>(ref row),
                Unsafe.As<Matrix4x4<T>, SimdRows<Vector128<float>>>(ref matrix));
            return Unsafe.As<Vector128<float>, Vector4<T>>(ref result);
        }

        if (typeof(T) == typeof(double) && Vector256.IsHardwareAccelerated)
        {
            var result = Row(Unsafe.As<Vector4<T>, Vector256<double>>(ref row),
                Unsafe.As<Matrix4x4<T>, SimdRows<Vector256<double>>>(ref matrix));
            return Unsafe.As<Vector256<double>, Vector4<T>>(ref result);
        }

        if (typeof(T) == typeof(double) && Vector128.IsHardwareAccelerated)
        {
            var result = Row(Unsafe.As<Vector4<T>, DoubleRowHalves>(ref row),
                Unsafe.As<Matrix4x4<T>, SimdRows<DoubleRowHalves>>(ref matrix));
            return Unsafe.As<DoubleRowHalves, Vector4<T>>(ref result);
        }

        return Row(row, matrix);
    }

    [MethodImpl(MethodImplOptions.AggressiveInlining)]
    private static Vector128<float> Row(Vector128<float> row, SimdRows<Vector128<float>> m) =>
        Vector128.Create(row.GetElement(0)) * m.Row1 + Vector128.Create(row.GetElement(1)) * m.Row2 +
        (Vector128.Create(row.GetElement(2)) * m.Row3 + Vector128.Create(row.GetElement(3)) * m.Row4);

    [MethodImpl(MethodImplOptions.AggressiveInlining)]
    private static Vector256<double> Row(Vector256<double> row, SimdRows<Vector256<double>> m) =>
        Vector256.Create(row.GetElement(0)) * m.Row1 + Vector256.Create(row.GetElement(1)) * m.Row2 +
        (Vector256.Create(row.GetElement(2)) * m.Row3 + Vector256.Create(row.GetElement(3)) * m.Row4);

    [MethodImpl(MethodImplOptions.AggressiveInlining)]
    private static DoubleRowHalves Row(DoubleRowHalves row, SimdRows<DoubleRowHalves> m)
    {
        var x = Vector128.Create(row.Low.GetElement(0));
        var y = Vector128.Create(row.Low.GetElement(1));
        var z = Vector128.Create(row.High.GetElement(0));
        var w = Vector128.Create(row.High.GetElement(1));
        return new DoubleRowHalves(x * m.Row1.Low + y * m.Row2.Low + (z * m.Row3.Low + w * m.Row4.Low),
            x * m.Row1.High + y * m.Row2.High + (z * m.Row3.High + w * m.Row4.High));
    }

    [MethodImpl(MethodImplOptions.AggressiveInlining)]
    private static Vector4<T> Row<T>(Vector4<T> row, Matrix4x4<T> matrix) where T : unmanaged, IFloatingPointIeee754<T> =>
        row.X * matrix.Row1 + row.Y * matrix.Row2 + (row.Z * matrix.Row3 + row.W * matrix.Row4);
}
