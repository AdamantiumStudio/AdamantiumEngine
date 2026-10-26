using System;
using System.Collections.Generic;

namespace Adamantium.Imaging;

/// <summary>A picture in a palette: the colors, RGB a byte each, and a palette index for every pixel; the index of
/// transparent pixels, or -1 when there are none.</summary>
public sealed record QuantizedImage(byte[] Palette, int Colors, byte[] Indices, int TransparentIndex);

/// <summary>Reduces RGBA pixels to a palette. A picture of few enough colors keeps them all exactly. Otherwise its
/// colors are cut into boxes one cut at a time - always the cut, across a channel at a value, that lowers their squared
/// error most - the boxes' means refined by k-means over the colors; pixels take the nearest color, with
/// Floyd–Steinberg error diffusion if asked. Pixels of alpha below 128 are transparent and take one index of their own;
/// the rest are taken as opaque.</summary>
public static class ColorQuantizer
{
    private const int Refinements = 3;

    /// <summary>Quantizes the <paramref name="width"/> × <paramref name="height"/> RGBA pixels of
    /// <paramref name="rgba"/> to at most <paramref name="maxColors"/> (2 to 256), the transparent index among
    /// them.</summary>
    public static QuantizedImage Quantize(ReadOnlySpan<byte> rgba, int width, int height, int maxColors = 256,
        bool dither = true)
    {
        if (maxColors is < 2 or > 256)
        {
            throw new ArgumentOutOfRangeException(nameof(maxColors), maxColors, "A palette holds 2 to 256 colors.");
        }

        var count = width * height;
        if (rgba.Length < count * 4)
        {
            throw new ArgumentException($"{width}×{height} RGBA pixels take {count * 4} bytes.", nameof(rgba));
        }

        var transparent = false;
        for (var i = 0; i < count && !transparent; i++)
        {
            transparent = rgba[i * 4 + 3] < 128;
        }

        var opaqueColors = maxColors - (transparent ? 1 : 0);
        var indices = new byte[count];
        var exact = Exact(rgba, count, opaqueColors, indices);
        var palette = exact ?? Cut(Colors(rgba, count), opaqueColors);
        var transparentIndex = transparent ? palette.Length / 3 : -1;
        if (exact == null)
        {
            Map(rgba, width, height, palette, indices, dither);
        }

        if (transparent)
        {
            Array.Resize(ref palette, palette.Length + 3);
            for (var i = 0; i < count; i++)
            {
                if (rgba[i * 4 + 3] < 128)
                {
                    indices[i] = (byte)transparentIndex;
                }
            }
        }

        return new QuantizedImage(palette, palette.Length / 3, indices, transparentIndex);
    }

    // Every opaque color in the palette and every pixel its index, when there are no more colors than fit; else null.
    private static byte[] Exact(ReadOnlySpan<byte> rgba, int count, int limit, byte[] indices)
    {
        var index = new Dictionary<int, byte>();
        var palette = new List<byte>();
        var last = -1;
        byte lastIndex = 0;
        for (var i = 0; i < count; i++)
        {
            if (rgba[i * 4 + 3] < 128)
            {
                continue;
            }

            var key = rgba[i * 4] | rgba[i * 4 + 1] << 8 | rgba[i * 4 + 2] << 16;
            if (key != last)
            {
                if (!index.TryGetValue(key, out lastIndex))
                {
                    if (index.Count == limit)
                    {
                        return null;
                    }

                    lastIndex = (byte)index.Count;
                    index.Add(key, lastIndex);
                    palette.AddRange([rgba[i * 4], rgba[i * 4 + 1], rgba[i * 4 + 2]]);
                }

                last = key;
            }

            indices[i] = lastIndex;
        }

        // A palette has at least one color, for pictures all transparent.
        return palette.Count == 0 ? [0, 0, 0] : palette.ToArray();
    }

    // The opaque colors and how many pixels have each, as keys R | G << 8 | B << 16.
    private static (int[] Keys, int[] Counts) Colors(ReadOnlySpan<byte> rgba, int count)
    {
        var counts = new Dictionary<int, int>();
        var last = -1;
        var lastCount = 0;
        for (var i = 0; i < count; i++)
        {
            if (rgba[i * 4 + 3] < 128)
            {
                continue;
            }

            var key = rgba[i * 4] | rgba[i * 4 + 1] << 8 | rgba[i * 4 + 2] << 16;
            if (key != last)
            {
                if (last >= 0)
                {
                    counts[last] = counts.GetValueOrDefault(last) + lastCount;
                }

                (last, lastCount) = (key, 0);
            }

            lastCount++;
        }

        if (last >= 0)
        {
            counts[last] = counts.GetValueOrDefault(last) + lastCount;
        }

        var keys = new int[counts.Count];
        var weights = new int[counts.Count];
        var at = 0;
        foreach (var (key, weight) in counts)
        {
            (keys[at], weights[at]) = (key, weight);
            at++;
        }

        return (keys, weights);
    }

