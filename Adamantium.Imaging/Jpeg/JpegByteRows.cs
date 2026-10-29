using System;
using System.Buffers;
using System.Runtime.CompilerServices;
using System.Runtime.InteropServices;
using System.Runtime.Intrinsics;

namespace Adamantium.Imaging.Jpeg;

/// <summary>The rows of an 8-bit gray, YCbCr or RGB picture written straight from its components' samples: a component
/// of the picture's size read in place, a halved one stretched into a row of bytes - smoothly, as
/// <see cref="JpegColor"/> stretches halves - and the three turned to colors sixteen pixels at a time.</summary>
[SkipLocalsInit]
internal static class JpegByteRows
{
    /// <summary>Whether <paramref name="stream"/>'s picture is one these rows are written for: 8 bits, gray, YCbCr or
    /// RGB, each component of the picture's size or halved across, down or both.</summary>
    public static bool CanWrite(JpegStream stream, JpegColorModel model)
    {
        if (stream.Precision != 8 || stream.Preview || model is not (JpegColorModel.Gray or JpegColorModel.YCbCr
                or JpegColorModel.Rgb))
        {
            return false;
        }

        var (maxAcross, maxDown) = MaxFactors(stream);
        foreach (var component in stream.Components)
        {
            var across = maxAcross / component.Horizontal;
            var down = maxDown / component.Vertical;
            if (component.Samples == null || across is not (1 or 2) || down is not (1 or 2)
                || across == 2 && component.Width < 2)
            {
                return false;
            }
        }

        return true;
    }

    /// <summary>Picture rows <paramref name="first"/> to <paramref name="last"/> into <paramref name="output"/>, rows
    /// <paramref name="stride"/> bytes apart: RGBA when <paramref name="rgba"/>, else the model's own channels.</summary>
    [MethodImpl(MethodImplOptions.AggressiveOptimization)]
    public static void Write(JpegStream stream, JpegColorModel model, bool rgba, Span<byte> output, int stride,
        int width, int first, int last)
    {
        var components = stream.Components;
        var (maxAcross, maxDown) = MaxFactors(stream);
        var stretched = new byte[components.Length][];
        var column = ArrayPool<ushort>.Shared.Rent(width + 1);
        for (var c = 0; c < components.Length; c++)
        {
            stretched[c] = ArrayPool<byte>.Shared.Rent(width);
        }

        try
        {
            for (var y = first; y < last; y++)
            {
                var y0 = Row(components[0], maxAcross, maxDown, y, width, stretched[0], column);
                var target = output.Slice(y * stride);
                if (model == JpegColorModel.Gray)
                {
                    WriteGray(y0, rgba, target, width);
                    continue;
                }

                var y1 = Row(components[1], maxAcross, maxDown, y, width, stretched[1], column);
                var y2 = Row(components[2], maxAcross, maxDown, y, width, stretched[2], column);
                WriteColor(y0, y1, y2, model == JpegColorModel.YCbCr, rgba, target, width);
            }
        }
        finally
        {
            foreach (var row in stretched)
            {
                ArrayPool<byte>.Shared.Return(row);
            }

            ArrayPool<ushort>.Shared.Return(column);
        }
    }

    private static (int Across, int Down) MaxFactors(JpegStream stream)
    {
        int across = 1, down = 1;
        foreach (var component in stream.Components)
        {
            across = Math.Max(across, component.Horizontal);
            down = Math.Max(down, component.Vertical);
        }

        return (across, down);
    }

    [MethodImpl(MethodImplOptions.AggressiveOptimization)]
    private static ReadOnlySpan<byte> Row(JpegComponent component, int maxAcross, int maxDown, int y, int width,
        byte[] stretched, ushort[] column)
    {
        var across = maxAcross / component.Horizontal;
        var down = maxDown / component.Vertical;
        var near = Math.Min(y / down, component.Height - 1);
        if (across == 1 && down == 1)
        {
            return component.Samples.AsSpan(near * component.Stride, width);
        }

        var far = down == 1 ? near : Math.Clamp(y % 2 == 0 ? near - 1 : near + 1, 0, component.Height - 1);
        Stretch(component.Samples.AsSpan(near * component.Stride, component.Width),
            component.Samples.AsSpan(far * component.Stride, component.Width), across, down, y % 2 != 0,
            stretched.AsSpan(0, width), column);
        return stretched.AsSpan(0, width);
    }

