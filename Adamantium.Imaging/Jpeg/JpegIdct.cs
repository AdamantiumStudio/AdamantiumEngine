using System;
using System.Runtime.CompilerServices;
using System.Runtime.InteropServices;
using System.Runtime.Intrinsics;

namespace Adamantium.Imaging.Jpeg;

/// <summary>The 8×8 inverse DCT of ITU-T T.81 (A.3.3) as two passes of the cosine matrix, four samples a vector:
/// coefficients dequantized, then level-shifted and clamped samples of 8 or 12 bits.</summary>
[SkipLocalsInit]
internal static class JpegIdct
{
    // Basis[k * 8 + n] = C(n) / 2 · cos((2k + 1) n π / 16), C(0) = 1/√2 - so f = Basisᵀ·F·Basis in both directions.
    private static readonly float[] Basis = BuildBasis();

    // The basis transposed: column u of the row pass.
    private static readonly float[] Transposed = Transpose(Basis);

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
    public static void Transform(ReadOnlySpan<short> coefficients, ReadOnlySpan<ushort> quantization, int rows,
        int precision, Span<byte> output, int offset, int stride)
    {
        if (Vector256.IsHardwareAccelerated && rows != 0 && offset + 7L * stride + 8 <= output.Length)
        {
            Wide(coefficients, quantization, rows | 1, output, offset, stride);
            return;
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

    // The 8-bit transform a row of eight a vector, where the hardware has vectors of 256 bits.
    private static void Wide(ReadOnlySpan<short> coefficients, ReadOnlySpan<ushort> quantization, int rows,
        Span<byte> output, int offset, int stride)
    {
        Span<Vector256<float>> present = stackalloc Vector256<float>[8];
        Span<int> which = stackalloc int[8];
        var count = 0;
        ref var coefficient = ref MemoryMarshal.GetReference(coefficients);
        ref var step = ref MemoryMarshal.GetReference(quantization);
        for (var v = 0; v < 8; v++)
        {
            if ((rows & (1 << v)) == 0)
            {
                continue;
            }

            var values = Vector128.LoadUnsafe(ref coefficient, (nuint)(v * 8));
            var steps = Vector128.LoadUnsafe(ref step, (nuint)(v * 8)).AsInt16();
            var low = Vector128.WidenLower(values) * Vector128.WidenLower(steps.AsUInt16()).AsInt32();
            var high = Vector128.WidenUpper(values) * Vector128.WidenUpper(steps.AsUInt16()).AsInt32();
            present[count] = Vector256.ConvertToSingle(Vector256.Create(low, high));
            which[count++] = v;
        }

        ref var basis = ref MemoryMarshal.GetArrayDataReference(Basis);
        ref var transposed = ref MemoryMarshal.GetArrayDataReference(Transposed);
        Span<float> columns = stackalloc float[64];
        ref var column = ref MemoryMarshal.GetReference(columns);
        for (var y = 0; y < 8; y++)
        {
            var sum = Vector256<float>.Zero;
            for (var i = 0; i < count; i++)
            {
                sum = Vector256.FusedMultiplyAdd(present[i], Vector256.Create(Unsafe.Add(ref basis, y * 8 + which[i])),
                    sum);
            }

            sum.StoreUnsafe(ref column, (nuint)(y * 8));
        }

        // Row pass over the columns of F that hold anything: the others are zero in G too.
        Span<int> used = stackalloc int[8];
        var columnCount = 0;
        for (var u = 0; u < 8; u++)
        {
            if (u == 0 || (rows & (256 << u)) != 0)
            {
                used[columnCount++] = u;
            }
        }

        var shift = Vector256.Create(128.5f);
        var top = Vector256.Create(255f);
        for (var y = 0; y < 8; y++)
        {
            var sum = Vector256<float>.Zero;
            for (var i = 0; i < columnCount; i++)
            {
                var u = used[i];
                sum = Vector256.FusedMultiplyAdd(Vector256.Create(Unsafe.Add(ref column, y * 8 + u)),
                    Vector256.LoadUnsafe(ref transposed, (nuint)(u * 8)), sum);
            }

            var samples = Vector256.ConvertToInt32(Vector256.Min(Vector256.Max(sum + shift, Vector256<float>.Zero),
                top));
            var shorts = Vector128.Narrow(samples.GetLower(), samples.GetUpper());
            var bytes = Vector128.Narrow(shorts, shorts).AsUInt64().ToScalar();
            Unsafe.WriteUnaligned(ref output[offset + y * stride], bytes);
        }
    }

    /// <summary>The DC term alone, as the sample of its whole block: what the eighth-scale picture is made of.</summary>
    public static int Average(int coefficient, int quantization, int precision) =>
        Clamp(coefficient * quantization / 8f, precision);

    private static int Clamp(float value, int precision) =>
        Math.Clamp((int)MathF.Floor(value + (1 << (precision - 1)) + 0.5f), 0, (1 << precision) - 1);

    // Into samples unless the block is its DC term alone; then flat is the value of all 64.
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
