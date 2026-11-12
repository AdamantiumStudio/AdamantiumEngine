using System;
using System.Collections.Generic;

namespace Adamantium.Mathematics.Triangulation;

/// <summary>
/// A line swept across x over segments, holding the segments it passes in order up it. Two segments that meet are next
/// to each other in that order somewhere before the first place any two meet, so only segments coming next to each
/// other - as one joins the line, or one between them leaves it - are tried: if none of those meet, none do.
/// </summary>
internal sealed class ContactSweep
{
    private readonly IReadOnlyList<(Vector2<double> Left, Vector2<double> Right)> segments;
    private readonly double margin;
    private readonly List<int> line = [];
    private double x;

    /// <summary>A sweep over <paramref name="segments"/>, each its leftmost end - the lower of two at one x - first. A
    /// segment joins the line <paramref name="margin"/> before its left end and leaves it that far past its right one,
    /// and segments within the margin of a segment running straight up are tried against it, so segments that near
    /// meet the line together.</summary>
    public ContactSweep(IReadOnlyList<(Vector2<double> Left, Vector2<double> Right)> segments, double margin)
    {
        this.segments = segments;
        this.margin = margin;
    }

    /// <summary>The segment from <paramref name="a"/> to <paramref name="b"/>, its leftmost end first.</summary>
    public static (Vector2<double> Left, Vector2<double> Right) Ordered(Vector2<double> a, Vector2<double> b) =>
        a.X < b.X || (a.X == b.X && a.Y < b.Y) ? (a, b) : (b, a);

    /// <summary>The segments on the line, in order up it; read only.</summary>
    public IReadOnlyList<int> Line => line;

    /// <summary>Where on the line <paramref name="segment"/> - one that has just joined it - is, or -1.</summary>
    public int Position(int segment) => Find(segment, leftOfX: false);

    /// <summary>Hands <paramref name="meet"/> each pair of segments as they come next to each other, until it answers
    /// true; whether it did. With <paramref name="passCrossings"/> two segments next to each other that cross trade
    /// places on the line where they cross, so the sweep goes on in order past them and finds the crossings beyond.
    /// <paramref name="joined"/>, when given, hears of each segment as it joins the line, with its place there;
    /// <paramref name="passed"/> of each point once every segment ending or beginning there has left or joined.
    /// <paramref name="mayPass"/>, when given, says which crossing pairs trade places.</summary>
    public bool Run(Func<int, int, bool> meet, bool passCrossings = false, Action<int, int> joined = null,
        Action<Vector2<double>> passed = null, Func<int, int, bool> mayPass = null)
    {
        var events = new List<(Vector2<double> At, bool Leaves, int Segment)>(segments.Count * 2);
        for (var i = 0; i < segments.Count; i++)
        {
            var (left, right) = segments[i];
            events.Add((new Vector2<double>(left.X - margin, left.Y), false, i));
            events.Add((new Vector2<double>(right.X + margin, right.Y), true, i));
        }

        events.Sort((e, f) =>
        {
            if (e.At.X != f.At.X) return e.At.X.CompareTo(f.At.X);
            if (e.At.Y != f.At.Y) return e.At.Y.CompareTo(f.At.Y);
            return f.Leaves.CompareTo(e.Leaves);
        });

        var crossings = new PriorityQueue<(int Below, int Above), double>();
        var swapped = new HashSet<(int, int)>();
        bool Next(int below, int above)
        {
            if (meet(below, above)) return true;
            if (passCrossings && CrossingX(below, above) is var at && at >= x && !swapped.Contains((below, above)) &&
                (mayPass == null || mayPass(below, above)))
            {
                crossings.Enqueue((below, above), at);
            }

            return false;
        }

        for (var index = 0; index < events.Count; index++)
        {
            var (at, leaves, s) = events[index];
            while (crossings.TryPeek(out var pair, out var crossX) && crossX <= at.X)
            {
                crossings.Dequeue();
                x = crossX;
                var k = Find(pair.Below);
                if (k < 0 || k + 1 >= line.Count || line[k + 1] != pair.Above || !swapped.Add(pair)) continue;
                (line[k], line[k + 1]) = (pair.Above, pair.Below);
                if ((k > 0 && Next(line[k - 1], line[k])) || (k + 2 < line.Count && Next(line[k + 1], line[k + 2]))) return true;
            }

            x = at.X;
            if (!leaves)
            {
                var k = Place(s);
                line.Insert(k, s);
                joined?.Invoke(s, k);
                if ((k > 0 && Next(line[k - 1], s)) || (k + 1 < line.Count && Next(s, line[k + 1]))) return true;
                var (left, right) = segments[s];
                if (left.X == right.X)
                {
                    for (var j = k + 2; j < line.Count && At(line[j]) <= right.Y + margin; j++)
                    {
                        if (Next(s, line[j])) return true;
                    }
                }
            }
            else
            {
                var k = Find(s);
                if (k >= 0)
                {
                    line.RemoveAt(k);
                    if (k > 0 && k < line.Count && Next(line[k - 1], line[k])) return true;
                }
            }

            if (passed != null && (index + 1 == events.Count || events[index + 1].At != at))
            {
                passed(at);
            }
        }

        return false;
    }

    private double CrossingX(int below, int above)
    {
        var (p1, p2) = segments[below];
        var (p3, p4) = segments[above];
        var d1 = p2 - p1;
        var d2 = p4 - p3;
        var denominator = d1.X * d2.Y - d1.Y * d2.X;
        if (denominator == 0) return double.NaN;
        var w = p3 - p1;
        var t = (w.X * d2.Y - w.Y * d2.X) / denominator;
        var u = (w.X * d1.Y - w.Y * d1.X) / denominator;
        return t > 0 && t < 1 && u > 0 && u < 1 ? p1.X + d1.X * t : double.NaN;
    }

    private int Place(int s, bool leftOfX = false)
    {
        int lo = 0, hi = line.Count;
        while (lo < hi)
        {
            var m = (lo + hi) / 2;
            if (Below(line[m], s, leftOfX)) lo = m + 1;
            else hi = m;
        }

        return lo;
    }

    private int Find(int s, bool leftOfX = true)
    {
        var at = Place(s, leftOfX);
        for (var step = 0; step <= line.Count; step++)
        {
            if (at - step >= 0 && at - step < line.Count && line[at - step] == s) return at - step;
            if (at + step < line.Count && line[at + step] == s) return at + step;
        }

        return -1;
    }

    private bool Below(int a, int b, bool leftOfX)
    {
        var (ya, yb) = (At(a), At(b));
        if (ya != yb) return ya < yb;
        var (sa, sb) = (Slope(a), Slope(b));
        if (sa == sb) return a < b;
        return leftOfX ? sa > sb : sa < sb;
    }

    private double At(int s)
    {
        var (left, right) = segments[s];
        if (left.X == right.X || x <= left.X) return left.Y;
        if (x >= right.X) return right.Y;
        return left.Y + (right.Y - left.Y) * ((x - left.X) / (right.X - left.X));
    }

    private double Slope(int s)
    {
        var (left, right) = segments[s];
        return left.X == right.X ? double.PositiveInfinity : (right.Y - left.Y) / (right.X - left.X);
    }
}
