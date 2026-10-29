using System;
using System.Runtime.CompilerServices;
using System.Runtime.InteropServices;
using System.Runtime.Intrinsics;
using System.Runtime.Intrinsics.Arm;
using System.Runtime.Intrinsics.X86;

namespace Adamantium.Imaging.Jpeg;

/// <summary>The 8×8 inverse DCT of ITU-T T.81 (A.3.3) in two passes, coefficients dequantized, then level-shifted and
/// clamped samples of 8 or 12 bits. An 8-bit block is eight rows of eight - with AVX a vector a row, with NEON or SSE
/// two: each pass the even and odd halves of the 8-point transform, between them a transpose. Elsewhere, and for 12
/// bits, the passes multiply by the cosine matrix four samples a vector.</summary>
[SkipLocalsInit]
internal static class JpegIdct
{
    // Basis[k * 8 + n] = C(n) / 2 · cos((2k + 1) n π / 16), C(0) = 1/√2 - so f = Basisᵀ·F·Basis in both directions.
    private static readonly float[] Basis = BuildBasis();

    // The basis transposed: column u of the row pass.
    private static readonly float[] Transposed = Transpose(Basis);

    private static readonly float HalfOverRootTwo = (float)(0.5 / Math.Sqrt(2));
    private static readonly float HalfCos1 = HalfCos(1);
    private static readonly float HalfCos2 = HalfCos(2);
    private static readonly float HalfCos3 = HalfCos(3);
    private static readonly float HalfCos5 = HalfCos(5);
    private static readonly float HalfCos6 = HalfCos(6);
    private static readonly float HalfCos7 = HalfCos(7);

    private static float HalfCos(int sixteenths) => (float)(Math.Cos(sixteenths * Math.PI / 16) / 2);

    private static float[] BuildBasis()
    {
        var basis = new float[64];
        for (var k = 0; k < 8; k++)
        {
            for (var n = 0; n < 8; n++)
            {
                var scale = n == 0 ? 1 / Math.Sqrt(2) : 1;
                basis[k * 8 + n] = (float)(scale / 2 * Math.Cos((2 * k + 1) * n * Math.PI / 16));
            }
        }

        return basis;
    }

    private static float[] Transpose(float[] matrix)
    {
        var transposed = new float[64];
        for (var i = 0; i < 8; i++)
        {
            for (var j = 0; j < 8; j++)
            {
                transposed[j * 8 + i] = matrix[i * 8 + j];
            }
        }

        return transposed;
    }

    /// <summary>The bits of <paramref name="rows"/> for a nonzero AC coefficient at natural position
    /// <paramref name="at"/>: its row in the low byte, its column in the next.</summary>
    [MethodImpl(MethodImplOptions.AggressiveInlining)]
    public static int Mask(int at) => (1 << (at >> 3)) | (256 << (at & 7));

    /// <summary>Turns <paramref name="coefficients"/> (natural order, quantized) times <paramref name="quantization"/>
    /// into 8×8 samples written to <paramref name="output"/> at <paramref name="offset"/>, rows
    /// <paramref name="stride"/> apart. <paramref name="rows"/> is the <see cref="Mask"/> of every AC coefficient that
    /// is not zero - rows and columns of the block with any value past the DC term; 0 for the DC term alone.</summary>
    [MethodImpl(MethodImplOptions.AggressiveOptimization)]
    public static void Transform(ReadOnlySpan<short> coefficients, ReadOnlySpan<ushort> quantization, int rows,
        int precision, Span<byte> output, int offset, int stride)
    {
        if (rows != 0 && offset + 7L * stride + 8 <= output.Length)
        {
            if (Avx.IsSupported && Fma.IsSupported)
            {
                Butterflies(coefficients, quantization, output, offset, stride);
                return;
            }

            if (AdvSimd.Arm64.IsSupported || Sse.IsSupported)
            {
                NarrowButterflies(coefficients, quantization, output, offset, stride);
                return;
            }
        }

        Span<float> samples = stackalloc float[64];
        if (Inverse(coefficients, quantization, rows, samples, out var flat))
        {
            output.Slice(offset, 8).Fill((byte)Clamp(flat, precision));
            for (var y = 1; y < 8; y++)
            {
                output.Slice(offset, 8).CopyTo(output[(offset + y * stride)..]);
            }

            return;
        }

        // Shifted and clamped, then cut to bytes: truncation of a value from 0.5 to 255.5 rounds it.
        var shift = Vector128.Create(128.5f);
        var high = Vector128.Create(255f);
        ref var first = ref MemoryMarshal.GetReference(samples);
        for (var y = 0; y < 8; y++)
        {
            var left = Vector128.ConvertToInt32(Vector128.Min(Vector128.Max(
                Vector128.LoadUnsafe(ref first, (nuint)(y * 8)) + shift, Vector128<float>.Zero), high));
            var right = Vector128.ConvertToInt32(Vector128.Min(Vector128.Max(
                Vector128.LoadUnsafe(ref first, (nuint)(y * 8 + 4)) + shift, Vector128<float>.Zero), high));
            var shorts = Vector128.Narrow(left, right);
            var bytes = Vector128.Narrow(shorts, shorts).AsUInt64().ToScalar();
            MemoryMarshal.Write(output.Slice(offset + y * stride, 8), in bytes);
        }
    }