    // Pixels together: how many, their sums by channel and the sum of their squares.
    private struct Sums
    {
        public long Count, R, G, B;
        public double Squares;

        public void Add(int key, long count)
        {
            long r = key & 255, g = key >> 8 & 255, b = key >> 16;
            Count += count;
            (R, G, B) = (R + r * count, G + g * count, B + b * count);
            Squares += (r * r + g * g + b * b) * (double)count;
        }

        public readonly double Error => Count == 0
            ? 0
            : Squares - ((double)R * R + (double)G * G + (double)B * B) / Count;

        public readonly Sums Without(in Sums part) => new()
        {
            Count = Count - part.Count, R = R - part.R, G = G - part.G, B = B - part.B, Squares = Squares - part.Squares
        };
    }

    // The colors order[Start..End), their sums, and the cut that lowers their error most: across which channel, below
    // which value, and by how much.
    private sealed class Box
    {
        public int Start, End;
        public Sums Total;
        public int Axis = -1;
        public int At;
        public double Gain;
    }

    private static byte[] Cut((int[] Keys, int[] Counts) colors, int limit)
    {
        var order = new int[colors.Keys.Length];
        for (var i = 0; i < order.Length; i++)
        {
            order[i] = i;
        }

        var boxes = new List<Box> { Plan(colors, order, new Box { Start = 0, End = order.Length }) };
        while (boxes.Count < limit)
        {
            Box best = null;
            foreach (var box in boxes)
            {
                if (box.Axis >= 0 && (best == null || box.Gain > best.Gain))
                {
                    best = box;
                }
            }

            if (best == null)
            {
                break;
            }

            // The colors below the value go first, the rest to a box of their own.
            var shift = best.Axis * 8;
            int low = best.Start, high = best.End - 1;
            while (low <= high)
            {
                if ((colors.Keys[order[low]] >> shift & 255) < best.At)
                {
                    low++;
                }
                else
                {
                    (order[low], order[high]) = (order[high], order[low]);
                    high--;
                }
            }

            boxes.Add(Plan(colors, order, new Box { Start = low, End = best.End }));
            best.End = low;
            Plan(colors, order, best);
        }

        var palette = new double[boxes.Count * 3];
        for (var i = 0; i < boxes.Count; i++)
        {
            var total = boxes[i].Total;
            (palette[i * 3], palette[i * 3 + 1], palette[i * 3 + 2]) =
                ((double)total.R / total.Count, (double)total.G / total.Count, (double)total.B / total.Count);
        }

        Refine(colors, palette);
        var result = new byte[palette.Length];
        for (var i = 0; i < palette.Length; i++)
        {
            result[i] = (byte)Math.Clamp(Math.Round(palette[i]), 0, 255);
        }

        return result;
    }

    // The box's sums and its best cut: on each channel the colors counted by value, and every value tried as the one
    // below which they go first.
    private static Box Plan((int[] Keys, int[] Counts) colors, int[] order, Box box)
    {
        var slices = new Sums[3 * 256];
        box.Total = new Sums();
        for (var i = box.Start; i < box.End; i++)
        {
            var key = colors.Keys[order[i]];
            var count = colors.Counts[order[i]];
            box.Total.Add(key, count);
            slices[key & 255].Add(key, count);
            slices[256 + (key >> 8 & 255)].Add(key, count);
            slices[512 + (key >> 16)].Add(key, count);
        }

        var error = box.Total.Error;
        var bestError = double.MaxValue;
        box.Axis = -1;
        for (var axis = 0; axis < 3; axis++)
        {
            var lower = new Sums();
            for (var at = 1; at < 256; at++)
            {
                var slice = slices[axis * 256 + at - 1];
                if (slice.Count == 0)
                {
                    continue;
                }

                (lower.Count, lower.R, lower.G, lower.B, lower.Squares) = (lower.Count + slice.Count, lower.R + slice.R,
                    lower.G + slice.G, lower.B + slice.B, lower.Squares + slice.Squares);
                if (lower.Count == box.Total.Count)
                {
                    break;
                }

                var cut = lower.Error + box.Total.Without(lower).Error;
                if (cut < bestError)
                {
                    (bestError, box.Axis, box.At) = (cut, axis, at);
                }
            }
        }

        box.Gain = box.Axis < 0 ? 0 : error - bestError;
        return box;
    }

    // k-means over the colors: each goes to the nearest palette color, each palette color to their mean.
    private static void Refine((int[] Keys, int[] Counts) colors, double[] palette)
    {
        var entries = palette.Length / 3;
        var sums = new double[entries * 4];
        for (var round = 0; round < Refinements; round++)
        {
            Array.Clear(sums);
            var search = new NearestColor(palette);
            for (var i = 0; i < colors.Keys.Length; i++)
            {
                var key = colors.Keys[i];
                double count = colors.Counts[i];
                int r = key & 255, g = key >> 8 & 255, b = key >> 16;
                var nearest = search.Of(r, g, b);
                sums[nearest * 4] += r * count;
                sums[nearest * 4 + 1] += g * count;
                sums[nearest * 4 + 2] += b * count;
                sums[nearest * 4 + 3] += count;
            }

            for (var i = 0; i < entries; i++)
            {
                if (sums[i * 4 + 3] > 0)
                {
                    for (var c = 0; c < 3; c++)
                    {
                        palette[i * 3 + c] = sums[i * 4 + c] / sums[i * 4 + 3];
                    }
                }
            }
        }
    }

