using System;
using System.Buffers;
using System.Buffers.Binary;
using System.Runtime.CompilerServices;
using System.Runtime.InteropServices;
using System.Runtime.Intrinsics;
using System.Threading.Tasks;

namespace Adamantium.Imaging.Jpeg;

/// <summary>What a JPEG's components are.</summary>
internal enum JpegColorModel
{
    Gray,
    Rgb,
    YCbCr,
    Cmyk,
    Ycck
}

/// <summary>A decoded JPEG's components brought to the picture's size and to colors, row by row: subsampled components
/// stretched - halves smoothly, between their samples' centers, as JFIF sites them (T.871), other factors by
/// repeating - and YCbCr turned to RGB, YCCK to CMYK (Adobe).</summary>
internal static class JpegColor
{
    private const int RowsAtOnce = 16;

    // Pictures smaller than this are colored on the calling thread: a tile of a TIFF is, and its reader already
    // decodes tiles side by side.
    private const long ParallelPixels = 1 << 18;

    // T.871's YCbCr to RGB for 8-bit samples, in 16-bit fixed point, by chroma value: 1.402 Cr, 1.772 Cb, and
    // 0.344136 Cb + 0.714136 Cr for green.
    private static readonly int[] RedOfCr = Table(91881, 32768);
    private static readonly int[] BlueOfCb = Table(116130, 32768);
    private static readonly int[] GreenOfCb = Table(-22554, 0);
    private static readonly int[] GreenOfCr = Table(-46802, 32768);

    private static int[] Table(int factor, int rounding)
    {
        var table = new int[256];
        for (var i = 0; i < 256; i++)
        {
            table[i] = factor * (i - 128) + rounding;
        }

        return table;
    }

    /// <summary>How <paramref name="stream"/>'s components are colored: Adobe's transform when it is given, then
    /// JFIF, then the components' names; three are RGB as they are when <paramref name="rgb"/>.</summary>
    public static JpegColorModel ModelOf(JpegStream stream, bool rgb)
    {
        var components = stream.Components;
        switch (components.Length)
        {
            case 1:
                return JpegColorModel.Gray;
            case 3:
                if (rgb || stream.AdobeTransform == 0)
                {
                    return JpegColorModel.Rgb;
                }

                if (stream.AdobeTransform < 0 && !stream.Jfif && components[0].Id == 'R' && components[1].Id == 'G'
                    && components[2].Id == 'B')
                {
                    return JpegColorModel.Rgb;
                }

                return JpegColorModel.YCbCr;
            case 4:
                return stream.AdobeTransform == 2 ? JpegColorModel.Ycck : JpegColorModel.Cmyk;
            default:
                throw new NotSupportedException($"JPEG of {components.Length} components is not read.");
        }
    }

    /// <summary>The picture's size: the frame's, or an eighth of it, rounded up, for the eighth-scale picture.</summary>
    public static (int Width, int Height) SizeOf(JpegStream stream) => stream.Preview
        ? ((stream.Width + 7) / 8, (stream.Height + 7) / 8)
        : (stream.Width, stream.Height);