    /// <summary>As the 8-bit <see cref="Transform(ReadOnlySpan{short}, ReadOnlySpan{ushort}, int, int, Span{byte}, int, int)"/>,
    /// for samples of up to 16 bits.</summary>
    [MethodImpl(MethodImplOptions.AggressiveOptimization)]
    public static void Transform(ReadOnlySpan<short> coefficients, ReadOnlySpan<ushort> quantization, int rows,
        int precision, Span<ushort> output, int offset, int stride)
    {
        Span<float> samples = stackalloc float[64];
        if (Inverse(coefficients, quantization, rows, samples, out var flat))
        {
            var value = (ushort)Clamp(flat, precision);
            for (var y = 0; y < 8; y++)
            {
                output.Slice(offset + y * stride, 8).Fill(value);
            }

            return;
        }

        var level = 1 << (precision - 1);
        var top = (1 << precision) - 1;
        for (var y = 0; y < 8; y++)
        {
            for (var x = 0; x < 8; x++)
            {
                output[offset + y * stride + x] =
                    (ushort)Math.Clamp((int)MathF.Floor(samples[y * 8 + x] + level + 0.5f), 0, top);
            }
        }
    }

    [MethodImpl(MethodImplOptions.AggressiveOptimization)]
    private static void Butterflies(ReadOnlySpan<short> coefficients, ReadOnlySpan<ushort> quantization,
        Span<byte> output, int offset, int stride)
    {
        ref var coefficient = ref MemoryMarshal.GetReference(coefficients);
        ref var step = ref MemoryMarshal.GetReference(quantization);
        var r0 = Dequantize(ref coefficient, ref step, 0);
        var r1 = Dequantize(ref coefficient, ref step, 8);
        var r2 = Dequantize(ref coefficient, ref step, 16);
        var r3 = Dequantize(ref coefficient, ref step, 24);
        var r4 = Dequantize(ref coefficient, ref step, 32);
        var r5 = Dequantize(ref coefficient, ref step, 40);
        var r6 = Dequantize(ref coefficient, ref step, 48);
        var r7 = Dequantize(ref coefficient, ref step, 56);
        Transpose(ref r0, ref r1, ref r2, ref r3, ref r4, ref r5, ref r6, ref r7);
        Butterfly(ref r0, ref r1, ref r2, ref r3, ref r4, ref r5, ref r6, ref r7);
        Transpose(ref r0, ref r1, ref r2, ref r3, ref r4, ref r5, ref r6, ref r7);
        Butterfly(ref r0, ref r1, ref r2, ref r3, ref r4, ref r5, ref r6, ref r7);
        ref var into = ref output[offset];
        WriteRow(r0, ref into);
        WriteRow(r1, ref Unsafe.Add(ref into, stride));
        WriteRow(r2, ref Unsafe.Add(ref into, 2 * stride));
        WriteRow(r3, ref Unsafe.Add(ref into, 3 * stride));
        WriteRow(r4, ref Unsafe.Add(ref into, 4 * stride));
        WriteRow(r5, ref Unsafe.Add(ref into, 5 * stride));
        WriteRow(r6, ref Unsafe.Add(ref into, 6 * stride));
        WriteRow(r7, ref Unsafe.Add(ref into, 7 * stride));
    }

