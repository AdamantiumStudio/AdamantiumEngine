using System;
using System.Threading.Tasks;

namespace Adamantium.Imaging.Jpeg2000;

/// <summary>The inverse discrete wavelet transform (ITU-T T.800, annex F): each resolution interleaves the image of
/// the one below with its three subbands, then lifts every row and every column, with the reversible 5-3 filter on
/// integers or the irreversible 9-7 filter on reals, over a periodic symmetric extension. Rows, then columns, are
/// lifted in parallel.</summary>
internal static class J2kWavelet
{
    private const int Pad = 8;

    private const float Alpha = -1.586134342059924f;
    private const float Beta = -0.052980118572961f;
    private const float Gamma = 0.882911075530934f;
    private const float Delta = 0.443506852043971f;
    private const float K = 1.230174104914001f;

    /// <summary>The samples of the tile-component, row after row.</summary>
    public static float[] Inverse(J2kTileComponent component)
    {
        var low = component.Resolutions[0];
        var width = low.X1 - low.X0;
        var longest = Math.Max(component.Width, component.Height) + 2 * Pad;
        if (component.Style.Reversible)
        {
            var image = (int[])low.Bands[0].Integers.Clone();
            for (var r = 1; r < component.Resolutions.Length; r++)
            {
                var resolution = component.Resolutions[r];
                image = Interleave(image, width, resolution, band => band.Integers);
                var (u0, v0) = (resolution.X0, resolution.Y0);
                var (w, h) = (resolution.X1 - u0, resolution.Y1 - v0);
                var next = image;
                Parallel.For(0, h, () => new int[longest], (y, _, line) =>
                {
                    Lift53(next, y * w, 1, w, u0, line);
                    return line;
                }, _ => { });
                Parallel.For(0, w, () => new int[longest], (x, _, line) =>
                {
                    Lift53(next, x, w, h, v0, line);
                    return line;
                }, _ => { });
                width = w;
            }

            var samples = new float[image.Length];
            for (var i = 0; i < samples.Length; i++)
            {
                samples[i] = image[i];
            }

            return samples;
        }

        var reals = (float[])low.Bands[0].Values.Clone();
        for (var r = 1; r < component.Resolutions.Length; r++)
        {
            var resolution = component.Resolutions[r];
            reals = Interleave(reals, width, resolution, band => band.Values);
            var (u0, v0) = (resolution.X0, resolution.Y0);
            var (w, h) = (resolution.X1 - u0, resolution.Y1 - v0);
            var next = reals;
            Parallel.For(0, h, () => new float[longest], (y, _, line) =>
            {
                Lift97(next, y * w, 1, w, u0, line);
                return line;
            }, _ => { });
            Parallel.For(0, w, () => new float[longest], (x, _, line) =>
            {
                Lift97(next, x, w, h, v0, line);
                return line;
            }, _ => { });
            width = w;
        }

        return reals;
    }

    private static T[] Interleave<T>(T[] lower, int lowerWidth, J2kResolution resolution, Func<J2kBand, T[]> of)
    {
        var (u0, v0) = (resolution.X0, resolution.Y0);
        var (w, h) = (resolution.X1 - u0, resolution.Y1 - v0);
        var next = new T[(long)w * h];
        var lowX0 = (u0 + 1) >> 1;
        var lowY0 = (v0 + 1) >> 1;
        var (hl, lh, hh) = (resolution.Bands[0], resolution.Bands[1], resolution.Bands[2]);
        var (hlValues, lhValues, hhValues) = (of(hl), of(lh), of(hh));
        for (var y = 0; y < h; y++)
        {
            var vy = v0 + y;
            var half = vy >> 1;
            T[] even, odd;
            int evenRow, oddRow, evenX0, oddX0;
            if ((vy & 1) == 0)
            {
                (even, evenRow, evenX0) = (lower, (half - lowY0) * lowerWidth, lowX0);
                (odd, oddRow, oddX0) = (hlValues, (half - hl.Y0) * hl.Width, hl.X0);
            }
            else
            {
                (even, evenRow, evenX0) = (lhValues, (half - lh.Y0) * lh.Width, lh.X0);
                (odd, oddRow, oddX0) = (hhValues, (half - hh.Y0) * hh.Width, hh.X0);
            }

            var target = y * w;
            for (var x = 0; x < w; x++)
            {
                var ux = u0 + x;
                next[target + x] = (ux & 1) == 0
                    ? even[evenRow + (ux >> 1) - evenX0]
                    : odd[oddRow + (ux >> 1) - oddX0];
            }
        }

        return next;
    }

    private static int Extend(int i, int i0, int i1)
    {
        var period = 2 * (i1 - i0 - 1);
        var m = (i - i0) % period;
        if (m < 0)
        {
            m += period;
        }

        return i0 + Math.Min(m, period - m);
    }

    private static void Lift53(int[] data, int start, int step, int count, int i0, int[] line)
    {
        if (count <= 0)
        {
            return;
        }

        if (count == 1)
        {
            if ((i0 & 1) == 1)
            {
                data[start] /= 2;
            }

            return;
        }

        var i1 = i0 + count;
        var length = count + 2 * Pad;
        for (var i = i0 - Pad; i < i1 + Pad; i++)
        {
            line[i - i0 + Pad] = data[start + (Extend(i, i0, i1) - i0) * step];
        }

        var evenFirst = ((i0 - Pad) & 1) == 0 ? 0 : 1;
        for (var at = evenFirst == 0 ? 2 : 1; at < length - 1; at += 2)
        {
            line[at] -= (line[at - 1] + line[at + 1] + 2) >> 2;
        }

        for (var at = evenFirst == 0 ? 1 : 2; at < length - 1; at += 2)
        {
            line[at] += (line[at - 1] + line[at + 1]) >> 1;
        }

        for (var i = 0; i < count; i++)
        {
            data[start + i * step] = line[i + Pad];
        }
    }

    private static void Lift97(float[] data, int start, int step, int count, int i0, float[] line)
    {
        if (count <= 0)
        {
            return;
        }

        if (count == 1)
        {
            if ((i0 & 1) == 1)
            {
                data[start] /= 2;
            }

            return;
        }

        var i1 = i0 + count;
        var length = count + 2 * Pad;
        for (var i = i0 - Pad; i < i1 + Pad; i++)
        {
            line[i - i0 + Pad] = data[start + (Extend(i, i0, i1) - i0) * step];
        }

        var evenFirst = ((i0 - Pad) & 1) == 0 ? 0 : 1;
        for (var at = evenFirst; at < length; at += 2)
        {
            line[at] *= K;
        }

        for (var at = 1 - evenFirst; at < length; at += 2)
        {
            line[at] *= 1 / K;
        }

        Step(line, evenFirst, length, Delta);
        Step(line, 1 - evenFirst, length, Gamma);
        Step(line, evenFirst, length, Beta);
        Step(line, 1 - evenFirst, length, Alpha);
        for (var i = 0; i < count; i++)
        {
            data[start + i * step] = line[i + Pad];
        }
    }

    private static void Step(float[] line, int first, int length, float weight)
    {
        for (var at = first == 0 ? 2 : 1; at < length - 1; at += 2)
        {
            line[at] -= weight * (line[at - 1] + line[at + 1]);
        }
    }
}