    [MethodImpl(MethodImplOptions.AggressiveOptimization)]
    private static void Stretch(ReadOnlySpan<byte> near, ReadOnlySpan<byte> far, int across, int down, bool odd,
        Span<byte> row, ushort[] scratch)
    {
        var width = near.Length;
        ref var nearRef = ref MemoryMarshal.GetReference(near);
        ref var farRef = ref MemoryMarshal.GetReference(far);
        ref var rowRef = ref MemoryMarshal.GetReference(row);
        if (across == 1)
        {
            var count = Math.Min(width, row.Length);
            var bias = (ushort)(odd ? 2 : 1);
            var x = 0;
            if (Vector128.IsHardwareAccelerated)
            {
                var biases = Vector128.Create(bias);
                for (; x + 16 <= count; x += 16)
                {
                    var (nearLow, nearHigh) = Vector128.Widen(Vector128.LoadUnsafe(ref nearRef, (nuint)x));
                    var (farLow, farHigh) = Vector128.Widen(Vector128.LoadUnsafe(ref farRef, (nuint)x));
                    var low = (nearLow * 3 + farLow + biases) >>> 2;
                    var high = (nearHigh * 3 + farHigh + biases) >>> 2;
                    Vector128.Narrow(low, high).StoreUnsafe(ref rowRef, (nuint)x);
                }
            }

            for (; x < count; x++)
            {
                Unsafe.Add(ref rowRef, x) = (byte)((3 * Unsafe.Add(ref nearRef, x) + Unsafe.Add(ref farRef, x) + bias) >> 2);
            }

            row[count..].Fill(row[count - 1]);
            return;
        }

        var column = scratch.AsSpan(0, width);
        ref var columnRef = ref MemoryMarshal.GetArrayDataReference(scratch);
        var start = 0;
        if (Vector128.IsHardwareAccelerated)
        {
            for (; start + 16 <= width; start += 16)
            {
                var (nearLow, nearHigh) = Vector128.Widen(Vector128.LoadUnsafe(ref nearRef, (nuint)start));
                if (down == 2)
                {
                    var (farLow, farHigh) = Vector128.Widen(Vector128.LoadUnsafe(ref farRef, (nuint)start));
                    (nearLow, nearHigh) = (nearLow * 3 + farLow, nearHigh * 3 + farHigh);
                }

                nearLow.StoreUnsafe(ref columnRef, (nuint)start);
                nearHigh.StoreUnsafe(ref columnRef, (nuint)(start + 8));
            }
        }

        for (var x = start; x < width; x++)
        {
            column[x] = (ushort)(down == 1 ? near[x] : 3 * near[x] + far[x]);
        }

        var shift = down == 1 ? 2 : 4;
        var even = down == 1 ? 1 : 8;
        var oddBias = down == 1 ? 2 : 7;
        var pairs = Math.Min(width, row.Length / 2);
        row[0] = (byte)((4 * column[0] + even) >> shift);
        if (pairs > 0 && row.Length > 1)
        {
            row[1] = (byte)((3 * column[0] + column[Math.Min(1, width - 1)] + oddBias) >> shift);
        }

        var middle = 1;
        if (Vector128.IsHardwareAccelerated)
        {
            var evens = Vector128.Create((ushort)even);
            var odds = Vector128.Create((ushort)oddBias);
            for (; middle + 8 < pairs - 1; middle += 8)
            {
                var here = Vector128.LoadUnsafe(ref columnRef, (nuint)middle) * 3;
                var left = (here + Vector128.LoadUnsafe(ref columnRef, (nuint)(middle - 1)) + evens) >>> shift;
                var right = (here + Vector128.LoadUnsafe(ref columnRef, (nuint)(middle + 1)) + odds) >>> shift;
                (left | (right << 8)).AsByte().StoreUnsafe(ref rowRef, (nuint)(2 * middle));
            }
        }

        for (var x = middle; x < pairs - 1; x++)
        {
            var here = 3 * column[x];
            row[2 * x] = (byte)((here + column[x - 1] + even) >> shift);
            row[2 * x + 1] = (byte)((here + column[x + 1] + oddBias) >> shift);
        }

        for (var x = Math.Max(1, pairs - 1); x < width && 2 * x < row.Length; x++)
        {
            var here = 3 * column[x];
            row[2 * x] = (byte)((here + column[x - 1] + even) >> shift);
            if (2 * x + 1 < row.Length)
            {
                row[2 * x + 1] = (byte)((here + column[Math.Min(x + 1, width - 1)] + oddBias) >> shift);
            }
        }

        var filled = Math.Min(2 * width, row.Length);
        row[filled..].Fill(row[filled - 1]);
    }