    /// <summary>Writes the picture into <paramref name="output"/>, rows <paramref name="stride"/> bytes apart: RGBA
    /// when <paramref name="rgba"/> - 8 bits a channel, or 16 for 12-bit samples - else the model's own channels (gray,
    /// RGB, or CMYK for CMYK and YCCK) as samples of 1 byte, or 2 little-endian for 12 bits. Rows past
    /// <paramref name="rows"/> are left out.</summary>
    public static void Write(JpegStream stream, JpegColorModel model, bool rgba, Span<byte> output, int stride,
        int rows)
    {
        var (width, height) = SizeOf(stream);
        height = Math.Min(height, rows);
        if (height <= 0)
        {
            return;
        }

        var rowBytes = (long)width * (stream.Precision > 8 ? 2 : 1) * (rgba ? 4 : model switch
        {
            JpegColorModel.Gray => 1,
            JpegColorModel.Rgb or JpegColorModel.YCbCr => 3,
            _ => 4
        });
        if (stride < rowBytes || (long)stride * (height - 1) + rowBytes > output.Length)
        {
            throw new ArgumentException("The output is too small for the picture.", nameof(output));
        }

        if ((long)width * height < ParallelPixels)
        {
            Rows(stream, model, rgba, output, stride, (int)rowBytes, width, 0, height);
            return;
        }

        unsafe
        {
            fixed (byte* pointer = output)
            {
                var address = (nint)pointer;
                var length = output.Length;
                Parallel.For(0, (height + RowsAtOnce - 1) / RowsAtOnce, band =>
                    Rows(stream, model, rgba, new Span<byte>((byte*)address, length), stride, (int)rowBytes, width,
                        band * RowsAtOnce, Math.Min(height, (band + 1) * RowsAtOnce)));
            }
        }
    }

    [MethodImpl(MethodImplOptions.AggressiveOptimization)]
    private static void Rows(JpegStream stream, JpegColorModel model, bool rgba, Span<byte> output, int stride,
        int rowBytes, int width, int first, int last)
    {
        if (JpegByteRows.CanWrite(stream, model))
        {
            JpegByteRows.Write(stream, model, rgba, output, stride, width, first, last);
            return;
        }

        var lines = new int[stream.Components.Length][];
        for (var c = 0; c < lines.Length; c++)
        {
            lines[c] = ArrayPool<int>.Shared.Rent(width);
        }

        var scratch = ArrayPool<int>.Shared.Rent(width + 1);
        try
        {
            for (var y = first; y < last; y++)
            {
                for (var c = 0; c < lines.Length; c++)
                {
                    Stretch(stream, stream.Components[c], y, lines[c].AsSpan(0, width), scratch);
                }

                Convert(lines, width, model, stream.Precision, stream.AdobeTransform >= 0, rgba,
                    output.Slice(y * stride, rowBytes));
            }
        }
        finally
        {
            foreach (var line in lines)
            {
                ArrayPool<int>.Shared.Return(line);
            }

            ArrayPool<int>.Shared.Return(scratch);
        }
    }

    private static int Max(JpegStream stream, bool horizontal)
    {
        var max = 1;
        foreach (var component in stream.Components)
        {
            max = Math.Max(max, horizontal ? component.Horizontal : component.Vertical);
        }

        return max;
    }

    private static int Sample(JpegComponent component, int x, int y) => component.Samples != null
        ? component.Samples[y * component.Stride + x]
        : component.Wide[y * component.Stride + x];

