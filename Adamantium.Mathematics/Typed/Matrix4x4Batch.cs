using System;
using System.Numerics;
using System.Runtime.InteropServices;
using System.Runtime.Intrinsics;

namespace Adamantium.Mathematics.Typed;

/// <summary>
/// A 4x4 matrix applied to many points kept as separate arrays of X, Y and Z, and points of double brought near an
/// origin as float: eight floats or four doubles at a time in 256 bits (AVX), four or two in 128 (SSE, NEON), the rest
/// - and every Half - one by one. Each sum is grouped as <see cref="Matrix4x4Simd"/> groups a row, without fused
/// multiply-add, so a point comes out as the same number as it does alone, on every path; where two NaNs meet, which
/// of them comes out is the processor's. The checks are JIT constants.
/// </summary>
internal static class Matrix4x4Batch
{
    /// <summary>Each point times <paramref name="matrix"/>, divided by the W of the answer.</summary>
    public static void TransformCoordinate<T>(ReadOnlySpan<T> x, ReadOnlySpan<T> y, ReadOnlySpan<T> z, Matrix4x4<T> matrix,
        Span<T> outX, Span<T> outY, Span<T> outZ) where T : unmanaged, IFloatingPointIeee754<T>
    {
        var done = 0;
        if (typeof(T) == typeof(float) || typeof(T) == typeof(double))
        {
            if (Vector256.IsHardwareAccelerated)
            {
                done = Coordinates(x, y, z, Rows<T, Vector256<T>>(matrix, Vector256.Create), outX, outY, outZ);
            }
            else if (Vector128.IsHardwareAccelerated)
            {
                done = Coordinates(x, y, z, Rows<T, Vector128<T>>(matrix, Vector128.Create), outX, outY, outZ);
            }
        }

        for (var i = done; i < x.Length; i++)
        {
            var point = Vector3<T>.TransformCoordinate(new Vector3<T>(x[i], y[i], z[i]), matrix);
            (outX[i], outY[i], outZ[i]) = (point.X, point.Y, point.Z);
        }
    }

    /// <summary>Each direction times <paramref name="matrix"/>, without its translation.</summary>
    public static void TransformNormal<T>(ReadOnlySpan<T> x, ReadOnlySpan<T> y, ReadOnlySpan<T> z, Matrix4x4<T> matrix,
        Span<T> outX, Span<T> outY, Span<T> outZ) where T : unmanaged, IFloatingPointIeee754<T>
    {
        var done = 0;
        if (typeof(T) == typeof(float) || typeof(T) == typeof(double))
        {
            if (Vector256.IsHardwareAccelerated)
            {
                done = Normals(x, y, z, Rows<T, Vector256<T>>(matrix, Vector256.Create), outX, outY, outZ);
            }
            else if (Vector128.IsHardwareAccelerated)
            {
                done = Normals(x, y, z, Rows<T, Vector128<T>>(matrix, Vector128.Create), outX, outY, outZ);
            }
        }

        for (var i = done; i < x.Length; i++)
        {
            var direction = Vector3<T>.TransformNormal(new Vector3<T>(x[i], y[i], z[i]), matrix);
            (outX[i], outY[i], outZ[i]) = (direction.X, direction.Y, direction.Z);
        }
    }