    [MethodImpl(MethodImplOptions.AggressiveOptimization)]
    private static void WriteGray(ReadOnlySpan<byte> gray, bool rgba, Span<byte> output, int width)
    {
        if (!rgba)
        {
            gray[..width].CopyTo(output);
            return;
        }

        ref var from = ref MemoryMarshal.GetReference(gray);
        ref var into = ref MemoryMarshal.GetReference(output);
        var x = 0;
        if (Vector128.IsHardwareAccelerated)
        {
            var opaque = Vector128.Create(unchecked((int)0xFF000000));
            for (; x + 16 <= width; x += 16)
            {
                var (low, high) = Vector128.Widen(Vector128.LoadUnsafe(ref from, (nuint)x));
                var (a, b) = Vector128.Widen(low);
                var (c, d) = Vector128.Widen(high);
                (a.AsInt32() * 0x10101 | opaque).AsByte().StoreUnsafe(ref into, (nuint)(4 * x));
                (b.AsInt32() * 0x10101 | opaque).AsByte().StoreUnsafe(ref into, (nuint)(4 * x + 16));
                (c.AsInt32() * 0x10101 | opaque).AsByte().StoreUnsafe(ref into, (nuint)(4 * x + 32));
                (d.AsInt32() * 0x10101 | opaque).AsByte().StoreUnsafe(ref into, (nuint)(4 * x + 48));
            }
        }

        for (; x < width; x++)
        {
            Unsafe.WriteUnaligned(ref Unsafe.Add(ref into, 4 * x),
                Unsafe.Add(ref from, x) * 0x10101 | unchecked((int)0xFF000000));
        }
    }

    [MethodImpl(MethodImplOptions.AggressiveOptimization)]
    private static void WriteColor(ReadOnlySpan<byte> first, ReadOnlySpan<byte> second, ReadOnlySpan<byte> third,
        bool ycc, bool rgba, Span<byte> output, int width)
    {
        ref var a = ref MemoryMarshal.GetReference(first);
        ref var b = ref MemoryMarshal.GetReference(second);
        ref var c = ref MemoryMarshal.GetReference(third);
        ref var into = ref MemoryMarshal.GetReference(output);
        var step = rgba ? 4 : 3;

        var limit = rgba ? width : width - 2;
        var x = 0;
        if (Vector256.IsHardwareAccelerated)
        {
            for (; x + 16 <= limit; x += 16)
            {
                var (y0, y1) = Widen256(Vector128.LoadUnsafe(ref a, (nuint)x));
                var (b0, b1) = Widen256(Vector128.LoadUnsafe(ref b, (nuint)x));
                var (c0, c1) = Widen256(Vector128.LoadUnsafe(ref c, (nuint)x));
                Store(Pack(y0, b0, c0, ycc), rgba, ref into, x);
                Store(Pack(y1, b1, c1, ycc), rgba, ref into, x + 8);
            }
        }
        else if (Vector128.IsHardwareAccelerated)
        {
            for (; x + 16 <= limit; x += 16)
            {
                var (y0, y1, y2, y3) = Widen128(Vector128.LoadUnsafe(ref a, (nuint)x));
                var (b0, b1, b2, b3) = Widen128(Vector128.LoadUnsafe(ref b, (nuint)x));
                var (c0, c1, c2, c3) = Widen128(Vector128.LoadUnsafe(ref c, (nuint)x));
                Store(Pack(y0, b0, c0, ycc), rgba, ref into, x);
                Store(Pack(y1, b1, c1, ycc), rgba, ref into, x + 4);
                Store(Pack(y2, b2, c2, ycc), rgba, ref into, x + 8);
                Store(Pack(y3, b3, c3, ycc), rgba, ref into, x + 12);
            }
        }

        for (; x < width; x++)
        {
            ref var pixel = ref Unsafe.Add(ref into, x * step);
            int luma = Unsafe.Add(ref a, x), blue = Unsafe.Add(ref b, x), red = Unsafe.Add(ref c, x);
            if (ycc)
            {
                (luma, blue, red) = (Clamp(luma + ((91881 * (red - 128) + 32768) >> 16)),
                    Clamp(luma + ((-22554 * (blue - 128) - 46802 * (red - 128) + 32768) >> 16)),
                    Clamp(luma + ((116130 * (blue - 128) + 32768) >> 16)));
            }

            pixel = (byte)luma;
            Unsafe.Add(ref pixel, 1) = (byte)blue;
            Unsafe.Add(ref pixel, 2) = (byte)red;
            if (rgba)
            {
                Unsafe.Add(ref pixel, 3) = 255;
            }
        }
    }