    // Row y of the picture from component's samples, each a sample value.
    [MethodImpl(MethodImplOptions.AggressiveOptimization)]
    private static void Stretch(JpegStream stream, JpegComponent component, int y, Span<int> row, Span<int> scratch)
    {
        var across = Max(stream, true) / component.Horizontal;
        var down = Max(stream, false) / component.Vertical;
        var width = stream.Preview ? component.BlocksAcross : component.Width;
        var height = stream.Preview ? component.BlocksDown : component.Height;
        var cy = Math.Min(y / down, height - 1);
        if ((across, down) == (1, 1) && component.Samples != null && width >= row.Length)
        {
            var samples = component.Samples.AsSpan(cy * component.Stride, row.Length);
            var x = 0;
            if (Vector128.IsHardwareAccelerated)
            {
                ref var from = ref MemoryMarshal.GetReference(samples);
                ref var to = ref MemoryMarshal.GetReference(row);
                for (; x + 16 <= row.Length; x += 16)
                {
                    var (a, b, c, d) = Widen(Vector128.LoadUnsafe(ref from, (nuint)x));
                    a.StoreUnsafe(ref to, (nuint)x);
                    b.StoreUnsafe(ref to, (nuint)(x + 4));
                    c.StoreUnsafe(ref to, (nuint)(x + 8));
                    d.StoreUnsafe(ref to, (nuint)(x + 12));
                }
            }

            for (; x < row.Length; x++)
            {
                row[x] = samples[x];
            }

            return;
        }

        if (component.Samples != null && across is 1 or 2 && down is 1 or 2 && width >= 2)
        {
            var far = down == 1 ? cy : Math.Clamp(y % 2 == 0 ? cy - 1 : cy + 1, 0, height - 1);
            Smooth(component.Samples.AsSpan(cy * component.Stride, width),
                component.Samples.AsSpan(far * component.Stride, width), across, down, y % 2 != 0, row, scratch);
            return;
        }

        if (across is 1 or 2 && down is 1 or 2 && (across, down) != (1, 1))
        {
            // Smoothly, halves only: a sample stands a quarter of the way between two of the component's.
            var near = cy;
            var far = down == 1 ? cy : Math.Clamp(y % 2 == 0 ? cy - 1 : cy + 1, 0, height - 1);
            var lastX = width - 1;
            for (var cx = 0; cx <= lastX && cx * across < row.Length; cx++)
            {
                var here = Vertical(component, cx, near, far, down);
                if (across == 1)
                {
                    row[cx] = down == 1 ? here : (here + (y % 2 == 0 ? 1 : 2)) >> 2;
                    continue;
                }

                var left = Vertical(component, Math.Max(cx - 1, 0), near, far, down);
                var right = Vertical(component, Math.Min(cx + 1, lastX), near, far, down);
                var scale = down == 1 ? 2 : 4;
                var bias = down == 1 ? 1 : 8;
                row[2 * cx] = (3 * here + left + bias) >> scale;
                if (2 * cx + 1 < row.Length)
                {
                    row[2 * cx + 1] = (3 * here + right + bias + (down == 1 ? 1 : -1)) >> scale;
                }
            }

            for (var x = width * across; x < row.Length; x++)
            {
                row[x] = row[width * across - 1];
            }

            return;
        }

        for (var x = 0; x < row.Length; x++)
        {
            row[x] = Sample(component, Math.Min(x / across, width - 1), cy);
        }
    }