    [MethodImpl(MethodImplOptions.AggressiveInlining)]
    private static Vector256<float> Dequantize(ref short coefficient, ref ushort step, int at)
    {
        var values = Vector256.WidenLower(Vector128.LoadUnsafe(ref coefficient, (nuint)at).ToVector256Unsafe());
        var steps = Vector256.WidenLower(Vector128.LoadUnsafe(ref step, (nuint)at).ToVector256Unsafe());
        return Vector256.ConvertToSingle(values * steps.AsInt32());
    }

    [MethodImpl(MethodImplOptions.AggressiveInlining)]
    private static void Transpose(ref Vector256<float> r0, ref Vector256<float> r1, ref Vector256<float> r2,
        ref Vector256<float> r3, ref Vector256<float> r4, ref Vector256<float> r5, ref Vector256<float> r6,
        ref Vector256<float> r7)
    {
        var t0 = Avx.UnpackLow(r0, r1);
        var t1 = Avx.UnpackHigh(r0, r1);
        var t2 = Avx.UnpackLow(r2, r3);
        var t3 = Avx.UnpackHigh(r2, r3);
        var t4 = Avx.UnpackLow(r4, r5);
        var t5 = Avx.UnpackHigh(r4, r5);
        var t6 = Avx.UnpackLow(r6, r7);
        var t7 = Avx.UnpackHigh(r6, r7);
        var s0 = Avx.Shuffle(t0, t2, 0x44);
        var s1 = Avx.Shuffle(t0, t2, 0xEE);
        var s2 = Avx.Shuffle(t1, t3, 0x44);
        var s3 = Avx.Shuffle(t1, t3, 0xEE);
        var s4 = Avx.Shuffle(t4, t6, 0x44);
        var s5 = Avx.Shuffle(t4, t6, 0xEE);
        var s6 = Avx.Shuffle(t5, t7, 0x44);
        var s7 = Avx.Shuffle(t5, t7, 0xEE);
        r0 = Avx.Permute2x128(s0, s4, 0x20);
        r1 = Avx.Permute2x128(s1, s5, 0x20);
        r2 = Avx.Permute2x128(s2, s6, 0x20);
        r3 = Avx.Permute2x128(s3, s7, 0x20);
        r4 = Avx.Permute2x128(s0, s4, 0x31);
        r5 = Avx.Permute2x128(s1, s5, 0x31);
        r6 = Avx.Permute2x128(s2, s6, 0x31);
        r7 = Avx.Permute2x128(s3, s7, 0x31);
    }

    [MethodImpl(MethodImplOptions.AggressiveInlining)]
    private static void Butterfly(ref Vector256<float> f0, ref Vector256<float> f1, ref Vector256<float> f2,
        ref Vector256<float> f3, ref Vector256<float> f4, ref Vector256<float> f5, ref Vector256<float> f6,
        ref Vector256<float> f7)
    {
        var half = Vector256.Create(HalfOverRootTwo);
        var p0 = (f0 + f4) * half;
        var p1 = (f0 - f4) * half;
        var c2 = Vector256.Create(HalfCos2);
        var c6 = Vector256.Create(HalfCos6);
        var q0 = Vector256.FusedMultiplyAdd(f2, c2, f6 * c6);
        var q1 = Vector256.FusedMultiplyAdd(f2, c6, -(f6 * c2));
        var e0 = p0 + q0;
        var e1 = p1 + q1;
        var e2 = p1 - q1;
        var e3 = p0 - q0;

        var c1 = Vector256.Create(HalfCos1);
        var c3 = Vector256.Create(HalfCos3);
        var c5 = Vector256.Create(HalfCos5);
        var c7 = Vector256.Create(HalfCos7);
        var o0 = Vector256.FusedMultiplyAdd(f1, c1, Vector256.FusedMultiplyAdd(f3, c3,
            Vector256.FusedMultiplyAdd(f5, c5, f7 * c7)));
        var o1 = Vector256.FusedMultiplyAdd(f1, c3, -Vector256.FusedMultiplyAdd(f3, c7,
            Vector256.FusedMultiplyAdd(f5, c1, f7 * c5)));
        var o2 = Vector256.FusedMultiplyAdd(f1, c5, Vector256.FusedMultiplyAdd(f5, c7,
            Vector256.FusedMultiplyAdd(f7, c3, -(f3 * c1))));
        var o3 = Vector256.FusedMultiplyAdd(f1, c7, Vector256.FusedMultiplyAdd(f5, c3,
            -Vector256.FusedMultiplyAdd(f3, c5, f7 * c1)));

        f0 = e0 + o0;
        f7 = e0 - o0;
        f1 = e1 + o1;
        f6 = e1 - o1;
        f2 = e2 + o2;
        f5 = e2 - o2;
        f3 = e3 + o3;
        f4 = e3 - o3;
    }

