using System;
using System.Collections.Generic;

namespace Adamantium.Mathematics.Triangulation;

/// <summary>
/// Segments laid in bands across the axis they reach least along, each in the bands it passes - widened by a margin -
/// and swept along the other axis within a band, so the segments that could come within the margin of one another are
/// found among their neighbours, not by trying every pair.
/// </summary>
internal sealed class SegmentBands
{
    private const int BandsPerRoot = 4;

    private readonly double margin;
    private readonly double[] alongLow;
    private readonly double[] alongHigh;
    private readonly double[] acrossLow;
    private readonly double[] acrossHigh;
    private readonly int[] firstBand;
    private readonly List<int>[] bands;

    /// <summary>The bands of <paramref name="segments"/>, each widened by <paramref name="margin"/>.</summary>
    public SegmentBands(IReadOnlyList<(Vector2<double> A, Vector2<double> B)> segments, double margin)
    {
        this.margin = margin;
        var n = segments.Count;
        double minX = double.MaxValue, minY = double.MaxValue, maxX = double.MinValue, maxY = double.MinValue;
        double spanX = 0, spanY = 0;
        foreach (var (a, b) in segments)
        {
            minX = Math.Min(minX, Math.Min(a.X, b.X));
            minY = Math.Min(minY, Math.Min(a.Y, b.Y));
            maxX = Math.Max(maxX, Math.Max(a.X, b.X));
            maxY = Math.Max(maxY, Math.Max(a.Y, b.Y));
            spanX += Math.Abs(b.X - a.X);
            spanY += Math.Abs(b.Y - a.Y);
        }

        var count = Math.Clamp(BandsPerRoot * (int)Math.Sqrt(n), 1, Math.Max(1, n));
        bands = new List<int>[count];
        firstBand = new int[n];
        alongLow = new double[n];
        alongHigh = new double[n];
        acrossLow = new double[n];
        acrossHigh = new double[n];
        if (n == 0)
        {
            return;
        }

        var extentX = Math.Max(maxX - minX, double.Epsilon);
        var extentY = Math.Max(maxY - minY, double.Epsilon);
        var byY = spanY / extentY <= spanX / extentX;
        var low = (byY ? minY : minX) - margin;
        var step = Math.Max(((byY ? extentY : extentX) + 2 * margin) / count, double.Epsilon);
        for (var i = 0; i < n; i++)
        {
            var (a, b) = segments[i];
            var (lowX, highX) = (Math.Min(a.X, b.X), Math.Max(a.X, b.X));
            var (lowY, highY) = (Math.Min(a.Y, b.Y), Math.Max(a.Y, b.Y));
            (alongLow[i], alongHigh[i], acrossLow[i], acrossHigh[i]) = byY ? (lowX, highX, lowY, highY) : (lowY, highY, lowX, highX);
            var first = Math.Clamp((int)Math.Floor((acrossLow[i] - margin - low) / step), 0, count - 1);
            var last = Math.Clamp((int)Math.Floor((acrossHigh[i] + margin - low) / step), 0, count - 1);
            firstBand[i] = first;
            for (var band = first; band <= last; band++)
            {
                (bands[band] ??= []).Add(i);
            }
        }
    }

    /// <summary>Hands <paramref name="visit"/> each pair of segments whose boxes, widened by the margin, overlap -
    /// once, the lower index first - until it answers true; whether it did.</summary>
    public bool Pairs(Func<int, int, bool> visit)
    {
        var reach = 2 * margin;
        var active = new List<int>();
        for (var band = 0; band < bands.Length; band++)
        {
            var members = bands[band];
            if (members == null)
            {
                continue;
            }

            members.Sort((e, f) => alongLow[e].CompareTo(alongLow[f]));
            active.Clear();
            foreach (var e in members)
            {
                var low = alongLow[e] - reach;
                var kept = 0;
                for (var k = 0; k < active.Count; k++)
                {
                    var o = active[k];
                    if (alongHigh[o] < low)
                    {
                        continue;
                    }

                    active[kept++] = o;
                    if (Math.Max(firstBand[e], firstBand[o]) == band &&
                        acrossHigh[e] >= acrossLow[o] - reach && acrossHigh[o] >= acrossLow[e] - reach &&
                        visit(Math.Min(e, o), Math.Max(e, o)))
                    {
                        return true;
                    }
                }

                active.RemoveRange(kept, active.Count - kept);
                active.Add(e);
            }
        }

        return false;
    }
}