    /// <summary>Each of <paramref name="values"/> less the matching component of <paramref name="origin"/>, which
    /// repeats every three values, rounded to float.</summary>
    public static void Narrow(ReadOnlySpan<double> values, Vector3<double> origin, Span<float> narrowed)
    {
        var i = 0;
        ref var from = ref MemoryMarshal.GetReference(values);
        ref var to = ref MemoryMarshal.GetReference(narrowed);
        if (Vector256.IsHardwareAccelerated)
        {
            Span<Vector256<double>> offsets =
            [
                Vector256.Create(origin.X, origin.Y, origin.Z, origin.X), Vector256.Create(origin.Y, origin.Z, origin.X, origin.Y),
                Vector256.Create(origin.Z, origin.X, origin.Y, origin.Z)
            ];
            for (; i + 24 <= values.Length; i += 24)
            {
                for (var part = 0; part < 6; part += 2)
                {
                    var at = (nuint)(i + part * 4);
                    Vector256.Narrow(Vector256.LoadUnsafe(ref from, at) - offsets[part % 3], Vector256.LoadUnsafe(ref from, at + 4) - offsets[(part + 1) % 3])
                        .StoreUnsafe(ref to, at);
                }
            }
        }
        else if (Vector128.IsHardwareAccelerated)
        {
            Span<Vector128<double>> offsets =
                [Vector128.Create(origin.X, origin.Y), Vector128.Create(origin.Z, origin.X), Vector128.Create(origin.Y, origin.Z)];
            for (; i + 12 <= values.Length; i += 12)
            {
                for (var part = 0; part < 6; part += 2)
                {
                    var at = (nuint)(i + part * 2);
                    Vector128.Narrow(Vector128.LoadUnsafe(ref from, at) - offsets[part % 3], Vector128.LoadUnsafe(ref from, at + 2) - offsets[(part + 1) % 3])
                        .StoreUnsafe(ref to, at);
                }
            }
        }

        for (; i < values.Length; i++)
        {
            narrowed[i] = (float)(values[i] - origin[i % 3]);
        }
    }

    /// <summary>Each of <paramref name="values"/> less <paramref name="origin"/>, rounded to float.</summary>
    public static void Narrow(ReadOnlySpan<double> values, double origin, Span<float> narrowed)
    {
        var i = 0;
        ref var from = ref MemoryMarshal.GetReference(values);
        ref var to = ref MemoryMarshal.GetReference(narrowed);
        if (Vector256.IsHardwareAccelerated)
        {
            var offset = Vector256.Create(origin);
            for (; i + 8 <= values.Length; i += 8)
            {
                Vector256.Narrow(Vector256.LoadUnsafe(ref from, (nuint)i) - offset, Vector256.LoadUnsafe(ref from, (nuint)i + 4) - offset)
                    .StoreUnsafe(ref to, (nuint)i);
            }
        }
        else if (Vector128.IsHardwareAccelerated)
        {
            var offset = Vector128.Create(origin);
            for (; i + 4 <= values.Length; i += 4)
            {
                Vector128.Narrow(Vector128.LoadUnsafe(ref from, (nuint)i) - offset, Vector128.LoadUnsafe(ref from, (nuint)i + 2) - offset)
                    .StoreUnsafe(ref to, (nuint)i);
            }
        }

        for (; i < values.Length; i++)
        {
            narrowed[i] = (float)(values[i] - origin);
        }
    }

    private static int Coordinates<T>(ReadOnlySpan<T> x, ReadOnlySpan<T> y, ReadOnlySpan<T> z, Broadcast<Vector256<T>> r,
        Span<T> outX, Span<T> outY, Span<T> outZ) where T : unmanaged
    {
        var lanes = Vector256<T>.Count;
        var i = 0;
        for (; i + lanes <= x.Length; i += lanes)
        {
            var (px, py, pz) = (Vector256.Create(x.Slice(i, lanes)), Vector256.Create(y.Slice(i, lanes)), Vector256.Create(z.Slice(i, lanes)));
            var reciprocal = Vector256<T>.One / (px * r.M14 + py * r.M24 + (pz * r.M34 + r.M44));
            ((px * r.M11 + py * r.M21 + (pz * r.M31 + r.M41)) * reciprocal).CopyTo(outX.Slice(i, lanes));
            ((px * r.M12 + py * r.M22 + (pz * r.M32 + r.M42)) * reciprocal).CopyTo(outY.Slice(i, lanes));
            ((px * r.M13 + py * r.M23 + (pz * r.M33 + r.M43)) * reciprocal).CopyTo(outZ.Slice(i, lanes));
        }

        return i;
    }