    [MethodImpl(MethodImplOptions.AggressiveInlining)]
    private static void WriteRow(Vector256<float> row, ref byte into)
    {
        var samples = Vector256.ConvertToInt32(Vector256.Min(Vector256.Max(row + Vector256.Create(128.5f),
            Vector256<float>.Zero), Vector256.Create(255f)));
        var shorts = Vector128.Narrow(samples.GetLower(), samples.GetUpper());
        Unsafe.WriteUnaligned(ref into, Vector128.Narrow(shorts, shorts).AsUInt64().ToScalar());
    }

    [MethodImpl(MethodImplOptions.AggressiveOptimization)]
    private static void NarrowButterflies(ReadOnlySpan<short> coefficients, ReadOnlySpan<ushort> quantization,
        Span<byte> output, int offset, int stride)
    {
        ref var coefficient = ref MemoryMarshal.GetReference(coefficients);
        ref var step = ref MemoryMarshal.GetReference(quantization);
        var (l0, r0) = DequantizeHalves(ref coefficient, ref step, 0);
        var (l1, r1) = DequantizeHalves(ref coefficient, ref step, 8);
        var (l2, r2) = DequantizeHalves(ref coefficient, ref step, 16);
        var (l3, r3) = DequantizeHalves(ref coefficient, ref step, 24);
        var (l4, r4) = DequantizeHalves(ref coefficient, ref step, 32);
        var (l5, r5) = DequantizeHalves(ref coefficient, ref step, 40);
        var (l6, r6) = DequantizeHalves(ref coefficient, ref step, 48);
        var (l7, r7) = DequantizeHalves(ref coefficient, ref step, 56);
        for (var pass = 0; pass < 2; pass++)
        {
            Transpose(ref l0, ref l1, ref l2, ref l3);
            Transpose(ref r0, ref r1, ref r2, ref r3);
            Transpose(ref l4, ref l5, ref l6, ref l7);
            Transpose(ref r4, ref r5, ref r6, ref r7);
            (l4, r0) = (r0, l4);
            (l5, r1) = (r1, l5);
            (l6, r2) = (r2, l6);
            (l7, r3) = (r3, l7);
            Butterfly(ref l0, ref l1, ref l2, ref l3, ref l4, ref l5, ref l6, ref l7);
            Butterfly(ref r0, ref r1, ref r2, ref r3, ref r4, ref r5, ref r6, ref r7);
        }

        ref var into = ref output[offset];
        WriteRow(l0, r0, ref into);
        WriteRow(l1, r1, ref Unsafe.Add(ref into, stride));
        WriteRow(l2, r2, ref Unsafe.Add(ref into, 2 * stride));
        WriteRow(l3, r3, ref Unsafe.Add(ref into, 3 * stride));
        WriteRow(l4, r4, ref Unsafe.Add(ref into, 4 * stride));
        WriteRow(l5, r5, ref Unsafe.Add(ref into, 5 * stride));
        WriteRow(l6, r6, ref Unsafe.Add(ref into, 6 * stride));
        WriteRow(l7, r7, ref Unsafe.Add(ref into, 7 * stride));
    }

    [MethodImpl(MethodImplOptions.AggressiveInlining)]
    private static (Vector128<float> Left, Vector128<float> Right) DequantizeHalves(ref short coefficient,
        ref ushort step, int at)
    {
        var values = Vector128.LoadUnsafe(ref coefficient, (nuint)at);
        var steps = Vector128.LoadUnsafe(ref step, (nuint)at);
        var (valuesLow, valuesHigh) = Vector128.Widen(values);
        var (stepsLow, stepsHigh) = Vector128.Widen(steps);
        return (Vector128.ConvertToSingle(valuesLow * stepsLow.AsInt32()),
            Vector128.ConvertToSingle(valuesHigh * stepsHigh.AsInt32()));
    }

