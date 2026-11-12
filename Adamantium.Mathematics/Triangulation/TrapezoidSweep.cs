using System;
using System.Collections.Generic;

namespace Adamantium.Mathematics.Triangulation;

/// <summary>
/// The trapezoids between the edges of a face - simple rings crossing nowhere, the face inside an odd number of them -
/// found by a line swept across x holding the edges it passes in order up it. The space between two edges next to each
/// other on the line is inside when an odd number of edges lie below it; it runs on as one trapezoid for as long as the
/// same two edges bound it, so only the spaces beside an edge joining or leaving the line end or begin there.
/// </summary>
internal sealed class TrapezoidSweep
{
    private readonly List<(Vector2<double> Left, Vector2<double> Right)> edges = [];
    private readonly List<int> line = [];
    private readonly Dictionary<(int Bottom, int Top), double> open = [];
    private double x;

    /// <summary>The sweep over the edges of <paramref name="rings"/>; edges running straight up bound nothing across x
    /// and are left out.</summary>
    public TrapezoidSweep(IReadOnlyList<Vector2<double>[]> rings)
    {
        foreach (var ring in rings)
        {
            for (var i = 0; i < ring.Length; i++)
            {
                var (a, b) = (ring[i], ring[(i + 1) % ring.Length]);
                if (a.X != b.X) edges.Add(a.X < b.X ? (a, b) : (b, a));
            }
        }
    }

    /// <summary>Hands <paramref name="trapezoid"/> each trapezoid inside the face: the edge below it, the edge above
    /// it, and where across x it begins and ends.</summary>
    public void Run(Action<(Vector2<double> Left, Vector2<double> Right), (Vector2<double> Left, Vector2<double> Right), double, double> trapezoid)
    {
        var events = new List<(double X, bool Leaves, int Edge)>(edges.Count * 2);
        for (var i = 0; i < edges.Count; i++)
        {
            events.Add((edges[i].Left.X, false, i));
            events.Add((edges[i].Right.X, true, i));
        }

        events.Sort((e, f) => e.X != f.X ? e.X.CompareTo(f.X) : f.Leaves.CompareTo(e.Leaves));
        var gone = new bool[edges.Count];
        var touched = new List<int>();
        for (var k = 0; k < events.Count;)
        {
            x = events[k].X;
            touched.Clear();
            for (; k < events.Count && events[k].X == x && events[k].Leaves; k++)
            {
                var p = Find(events[k].Edge, leftOfX: true);
                if (p < 0) continue;
                Close(p - 1, p, trapezoid);
                Close(p, p + 1, trapezoid);
                line.RemoveAt(p);
                gone[events[k].Edge] = true;
                if (p > 0) touched.Add(line[p - 1]);
                if (p < line.Count) touched.Add(line[p]);
            }

            for (; k < events.Count && events[k].X == x; k++)
            {
                var edge = events[k].Edge;
                var p = Place(edge, leftOfX: false);
                Close(p - 1, p, trapezoid);
                line.Insert(p, edge);
                touched.Add(edge);
            }

            foreach (var edge in touched)
            {
                if (gone[edge]) continue;
                var p = Find(edge, leftOfX: false);
                if (p < 0) continue;
                Open(p - 1, p);
                Open(p, p + 1);
            }
        }
    }

    private void Close(int below, int above, Action<(Vector2<double> Left, Vector2<double> Right), (Vector2<double> Left, Vector2<double> Right), double, double> trapezoid)
    {
        if (below < 0 || above >= line.Count) return;
        var key = (line[below], line[above]);
        if (!open.Remove(key, out var from)) return;
        trapezoid(edges[key.Item1], edges[key.Item2], from, x);
    }

    private void Open(int below, int above)
    {
        if (below < 0 || above >= line.Count || (above & 1) == 0) return;
        open.TryAdd((line[below], line[above]), x);
    }

    private int Place(int edge, bool leftOfX)
    {
        int lo = 0, hi = line.Count;
        while (lo < hi)
        {
            var m = (lo + hi) / 2;
            if (Below(line[m], edge, leftOfX)) lo = m + 1;
            else hi = m;
        }

        return lo;
    }

    private int Find(int edge, bool leftOfX)
    {
        var at = Place(edge, leftOfX);
        for (var step = 0; step <= line.Count; step++)
        {
            if (at - step >= 0 && at - step < line.Count && line[at - step] == edge) return at - step;
            if (at + step < line.Count && line[at + step] == edge) return at + step;
        }

        return -1;
    }

    private bool Below(int a, int b, bool leftOfX)
    {
        var (ya, yb) = (At(edges[a], x), At(edges[b], x));
        if (ya != yb) return ya < yb;
        var (sa, sb) = (Slope(edges[a]), Slope(edges[b]));
        if (sa == sb) return a < b;
        return leftOfX ? sa > sb : sa < sb;
    }

    /// <summary>Where across y <paramref name="edge"/> is at <paramref name="at"/>, held to its ends.</summary>
    public static double At((Vector2<double> Left, Vector2<double> Right) edge, double at)
    {
        var (left, right) = edge;
        return at <= left.X ? left.Y : at >= right.X ? right.Y : left.Y + (right.Y - left.Y) * ((at - left.X) / (right.X - left.X));
    }

    private static double Slope((Vector2<double> Left, Vector2<double> Right) edge) =>
        (edge.Right.Y - edge.Left.Y) / (edge.Right.X - edge.Left.X);
}