    // The 8-bit gray, YCbCr and RGB rows straight to bytes; false for the rest.
    [MethodImpl(MethodImplOptions.AggressiveOptimization)]
    private static bool Fast(int[][] rows, int width, JpegColorModel model, bool rgba, Span<byte> output)
    {
        switch (model)
        {
            case JpegColorModel.Gray when rgba:
            {
                var gray = rows[0];
                for (var x = 0; x < width; x++)
                {
                    var g = (byte)gray[x];
                    output[4 * x] = output[4 * x + 1] = output[4 * x + 2] = g;
                    output[4 * x + 3] = 255;
                }

                return true;
            }
            case JpegColorModel.Gray:
            {
                var gray = rows[0];
                for (var x = 0; x < width; x++)
                {
                    output[x] = (byte)gray[x];
                }

                return true;
            }
            case JpegColorModel.YCbCr or JpegColorModel.Rgb:
            {
                var step = rgba ? 4 : 3;
                if (output.Length < width * step || rows[0].Length < width || rows[1].Length < width
                    || rows[2].Length < width)
                {
                    throw new ArgumentException("A row is shorter than the picture.");
                }

                ref var into = ref MemoryMarshal.GetReference(output);
                ref var first = ref MemoryMarshal.GetArrayDataReference(rows[0]);
                ref var second = ref MemoryMarshal.GetArrayDataReference(rows[1]);
                ref var third = ref MemoryMarshal.GetArrayDataReference(rows[2]);
                ref var red = ref MemoryMarshal.GetArrayDataReference(RedOfCr);
                ref var blue = ref MemoryMarshal.GetArrayDataReference(BlueOfCb);
                ref var greenOfCb = ref MemoryMarshal.GetArrayDataReference(GreenOfCb);
                ref var greenOfCr = ref MemoryMarshal.GetArrayDataReference(GreenOfCr);
                var ycc = model == JpegColorModel.YCbCr;

                // Four pixels at a time: the channels computed side by side, packed one int a pixel, and written as
                // four bytes - for RGB overlapping, the fourth byte the next pixel's first; the last pixel alone.
                var start = 0;
                if (Vector128.IsHardwareAccelerated)
                {
                    var limit = rgba ? width - 3 : width - 4;
                    for (; start < limit; start += 4)
                    {
                        var a = Vector128.LoadUnsafe(ref first, (nuint)start);
                        var b = Vector128.LoadUnsafe(ref second, (nuint)start);
                        var c = Vector128.LoadUnsafe(ref third, (nuint)start);
                        if (ycc)
                        {
                            var cb = b - Vector128.Create(128);
                            var cr = c - Vector128.Create(128);
                            var half = Vector128.Create(32768);
                            (a, b, c) = (a + Vector128.ShiftRightArithmetic(cr * 91881 + half, 16),
                                a + Vector128.ShiftRightArithmetic(cb * -22554 - cr * 46802 + half, 16),
                                a + Vector128.ShiftRightArithmetic(cb * 116130 + half, 16));
                        }

                        var top = Vector128.Create(255);
                        a = Vector128.Min(Vector128.Max(a, Vector128<int>.Zero), top);
                        b = Vector128.Min(Vector128.Max(b, Vector128<int>.Zero), top);
                        c = Vector128.Min(Vector128.Max(c, Vector128<int>.Zero), top);
                        var packed = a | (b << 8) | (c << 16) | Vector128.Create(unchecked((int)0xFF000000));
                        if (rgba)
                        {
                            packed.AsByte().StoreUnsafe(ref into, (nuint)(start * 4));
                            continue;
                        }

                        for (var i = 0; i < 4; i++)
                        {
                            Unsafe.WriteUnaligned(ref Unsafe.Add(ref into, (start + i) * 3), packed.GetElement(i));
                        }
                    }
                }

                for (var x = start; x < width; x++)
                {
                    ref var pixel = ref Unsafe.Add(ref into, x * step);
                    int a = Unsafe.Add(ref first, x), b = Unsafe.Add(ref second, x), c = Unsafe.Add(ref third, x);
                    if (ycc)
                    {
                        // Samples are 0 to 255, so the tables are indexed safely.
                        pixel = Clamp(a + (Unsafe.Add(ref red, c) >> 16));
                        Unsafe.Add(ref pixel, 1) =
                            Clamp(a + ((Unsafe.Add(ref greenOfCb, b) + Unsafe.Add(ref greenOfCr, c)) >> 16));
                        Unsafe.Add(ref pixel, 2) = Clamp(a + (Unsafe.Add(ref blue, b) >> 16));
                    }
                    else
                    {
                        pixel = (byte)a;
                        Unsafe.Add(ref pixel, 1) = (byte)b;
                        Unsafe.Add(ref pixel, 2) = (byte)c;
                    }

                    if (rgba)
                    {
                        Unsafe.Add(ref pixel, 3) = 255;
                    }
                }

                return true;
            }
            default:
                return false;
        }
    }

    private static byte Clamp(int value) => (byte)Math.Clamp(value, 0, 255);