    [MethodImpl(MethodImplOptions.AggressiveInlining)]
    private static void Transpose(ref Vector128<float> r0, ref Vector128<float> r1, ref Vector128<float> r2,
        ref Vector128<float> r3)
    {
        Vector128<float> t0, t1, t2, t3;
        if (AdvSimd.Arm64.IsSupported)
        {
            (t0, t1) = (AdvSimd.Arm64.ZipLow(r0, r1), AdvSimd.Arm64.ZipHigh(r0, r1));
            (t2, t3) = (AdvSimd.Arm64.ZipLow(r2, r3), AdvSimd.Arm64.ZipHigh(r2, r3));
            r0 = AdvSimd.Arm64.ZipLow(t0.AsUInt64(), t2.AsUInt64()).AsSingle();
            r1 = AdvSimd.Arm64.ZipHigh(t0.AsUInt64(), t2.AsUInt64()).AsSingle();
            r2 = AdvSimd.Arm64.ZipLow(t1.AsUInt64(), t3.AsUInt64()).AsSingle();
            r3 = AdvSimd.Arm64.ZipHigh(t1.AsUInt64(), t3.AsUInt64()).AsSingle();
            return;
        }

        (t0, t1) = (Sse.UnpackLow(r0, r1), Sse.UnpackHigh(r0, r1));
        (t2, t3) = (Sse.UnpackLow(r2, r3), Sse.UnpackHigh(r2, r3));
        r0 = Sse.MoveLowToHigh(t0, t2);
        r1 = Sse.MoveHighToLow(t2, t0);
        r2 = Sse.MoveLowToHigh(t1, t3);
        r3 = Sse.MoveHighToLow(t3, t1);
    }

    [MethodImpl(MethodImplOptions.AggressiveInlining)]
    private static void Butterfly(ref Vector128<float> f0, ref Vector128<float> f1, ref Vector128<float> f2,
        ref Vector128<float> f3, ref Vector128<float> f4, ref Vector128<float> f5, ref Vector128<float> f6,
        ref Vector128<float> f7)
    {
        var half = Vector128.Create(HalfOverRootTwo);
        var p0 = (f0 + f4) * half;
        var p1 = (f0 - f4) * half;
        var c2 = Vector128.Create(HalfCos2);
        var c6 = Vector128.Create(HalfCos6);
        var q0 = Vector128.MultiplyAddEstimate(f2, c2, f6 * c6);
        var q1 = Vector128.MultiplyAddEstimate(f2, c6, -(f6 * c2));
        var e0 = p0 + q0;
        var e1 = p1 + q1;
        var e2 = p1 - q1;
        var e3 = p0 - q0;

        var c1 = Vector128.Create(HalfCos1);
        var c3 = Vector128.Create(HalfCos3);
        var c5 = Vector128.Create(HalfCos5);
        var c7 = Vector128.Create(HalfCos7);
        var o0 = Vector128.MultiplyAddEstimate(f1, c1, Vector128.MultiplyAddEstimate(f3, c3,
            Vector128.MultiplyAddEstimate(f5, c5, f7 * c7)));
        var o1 = Vector128.MultiplyAddEstimate(f1, c3, -Vector128.MultiplyAddEstimate(f3, c7,
            Vector128.MultiplyAddEstimate(f5, c1, f7 * c5)));
        var o2 = Vector128.MultiplyAddEstimate(f1, c5, Vector128.MultiplyAddEstimate(f5, c7,
            Vector128.MultiplyAddEstimate(f7, c3, -(f3 * c1))));
        var o3 = Vector128.MultiplyAddEstimate(f1, c7, Vector128.MultiplyAddEstimate(f5, c3,
            -Vector128.MultiplyAddEstimate(f3, c5, f7 * c1)));

        f0 = e0 + o0;
        f7 = e0 - o0;
        f1 = e1 + o1;
        f6 = e1 - o1;
        f2 = e2 + o2;
        f5 = e2 - o2;
        f3 = e3 + o3;
        f4 = e3 - o3;
    }