    // The nearest palette color to a color: the palette sorted by red, looked through outward from the color's red, and
    // no further on a side once red alone is farther than the nearest found.
    private sealed class NearestColor
    {
        private readonly double[] _red;
        private readonly double[] _green;
        private readonly double[] _blue;
        private readonly int[] _index;

        public NearestColor(double[] palette)
        {
            var count = palette.Length / 3;
            _index = new int[count];
            for (var i = 0; i < count; i++)
            {
                _index[i] = i;
            }

            Array.Sort(_index, (x, y) => palette[x * 3].CompareTo(palette[y * 3]));
            (_red, _green, _blue) = (new double[count], new double[count], new double[count]);
            for (var i = 0; i < count; i++)
            {
                (_red[i], _green[i], _blue[i]) =
                    (palette[_index[i] * 3], palette[_index[i] * 3 + 1], palette[_index[i] * 3 + 2]);
            }
        }

        public int Of(double r, double g, double b)
        {
            var start = Array.BinarySearch(_red, r);
            start = start < 0 ? ~start : start;
            var best = -1;
            var bestDistance = double.MaxValue;
            int down = start - 1, up = start;
            while (down >= 0 || up < _red.Length)
            {
                if (up < _red.Length)
                {
                    var dr = _red[up] - r;
                    if (dr * dr >= bestDistance)
                    {
                        up = _red.Length;
                    }
                    else
                    {
                        Try(up++, dr, g, b, ref best, ref bestDistance);
                    }
                }

                if (down >= 0)
                {
                    var dr = _red[down] - r;
                    if (dr * dr >= bestDistance)
                    {
                        down = -1;
                    }
                    else
                    {
                        Try(down--, dr, g, b, ref best, ref bestDistance);
                    }
                }
            }

            return _index[best];
        }

        private void Try(int at, double dr, double g, double b, ref int best, ref double bestDistance)
        {
            double dg = _green[at] - g, db = _blue[at] - b;
            var distance = dr * dr + dg * dg + db * db;
            if (distance < bestDistance)
            {
                (best, bestDistance) = (at, distance);
            }
        }
    }

    // Each opaque pixel's nearest color, with or without its error spread to the pixels after it - rows walked to and
    // fro so the error does not drift one way.
    private static void Map(ReadOnlySpan<byte> rgba, int width, int height, byte[] palette, byte[] indices,
        bool dither)
    {
        // The nearest color, by color.
        var nearest = new Dictionary<int, byte>();
        var search = new NearestColor(Array.ConvertAll(palette, value => (double)value));
        var error = dither ? new int[2, (width + 2) * 3] : null;
        for (var y = 0; y < height; y++)
        {
            var forward = (y & 1) == 0;
            int current = y & 1, next = current ^ 1;
            if (dither)
            {
                for (var i = 0; i < (width + 2) * 3; i++)
                {
                    error[next, i] = 0;
                }
            }

            for (var step = 0; step < width; step++)
            {
                var x = forward ? step : width - 1 - step;
                var at = y * width + x;
                if (rgba[at * 4 + 3] < 128)
                {
                    continue;
                }

                int r = rgba[at * 4], g = rgba[at * 4 + 1], b = rgba[at * 4 + 2];
                // The error carried here, in sixteenths.
                var e = (x + 1) * 3;
                if (dither)
                {
                    r = Math.Clamp(r + (error[current, e] + 8 >> 4), 0, 255);
                    g = Math.Clamp(g + (error[current, e + 1] + 8 >> 4), 0, 255);
                    b = Math.Clamp(b + (error[current, e + 2] + 8 >> 4), 0, 255);
                }

                var key = r | g << 8 | b << 16;
                if (!nearest.TryGetValue(key, out var chosen))
                {
                    chosen = (byte)search.Of(r, g, b);
                    nearest.Add(key, chosen);
                }

                indices[at] = chosen;
                if (!dither)
                {
                    continue;
                }

                Span<int> rest = [r - palette[chosen * 3], g - palette[chosen * 3 + 1], b - palette[chosen * 3 + 2]];
                // 7/16 ahead, 3/16 behind below, 5/16 below, 1/16 ahead below.
                var ahead = forward ? 3 : -3;
                for (var c = 0; c < 3; c++)
                {
                    error[current, e + ahead + c] += rest[c] * 7;
                    error[next, e - ahead + c] += rest[c] * 3;
                    error[next, e + c] += rest[c] * 5;
                    error[next, e + ahead + c] += rest[c];
                }
            }
        }
    }
}