    // The halves of 8-bit samples: near, the component's row nearest the picture's, and far, the one beyond it -
    // three parts to one in each direction stretched; scratch holds the vertical step's columns.
    [MethodImpl(MethodImplOptions.AggressiveOptimization)]
    private static void Smooth(ReadOnlySpan<byte> near, ReadOnlySpan<byte> far, int across, int down, bool odd,
        Span<int> row, Span<int> scratch)
    {
        var width = near.Length;
        if (across == 1)
        {
            var bias = odd ? 2 : 1;
            var count = Math.Min(width, row.Length);
            for (var x = 0; x < count; x++)
            {
                row[x] = (3 * near[x] + far[x] + bias) >> 2;
            }

            row[count..].Fill(row[count - 1]);
            return;
        }

        var column = scratch[..width];
        var weight = down == 1 ? 0 : 3;
        var start = 0;
        if (Vector128.IsHardwareAccelerated)
        {
            ref var nearRef = ref MemoryMarshal.GetReference(near);
            ref var farRef = ref MemoryMarshal.GetReference(far);
            ref var columnRef = ref MemoryMarshal.GetReference(column);
            for (; start + 16 <= width; start += 16)
            {
                var (n0, n1, n2, n3) = Widen(Vector128.LoadUnsafe(ref nearRef, (nuint)start));
                if (weight != 0)
                {
                    var (f0, f1, f2, f3) = Widen(Vector128.LoadUnsafe(ref farRef, (nuint)start));
                    (n0, n1, n2, n3) = (n0 * 3 + f0, n1 * 3 + f1, n2 * 3 + f2, n3 * 3 + f3);
                }

                n0.StoreUnsafe(ref columnRef, (nuint)start);
                n1.StoreUnsafe(ref columnRef, (nuint)(start + 4));
                n2.StoreUnsafe(ref columnRef, (nuint)(start + 8));
                n3.StoreUnsafe(ref columnRef, (nuint)(start + 12));
            }
        }

        for (var x = start; x < width; x++)
        {
            column[x] = weight == 0 ? near[x] : weight * near[x] + far[x];
        }

        var shift = down == 1 ? 2 : 4;
        var even = down == 1 ? 1 : 8;
        var oddBias = down == 1 ? 2 : 7;
        var pairs = Math.Min(width, row.Length / 2);
        row[0] = (4 * column[0] + even) >> shift;
        if (pairs > 0)
        {
            row[1] = (3 * column[0] + column[Math.Min(1, width - 1)] + oddBias) >> shift;
        }

        // Four columns at a time, their neighbours by loads one off; the halves written in turn.
        var middle = 1;
        if (Vector128.IsHardwareAccelerated && pairs > 6)
        {
            ref var columnRef = ref MemoryMarshal.GetReference(column);
            ref var rowRef = ref MemoryMarshal.GetReference(row);
            var evenBias = Vector128.Create(even);
            var odds = Vector128.Create(oddBias);
            for (; middle + 4 < pairs - 1; middle += 4)
            {
                var here = Vector128.LoadUnsafe(ref columnRef, (nuint)middle) * 3;
                var left = Vector128.ShiftRightArithmetic(
                    here + Vector128.LoadUnsafe(ref columnRef, (nuint)(middle - 1)) + evenBias, shift);
                var right = Vector128.ShiftRightArithmetic(
                    here + Vector128.LoadUnsafe(ref columnRef, (nuint)(middle + 1)) + odds, shift);
                for (var i = 0; i < 4; i++)
                {
                    Unsafe.Add(ref rowRef, 2 * (middle + i)) = left.GetElement(i);
                    Unsafe.Add(ref rowRef, 2 * (middle + i) + 1) = right.GetElement(i);
                }
            }
        }

        for (var x = middle; x < pairs - 1; x++)
        {
            var here = 3 * column[x];
            row[2 * x] = (here + column[x - 1] + even) >> shift;
            row[2 * x + 1] = (here + column[x + 1] + oddBias) >> shift;
        }

        for (var x = Math.Max(1, pairs - 1); x < width && 2 * x < row.Length; x++)
        {
            var here = 3 * column[x];
            row[2 * x] = (here + column[x - 1] + even) >> shift;
            if (2 * x + 1 < row.Length)
            {
                row[2 * x + 1] = (here + column[Math.Min(x + 1, width - 1)] + oddBias) >> shift;
            }
        }

        var filled = Math.Min(2 * width, row.Length);
        row[filled..].Fill(row[filled - 1]);
    }

    private static (Vector128<int>, Vector128<int>, Vector128<int>, Vector128<int>) Widen(Vector128<byte> bytes)
    {
        var (low, high) = Vector128.Widen(bytes);
        var (a, b) = Vector128.Widen(low);
        var (c, d) = Vector128.Widen(high);
        return (a.AsInt32(), b.AsInt32(), c.AsInt32(), d.AsInt32());
    }