    [MethodImpl(MethodImplOptions.AggressiveInlining)]
    private static void WriteRow(Vector128<float> left, Vector128<float> right, ref byte into)
    {
        var shift = Vector128.Create(128.5f);
        var top = Vector128.Create(255f);
        var low = Vector128.ConvertToInt32(Vector128.Min(Vector128.Max(left + shift, Vector128<float>.Zero), top));
        var high = Vector128.ConvertToInt32(Vector128.Min(Vector128.Max(right + shift, Vector128<float>.Zero), top));
        var shorts = Vector128.Narrow(low, high);
        Unsafe.WriteUnaligned(ref into, Vector128.Narrow(shorts, shorts).AsUInt64().ToScalar());
    }

    /// <summary>The DC term alone, as the sample of its whole block: what the eighth-scale picture is made of.</summary>
    public static int Average(int coefficient, int quantization, int precision) =>
        Clamp(coefficient * quantization / 8f, precision);

    private static int Clamp(float value, int precision) =>
        Math.Clamp((int)MathF.Floor(value + (1 << (precision - 1)) + 0.5f), 0, (1 << precision) - 1);

    // Into samples unless the block is its DC term alone; then flat is the value of all 64.
    [MethodImpl(MethodImplOptions.AggressiveOptimization)]
    private static bool Inverse(ReadOnlySpan<short> coefficients, ReadOnlySpan<ushort> quantization, int rows,
        Span<float> samples, out float flat)
    {
        if (rows == 0)
        {
            flat = coefficients[0] * quantization[0] / 8f;
            return true;
        }

        flat = 0;
        rows |= 1;

        // The rows of F that are not zero, dequantized once, as vectors of four.
        Span<Vector128<float>> lefts = stackalloc Vector128<float>[8];
        Span<Vector128<float>> rights = stackalloc Vector128<float>[8];
        Span<int> present = stackalloc int[8];
        var count = 0;
        for (var v = 0; v < 8; v++)
        {
            if ((rows & (1 << v)) == 0)
            {
                continue;
            }

            var at = v * 8;
            lefts[count] = Vector128.Create(coefficients[at] * quantization[at],
                (float)(coefficients[at + 1] * quantization[at + 1]), coefficients[at + 2] * quantization[at + 2],
                coefficients[at + 3] * quantization[at + 3]);
            rights[count] = Vector128.Create(coefficients[at + 4] * quantization[at + 4],
                (float)(coefficients[at + 5] * quantization[at + 5]), coefficients[at + 6] * quantization[at + 6],
                coefficients[at + 7] * quantization[at + 7]);
            present[count++] = v;
        }

        // Column pass: G[y] = Σv Basis[y][v] · F[v], a row of eight at a time.
        Span<float> columns = stackalloc float[64];
        ref var basis = ref MemoryMarshal.GetArrayDataReference(Basis);
        ref var transposed = ref MemoryMarshal.GetArrayDataReference(Transposed);
        for (var y = 0; y < 8; y++)
        {
            var left = Vector128<float>.Zero;
            var right = Vector128<float>.Zero;
            for (var i = 0; i < count; i++)
            {
                var weight = Vector128.Create(Unsafe.Add(ref basis, y * 8 + present[i]));
                left += weight * lefts[i];
                right += weight * rights[i];
            }

            left.CopyTo(columns[(y * 8)..]);
            right.CopyTo(columns[(y * 8 + 4)..]);
        }

        // Row pass: f[y][x] = Σu G[y][u] · Basis[x][u], eight x at a time from the transposed basis.
        for (var y = 0; y < 8; y++)
        {
            var left = Vector128<float>.Zero;
            var right = Vector128<float>.Zero;
            for (var u = 0; u < 8; u++)
            {
                var weight = Vector128.Create(columns[y * 8 + u]);
                left += weight * Vector128.LoadUnsafe(ref Unsafe.Add(ref transposed, u * 8));
                right += weight * Vector128.LoadUnsafe(ref Unsafe.Add(ref transposed, u * 8 + 4));
            }

            left.CopyTo(samples[(y * 8)..]);
            right.CopyTo(samples[(y * 8 + 4)..]);
        }

        return false;
    }
}