    private static int Coordinates<T>(ReadOnlySpan<T> x, ReadOnlySpan<T> y, ReadOnlySpan<T> z, Broadcast<Vector128<T>> r,
        Span<T> outX, Span<T> outY, Span<T> outZ) where T : unmanaged
    {
        var lanes = Vector128<T>.Count;
        var i = 0;
        for (; i + lanes <= x.Length; i += lanes)
        {
            var (px, py, pz) = (Vector128.Create(x.Slice(i, lanes)), Vector128.Create(y.Slice(i, lanes)), Vector128.Create(z.Slice(i, lanes)));
            var reciprocal = Vector128<T>.One / (px * r.M14 + py * r.M24 + (pz * r.M34 + r.M44));
            ((px * r.M11 + py * r.M21 + (pz * r.M31 + r.M41)) * reciprocal).CopyTo(outX.Slice(i, lanes));
            ((px * r.M12 + py * r.M22 + (pz * r.M32 + r.M42)) * reciprocal).CopyTo(outY.Slice(i, lanes));
            ((px * r.M13 + py * r.M23 + (pz * r.M33 + r.M43)) * reciprocal).CopyTo(outZ.Slice(i, lanes));
        }

        return i;
    }

    private static int Normals<T>(ReadOnlySpan<T> x, ReadOnlySpan<T> y, ReadOnlySpan<T> z, Broadcast<Vector256<T>> r,
        Span<T> outX, Span<T> outY, Span<T> outZ) where T : unmanaged
    {
        var lanes = Vector256<T>.Count;
        var i = 0;
        for (; i + lanes <= x.Length; i += lanes)
        {
            var (px, py, pz) = (Vector256.Create(x.Slice(i, lanes)), Vector256.Create(y.Slice(i, lanes)), Vector256.Create(z.Slice(i, lanes)));
            (px * r.M11 + py * r.M21 + pz * r.M31).CopyTo(outX.Slice(i, lanes));
            (px * r.M12 + py * r.M22 + pz * r.M32).CopyTo(outY.Slice(i, lanes));
            (px * r.M13 + py * r.M23 + pz * r.M33).CopyTo(outZ.Slice(i, lanes));
        }

        return i;
    }

    private static int Normals<T>(ReadOnlySpan<T> x, ReadOnlySpan<T> y, ReadOnlySpan<T> z, Broadcast<Vector128<T>> r,
        Span<T> outX, Span<T> outY, Span<T> outZ) where T : unmanaged
    {
        var lanes = Vector128<T>.Count;
        var i = 0;
        for (; i + lanes <= x.Length; i += lanes)
        {
            var (px, py, pz) = (Vector128.Create(x.Slice(i, lanes)), Vector128.Create(y.Slice(i, lanes)), Vector128.Create(z.Slice(i, lanes)));
            (px * r.M11 + py * r.M21 + pz * r.M31).CopyTo(outX.Slice(i, lanes));
            (px * r.M12 + py * r.M22 + pz * r.M32).CopyTo(outY.Slice(i, lanes));
            (px * r.M13 + py * r.M23 + pz * r.M33).CopyTo(outZ.Slice(i, lanes));
        }

        return i;
    }

    private static Broadcast<TVector> Rows<T, TVector>(Matrix4x4<T> m, Func<T, TVector> spread) where T : unmanaged, IFloatingPointIeee754<T> =>
        new(spread(m.M11), spread(m.M12), spread(m.M13), spread(m.M14), spread(m.M21), spread(m.M22), spread(m.M23), spread(m.M24),
            spread(m.M31), spread(m.M32), spread(m.M33), spread(m.M34), spread(m.M41), spread(m.M42), spread(m.M43), spread(m.M44));

    private readonly record struct Broadcast<TVector>(
        TVector M11, TVector M12, TVector M13, TVector M14, TVector M21, TVector M22, TVector M23, TVector M24,
        TVector M31, TVector M32, TVector M33, TVector M34, TVector M41, TVector M42, TVector M43, TVector M44);
}