    private static int Clamp(int value) => Math.Clamp(value, 0, 255);

    private static (Vector256<int>, Vector256<int>) Widen256(Vector128<byte> bytes)
    {
        var shorts = Vector256.WidenLower(bytes.ToVector256Unsafe());
        return (Vector256.WidenLower(shorts).AsInt32(), Vector256.WidenUpper(shorts).AsInt32());
    }

    private static (Vector128<int>, Vector128<int>, Vector128<int>, Vector128<int>) Widen128(Vector128<byte> bytes)
    {
        var (low, high) = Vector128.Widen(bytes);
        var (a, b) = Vector128.Widen(low);
        var (c, d) = Vector128.Widen(high);
        return (a.AsInt32(), b.AsInt32(), c.AsInt32(), d.AsInt32());
    }

    [MethodImpl(MethodImplOptions.AggressiveInlining)]
    private static Vector256<int> Pack(Vector256<int> first, Vector256<int> second, Vector256<int> third, bool ycc)
    {
        if (ycc)
        {
            var blue = second - Vector256.Create(128);
            var red = third - Vector256.Create(128);
            var half = Vector256.Create(32768);
            (first, second, third) = (first + ((red * 91881 + half) >> 16),
                first + ((blue * -22554 - red * 46802 + half) >> 16), first + ((blue * 116130 + half) >> 16));
        }

        var top = Vector256.Create(255);
        first = Vector256.Min(Vector256.Max(first, Vector256<int>.Zero), top);
        second = Vector256.Min(Vector256.Max(second, Vector256<int>.Zero), top);
        third = Vector256.Min(Vector256.Max(third, Vector256<int>.Zero), top);
        return first | (second << 8) | (third << 16) | Vector256.Create(unchecked((int)0xFF000000));
    }

    [MethodImpl(MethodImplOptions.AggressiveInlining)]
    private static Vector128<int> Pack(Vector128<int> first, Vector128<int> second, Vector128<int> third, bool ycc)
    {
        if (ycc)
        {
            var blue = second - Vector128.Create(128);
            var red = third - Vector128.Create(128);
            var half = Vector128.Create(32768);
            (first, second, third) = (first + ((red * 91881 + half) >> 16),
                first + ((blue * -22554 - red * 46802 + half) >> 16), first + ((blue * 116130 + half) >> 16));
        }

        var top = Vector128.Create(255);
        first = Vector128.Min(Vector128.Max(first, Vector128<int>.Zero), top);
        second = Vector128.Min(Vector128.Max(second, Vector128<int>.Zero), top);
        third = Vector128.Min(Vector128.Max(third, Vector128<int>.Zero), top);
        return first | (second << 8) | (third << 16) | Vector128.Create(unchecked((int)0xFF000000));
    }

    [MethodImpl(MethodImplOptions.AggressiveInlining)]
    private static void Store(Vector256<int> pixels, bool rgba, ref byte into, int x)
    {
        if (rgba)
        {
            pixels.AsByte().StoreUnsafe(ref into, (nuint)(4 * x));
            return;
        }

        Store(pixels.GetLower(), false, ref into, x);
        Store(pixels.GetUpper(), false, ref into, x + 4);
    }

    [MethodImpl(MethodImplOptions.AggressiveInlining)]
    private static void Store(Vector128<int> pixels, bool rgba, ref byte into, int x)
    {
        if (rgba)
        {
            pixels.AsByte().StoreUnsafe(ref into, (nuint)(4 * x));
            return;
        }

        Vector128.Shuffle(pixels.AsByte(), Vector128.Create((byte)0, 1, 2, 4, 5, 6, 8, 9, 10, 12, 13, 14, 15, 15, 15, 15))
            .StoreUnsafe(ref into, (nuint)(3 * x));
    }
}