    // A column of the component at the picture's row: its own sample, or for halved rows three parts it and one
    // the row beyond - in quarters, to be divided once with the horizontal step.
    private static int Vertical(JpegComponent component, int x, int near, int far, int down) => down == 1
        ? Sample(component, x, near)
        : 3 * Sample(component, x, near) + Sample(component, x, far);

    [MethodImpl(MethodImplOptions.AggressiveOptimization)]
    private static void Convert(int[][] rows, int width, JpegColorModel model, int precision, bool adobe, bool rgba,
        Span<byte> output)
    {
        if (precision == 8 && Fast(rows, width, model, rgba, output))
        {
            return;
        }

        var top = (1 << precision) - 1;
        var middle = 1 << (precision - 1);
        var wide = precision > 8;
        var channels = rgba ? 4 : model switch
        {
            JpegColorModel.Gray => 1,
            JpegColorModel.Rgb or JpegColorModel.YCbCr => 3,
            _ => 4
        };
        Span<int> pixel = stackalloc int[4];
        for (var x = 0; x < width; x++)
        {
            switch (model)
            {
                case JpegColorModel.Gray:
                    pixel[0] = pixel[1] = pixel[2] = rows[0][x];
                    break;
                case JpegColorModel.Rgb:
                    (pixel[0], pixel[1], pixel[2]) = (rows[0][x], rows[1][x], rows[2][x]);
                    break;
                default:
                {
                    int luma = rows[0][x], blue = rows[1][x] - middle, red = rows[2][x] - middle;
                    if (model is JpegColorModel.Cmyk)
                    {
                        (pixel[0], pixel[1], pixel[2]) = (rows[0][x], rows[1][x], rows[2][x]);
                    }
                    else
                    {
                        // T.871: R = Y + 1.402 Cr, G = Y - 0.344136 Cb - 0.714136 Cr, B = Y + 1.772 Cb, in 16-bit
                        // fixed point.
                        pixel[0] = Math.Clamp(luma + ((91881 * red + 32768) >> 16), 0, top);
                        pixel[1] = Math.Clamp(luma + ((-22554 * blue - 46802 * red + 32768) >> 16), 0, top);
                        pixel[2] = Math.Clamp(luma + ((116130 * blue + 32768) >> 16), 0, top);
                        if (model == JpegColorModel.Ycck)
                        {
                            (pixel[0], pixel[1], pixel[2]) = (top - pixel[0], top - pixel[1], top - pixel[2]);
                        }
                    }

                    pixel[3] = model is JpegColorModel.YCbCr ? top : rows[3][x];
                    break;
                }
            }

            if (model == JpegColorModel.Gray || model is JpegColorModel.Rgb or JpegColorModel.YCbCr)
            {
                pixel[3] = top;
            }

            if (rgba && model is JpegColorModel.Cmyk or JpegColorModel.Ycck)
            {
                // Adobe's files keep the inks inverted - 0 is full ink; others do not.
                for (var i = 0; i < 3; i++)
                {
                    var ink = adobe ? pixel[i] : top - pixel[i];
                    var black = adobe ? pixel[3] : top - pixel[3];
                    pixel[i] = (ink * black + top / 2) / top;
                }

                pixel[3] = top;
            }

            for (var i = 0; i < channels; i++)
            {
                var value = model == JpegColorModel.Gray && !rgba ? pixel[0] : pixel[i];
                if (!wide)
                {
                    output[x * channels + i] = (byte)value;
                }
                else if (rgba)
                {
                    BinaryPrimitives.WriteUInt16LittleEndian(output[((x * channels + i) * 2)..],
                        (ushort)((value << 4) | (value >> 8)));
                }
                else
                {
                    BinaryPrimitives.WriteUInt16LittleEndian(output[((x * channels + i) * 2)..], (ushort)value);
                }
            }
        }
    }
}
