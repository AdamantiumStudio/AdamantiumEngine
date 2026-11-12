using System;
using System.Collections.Generic;
using System.Linq;

namespace Adamantium.Mathematics.Triangulation;

/// <summary>
/// The working of <see cref="GeometryOverlay"/>: the points, the pieces the rings are cut into, the faces they bound and
/// the winding in each.
/// </summary>
internal sealed class OverlayGraph
{
    private const double SnapFraction = 1e-12;
    private const double PositionFraction = 2.5e-16;
    private const double MergeSteps = 100;
    private const int MostRounds = 256;
    private const double WeldSteps = 4;
    private const int SweepRounds = 8;

    private readonly double merge;
    private readonly List<Vector2<double>> points = [];
    private readonly Dictionary<(long, long), List<int>> corners = [];
    private readonly Dictionary<Vector2<double>, int> given = [];
    private readonly List<int> alias = [];
    private List<(int From, int To, int First, int Second)> pieces = [];
    private int[] together = [];

    private OverlayGraph(double merge)
    {
        this.merge = merge;
    }

    /// <summary>See <see cref="GeometryOverlay.Combine(RingArea, RingArea, AreaCombineMode, double)"/>; points nearer
    /// than <paramref name="weld"/>, or than the overlay's own least distance if that is longer, are one.</summary>
    public static List<Vector2<double>[]> Combine(RingArea first, RingArea second, AreaCombineMode mode, double weld = 0)
    {
        var grid = Grid(first.Rings, second.Rings);
        if (grid <= 0)
        {
            return [];
        }

        var overlay = new OverlayGraph(Math.Max(grid * MergeSteps, weld));
        overlay.AddRings(first.Rings, 0);
        overlay.AddRings(second.Rings, 1);
        overlay.Planarize();
        return overlay.Boundary(first.Rule, second.Rule, mode);
    }

    /// <summary>Whether <paramref name="mode"/> keeps a point inside the first area or not and the second or not.</summary>
    internal static bool Keeps(AreaCombineMode mode, bool inFirst, bool inSecond) => mode switch
    {
        AreaCombineMode.Union => inFirst || inSecond,
        AreaCombineMode.Xor => inFirst != inSecond,
        AreaCombineMode.Intersect => inFirst && inSecond,
        _ => inFirst && !inSecond,
    };

    private static bool Usable(Vector2<double>[] ring) => ring != null && ring.Length >= 2 && MathHelper.RingMath.Finite(ring);

    private static double Grid(IReadOnlyList<Vector2<double>[]> first, IReadOnlyList<Vector2<double>[]> second)
    {
        double minX = double.MaxValue, minY = double.MaxValue, maxX = double.MinValue, maxY = double.MinValue;
        foreach (var rings in new[] { first, second })
        {
            foreach (var ring in rings)
            {
                if (!Usable(ring))
                {
                    continue;
                }

                foreach (var p in ring)
                {
                    minX = Math.Min(minX, p.X);
                    minY = Math.Min(minY, p.Y);
                    maxX = Math.Max(maxX, p.X);
                    maxY = Math.Max(maxY, p.Y);
                }
            }
        }

        if (maxX < minX)
        {
            return 0;
        }

        var extent = Math.Max(maxX - minX, maxY - minY);
        var position = Math.Max(Math.Max(Math.Abs(minX), Math.Abs(maxX)), Math.Max(Math.Abs(minY), Math.Abs(maxY)));
        return Math.Max(Math.Max(extent * SnapFraction, position * PositionFraction), 1e-300);
    }

    private void AddRings(IReadOnlyList<Vector2<double>[]> rings, int area)
    {
        foreach (var ring in rings)
        {
            if (!Usable(ring))
            {
                continue;
            }

            for (var i = 0; i < ring.Length; i++)
            {
                var from = Corner(ring[i]);
                var to = Corner(ring[(i + 1) % ring.Length]);
                if (from != to)
                {
                    pieces.Add((from, to, area == 0 ? 1 : 0, area == 1 ? 1 : 0));
                }
            }
        }
    }

    private int Corner(Vector2<double> p)
    {
        if (given.TryGetValue(p, out var known))
        {
            return Resolve(known);
        }

        var corner = Nearest(p);
        given[p] = corner;
        return corner;
    }

    private int Nearest(Vector2<double> p)
    {
        var key = ((long)Math.Round(p.X / merge), (long)Math.Round(p.Y / merge));
        var nearest = -1;
        var nearestDistance = merge;
        for (var dx = -1; dx <= 1; dx++)
        {
            for (var dy = -1; dy <= 1; dy++)
            {
                if (!corners.TryGetValue((key.Item1 + dx, key.Item2 + dy), out var cell))
                {
                    continue;
                }

                foreach (var near in cell)
                {
                    var distance = (points[near] - p).Length();
                    if (distance <= nearestDistance)
                    {
                        (nearest, nearestDistance) = (near, distance);
                    }
                }
            }
        }

        if (nearest >= 0)
        {
            return Resolve(nearest);
        }

        var id = Point(p);
        if (!corners.TryGetValue(key, out var own))
        {
            corners[key] = own = [];
        }

        if (!own.Contains(id))
        {
            own.Add(id);
        }

        return id;
    }

    private void Planarize()
    {
        Apart();
        var level = new Dictionary<(bool, double), int>();
        for (var i = 0; i < pieces.Count; i++)
        {
            var (a, b) = (points[pieces[i].From], points[pieces[i].To]);
            if (a.X != b.X && a.Y != b.Y)
            {
                continue;
            }

            var key = (a.Y == b.Y, a.Y == b.Y ? a.Y : a.X);
            if (level.TryGetValue(key, out var first))
            {
                Join(first, i);
            }
            else
            {
                level[key] = i;
            }
        }

        pieces = Split(new List<int>[pieces.Count]);
        var everyPair = false;
        for (var round = 0; ; round++)
        {
            if (round == MostRounds)
            {
                throw new InvalidOperationException("The rings' edges kept cutting one another without end.");
            }

            Apart();
            var cuts = new List<int>[pieces.Count];
            var cut = false;
            var segments = new (Vector2<double> A, Vector2<double> B)[pieces.Count];
            for (var i = 0; i < pieces.Count; i++)
            {
                segments[i] = ContactSweep.Ordered(points[pieces[i].From], points[pieces[i].To]);
            }

            Func<int, int, bool> visit = (e, f) =>
            {
                cut |= Cut(e, f, cuts);
                return false;
            };

            var held = false;
            Func<int, int, bool> mayPass = (e, f) =>
            {
                if (everyPair || Root(together, e) != Root(together, f)) return true;
                held = true;
                return false;
            };

            if (round < SweepRounds)
            {
                new ContactSweep(segments, merge).Run(visit, passCrossings: true, mayPass: mayPass);
            }
            else
            {
                new SegmentBands(segments, merge).Pairs(visit);
            }

            pieces = Split(cuts);
            if (!cut)
            {
                if (!held || everyPair)
                {
                    return;
                }

                everyPair = true;
            }
        }
    }

    private List<(int From, int To, int First, int Second)> Split(List<int>[] cuts)
    {
        for (var e = 0; e < pieces.Count; e++)
        {
            var (from, to, first, second) = pieces[e];
            pieces[e] = (Resolve(from), Resolve(to), first, second);
            if (cuts[e] != null)
            {
                for (var k = 0; k < cuts[e].Count; k++) cuts[e][k] = Resolve(cuts[e][k]);
            }
        }

        var runs = new Dictionary<(int, int), (int First, int Second)>(pieces.Count);
        var order = new List<(int, int)>(pieces.Count);
        var roots = new int[pieces.Count];
        var size = new int[pieces.Count];
        for (var e = 0; e < pieces.Count; e++)
        {
            roots[e] = Root(together, e);
            size[roots[e]]++;
        }

        Dictionary<int, List<int>> lines = null;
        for (var e = 0; e < pieces.Count; e++)
        {
            if (size[roots[e]] < 2)
            {
                continue;
            }

            lines ??= [];
            if (!lines.TryGetValue(roots[e], out var members))
            {
                lines[roots[e]] = members = [];
            }

            members.Add(e);
        }

        var alongLine = new bool[pieces.Count];
        if (lines != null)
        {
            foreach (var (root, members) in lines)
            {
                alongLine[root] = CutAlongLine(members, cuts, runs, order);
            }
        }

        var chain = new List<(double Along, int Found, int Id)>();
        var seen = new HashSet<int>();
        for (var e = 0; e < pieces.Count; e++)
        {
            if (alongLine[roots[e]])
            {
                continue;
            }

            var (from, to, first, second) = pieces[e];
            if (cuts[e] == null)
            {
                Run(from, to, first, second, runs, order);
                continue;
            }

            var a = points[from];
            var d = points[to] - a;
            chain.Clear();
            seen.Clear();
            foreach (var id in cuts[e])
            {
                if (id != from && id != to && seen.Add(id)) chain.Add((Vector2<double>.Dot(points[id] - a, d), chain.Count, id));
            }

            chain.Sort();
            var previous = from;
            foreach (var (_, _, id) in chain)
            {
                Run(previous, id, first, second, runs, order);
                previous = id;
            }

            Run(previous, to, first, second, runs, order);
        }

        var split = new List<(int From, int To, int First, int Second)>(order.Count);
        foreach (var key in order)
        {
            var count = runs[key];
            if (count.First != 0 || count.Second != 0)
            {
                split.Add((key.Item1, key.Item2, count.First, count.Second));
            }
        }

        return split;
    }

    private static void Run(int u, int v, int first, int second, Dictionary<(int, int), (int First, int Second)> runs,
        List<(int, int)> order)
    {
        if (u == v)
        {
            return;
        }

        var key = u < v ? (u, v) : (v, u);
        var way = u < v ? 1 : -1;
        if (!runs.TryGetValue(key, out var count))
        {
            order.Add(key);
        }

        runs[key] = (count.First + way * first, count.Second + way * second);
    }

    private bool CutAlongLine(List<int> members, List<int>[] cuts, Dictionary<(int, int), (int First, int Second)> runs,
        List<(int, int)> order)
    {
        var longest = members[0];
        foreach (var e in members)
        {
            if ((points[pieces[e].To] - points[pieces[e].From]).LengthSquared() >
                (points[pieces[longest].To] - points[pieces[longest].From]).LengthSquared())
            {
                longest = e;
            }
        }

        var origin = points[pieces[longest].From];
        var direction = points[pieces[longest].To] - origin;
        var ids = new HashSet<int>();
        foreach (var e in members)
        {
            ids.Add(pieces[e].From);
            ids.Add(pieces[e].To);
            if (cuts[e] != null)
            {
                ids.UnionWith(cuts[e]);
            }
        }

        var span = direction.Length();
        foreach (var id in ids)
        {
            if (Math.Abs(Cross(direction, points[id] - origin)) / span > WeldSteps * merge)
            {
                return false;
            }
        }

        var along = ids.OrderBy(id => Vector2<double>.Dot(points[id] - origin, direction)).ThenBy(id => id).ToList();
        var place = new Dictionary<int, int>(along.Count);
        for (var i = 0; i < along.Count; i++)
        {
            place[along[i]] = i;
        }

        var change = new (int First, int Second)[along.Count];
        foreach (var e in members)
        {
            var (from, to, first, second) = pieces[e];
            var (start, end) = (place[from], place[to]);
            if (start > end)
            {
                (start, end, first, second) = (end, start, -first, -second);
            }

            change[start] = (change[start].First + first, change[start].Second + second);
            change[end] = (change[end].First - first, change[end].Second - second);
        }

        var running = (First: 0, Second: 0);
        for (var i = 0; i + 1 < along.Count; i++)
        {
            running = (running.First + change[i].First, running.Second + change[i].Second);
            if (running.First == 0 && running.Second == 0)
            {
                continue;
            }

            var (u, v) = (along[i], along[i + 1]);
            var key = u < v ? (u, v) : (v, u);
            var way = u < v ? 1 : -1;
            if (!runs.TryGetValue(key, out var count))
            {
                order.Add(key);
            }

            runs[key] = (count.First + way * running.First, count.Second + way * running.Second);
        }

        return true;
    }

    private void Apart()
    {
        together = new int[pieces.Count];
        for (var i = 0; i < together.Length; i++)
        {
            together[i] = i;
        }
    }

    private void Join(int e, int f)
    {
        var (a, b) = (Root(together, e), Root(together, f));
        if (a != b)
        {
            together[a] = b;
        }
    }

    private bool Cut(int e, int f, List<int>[] cuts)
    {
        var (e1, e2, _, _) = pieces[e];
        var (f1, f2, _, _) = pieces[f];
        var (p1, p2, p3, p4) = (points[e1], points[e2], points[f1], points[f2]);
        var cut = false;
        if (OnInterior(f1, e1, e2)) cut = Touch(cuts, e, f1, e1, e2);
        if (OnInterior(f2, e1, e2)) cut |= Touch(cuts, e, f2, e1, e2);
        if (OnInterior(e1, f1, f2)) cut |= Touch(cuts, f, e1, f1, f2);
        if (OnInterior(e2, f1, f2)) cut |= Touch(cuts, f, e2, f1, f2);

        var d1 = p2 - p1;
        var d2 = p4 - p3;
        var denominator = Cross(d1, d2);
        var length = d1.Length();
        var (side3, side4) = (Cross(d1, p3 - p1), Cross(d1, p4 - p1));
        if (Math.Abs(side3) / length <= merge && Math.Abs(side4) / length <= merge)
        {
            var squared = length * length;
            var (t3, t4) = (Vector2<double>.Dot(p3 - p1, d1) / squared, Vector2<double>.Dot(p4 - p1, d1) / squared);
            var reach = merge / length;
            if (Math.Max(t3, t4) > reach && Math.Min(t3, t4) < 1 - reach)
            {
                Join(e, f);
            }

            return cut;
        }

        if (denominator == 0 || e1 == f1 || e1 == f2 || e2 == f1 || e2 == f2)
        {
            return cut;
        }

        var t = Cross(p3 - p1, d2) / denominator;
        var u = Cross(p3 - p1, d1) / denominator;
        if (t <= 0 || t >= 1 || u <= 0 || u >= 1)
        {
            return cut;
        }

        var crossing = Corner(p1 + d1 * t);
        if (crossing != e1 && crossing != e2)
        {
            cut |= Add(cuts, e, crossing);
        }

        if (crossing != f1 && crossing != f2)
        {
            cut |= Add(cuts, f, crossing);
        }

        return cut;
    }

    private static bool Add(List<int>[] cuts, int edge, int point)
    {
        (cuts[edge] ??= []).Add(point);
        return true;
    }

    private bool OnInterior(int point, int from, int to)
    {
        if (point == from || point == to)
        {
            return false;
        }

        var (p, a, b) = (points[point], points[from], points[to]);
        var d = b - a;
        var length = d.Length();
        var t = Vector2<double>.Dot(p - a, d) / (length * length);
        return t > 0 && t < 1 && Math.Abs(Cross(d, p - a)) / length <= merge;
    }

    private List<Vector2<double>[]> Boundary(FillRule firstRule, FillRule secondRule, AreaCombineMode mode)
    {
        var winding = FaceWindings(out var face);
        var outgoing = new Dictionary<int, List<(double Angle, int Edge)>>();
        var kept = new List<(int From, int To)>();
        for (var i = 0; i < pieces.Count; i++)
        {
            var (u, v, _, _) = pieces[i];
            var (leftFirst, leftSecond) = winding[face[2 * i]];
            var (rightFirst, rightSecond) = winding[face[2 * i + 1]];
            var leftFilled = Keeps(mode, Inside(leftFirst, firstRule), Inside(leftSecond, secondRule));
            var rightFilled = Keeps(mode, Inside(rightFirst, firstRule), Inside(rightSecond, secondRule));
            if (leftFilled == rightFilled)
            {
                continue;
            }

            var edge = leftFilled ? (u, v) : (v, u);
            if (!outgoing.TryGetValue(edge.Item1, out var list))
            {
                outgoing[edge.Item1] = list = [];
            }

            list.Add((Angle(points[edge.Item2] - points[edge.Item1]), kept.Count));
            kept.Add(edge);
        }

        foreach (var list in outgoing.Values)
        {
            list.Sort((a, b) => a.Angle != b.Angle ? a.Angle.CompareTo(b.Angle) : a.Edge.CompareTo(b.Edge));
        }

        var used = new bool[kept.Count];
        var rings = new List<Vector2<double>[]>();
        for (var start = 0; start < kept.Count; start++)
        {
            if (used[start])
            {
                continue;
            }

            var walk = new List<int>();
            var current = start;
            var closed = false;
            while (current >= 0)
            {
                used[current] = true;
                walk.Add(kept[current].From);
                current = Next(kept, outgoing, current);
                if (current == start)
                {
                    closed = true;
                    break;
                }

                if (current >= 0)
                {
                    Remove(outgoing, kept, current);
                }
            }

            Remove(outgoing, kept, start);
            if (closed)
            {
                AddSimpleRings(walk, rings);
            }
        }

        return rings;
    }

    private void Remove(Dictionary<int, List<(double Angle, int Edge)>> outgoing, List<(int From, int To)> kept, int edge)
    {
        var (from, to) = kept[edge];
        var list = outgoing[from];
        var key = (Angle(points[to] - points[from]), edge);
        var at = list.BinarySearch(key, Comparer<(double Angle, int Edge)>.Create((a, b) =>
            a.Angle != b.Angle ? a.Angle.CompareTo(b.Angle) : a.Edge.CompareTo(b.Edge)));
        if (at >= 0)
        {
            list.RemoveAt(at);
        }
    }

    private (int First, int Second)[] FaceWindings(out int[] face)
    {
        var count = pieces.Count * 2;
        var around = new List<int>[points.Count];
        for (var h = 0; h < count; h++)
        {
            (around[Origin(h)] ??= []).Add(h);
        }

        var place = new int[count];
        foreach (var list in around)
        {
            if (list == null)
            {
                continue;
            }

            list.Sort((g, h) => Angle(Direction(g)).CompareTo(Angle(Direction(h))));
            for (var k = 0; k < list.Count; k++)
            {
                place[list[k]] = k;
            }
        }

        face = new int[count];
        Array.Fill(face, -1);
        var faces = new List<List<int>>();
        for (var h = 0; h < count; h++)
        {
            if (face[h] >= 0)
            {
                continue;
            }

            var edges = new List<int>();
            for (var g = h; face[g] < 0;)
            {
                face[g] = faces.Count;
                edges.Add(g);
                var twin = g ^ 1;
                var list = around[Origin(twin)];
                g = list[(place[twin] - 1 + list.Count) % list.Count];
            }

            faces.Add(edges);
        }

        var set = Sets();
        var leftmost = new Dictionary<int, int>();
        for (var h = 0; h < count; h++)
        {
            var origin = Origin(h);
            var s = set[origin];
            if (!leftmost.TryGetValue(s, out var best) || Before(origin, Origin(best)) ||
                (origin == Origin(best) && Angle(Direction(h)) > Angle(Direction(best))))
            {
                leftmost[s] = h;
            }
        }

        var winding = new (int First, int Second)[faces.Count];
        var known = new bool[faces.Count];
        var queue = new Queue<int>();
        var faceOf = face;
        void Spread(int start, (int First, int Second) around)
        {
            if (known[start])
            {
                return;
            }

            winding[start] = around;
            known[start] = true;
            queue.Enqueue(start);
            while (queue.Count > 0)
            {
                var f = queue.Dequeue();
                foreach (var h in faces[f])
                {
                    var other = faceOf[h ^ 1];
                    if (known[other])
                    {
                        continue;
                    }

                    var (first, second) = (pieces[h >> 1].First, pieces[h >> 1].Second);
                    winding[other] = (h & 1) == 0
                        ? (winding[f].First - first, winding[f].Second - second)
                        : (winding[f].First + first, winding[f].Second + second);
                    known[other] = true;
                    queue.Enqueue(other);
                }
            }
        }

        var segments = new (Vector2<double> Left, Vector2<double> Right)[pieces.Count];
        for (var i = 0; i < pieces.Count; i++)
        {
            segments[i] = ContactSweep.Ordered(points[pieces[i].From], points[pieces[i].To]);
        }

        var started = new HashSet<int>();
        var joinedThere = new List<int>();
        var sweep = new ContactSweep(segments, 0);
        sweep.Run(static (_, _) => false, joined: (piece, _) =>
        {
            if (!started.Contains(set[pieces[piece].From])) joinedThere.Add(piece);
        }, passed: _ =>
        {
            if (joinedThere.Count == 0) return;
            var lowest = new Dictionary<int, int>();
            foreach (var piece in joinedThere)
            {
                var s = set[pieces[piece].From];
                var at = sweep.Position(piece);
                if (!lowest.TryGetValue(s, out var low) || at < low) lowest[s] = at;
            }

            joinedThere.Clear();
            foreach (var (s, low) in lowest.OrderBy(entry => entry.Value))
            {
                started.Add(s);
                var line = sweep.Line;
                var j = low - 1;
                while (j >= 0 && (set[pieces[line[j]].From] == s || segments[line[j]].Left.X == segments[line[j]].Right.X)) j--;
                var around = (First: 0, Second: 0);
                if (j >= 0)
                {
                    var under = line[j];
                    var above = points[pieces[under].To].X > points[pieces[under].From].X ? 2 * under : 2 * under + 1;
                    around = winding[faceOf[above]];
                }

                Spread(faceOf[leftmost[s]], around);
            }
        });

        return winding;
    }

    private int[] Sets()
    {
        var parent = new int[points.Count];
        for (var i = 0; i < parent.Length; i++)
        {
            parent[i] = i;
        }

        foreach (var (from, to, _, _) in pieces)
        {
            var (a, b) = (Root(parent, from), Root(parent, to));
            if (a != b)
            {
                parent[a] = b;
            }
        }

        for (var i = 0; i < parent.Length; i++)
        {
            parent[i] = Root(parent, i);
        }

        return parent;
    }

    private static int Root(int[] parent, int i)
    {
        while (parent[i] != i)
        {
            parent[i] = parent[parent[i]];
            i = parent[i];
        }

        return i;
    }

    private bool Before(int a, int b) =>
        points[a].X < points[b].X || (points[a].X == points[b].X && points[a].Y < points[b].Y);

    private int Origin(int h) => (h & 1) == 0 ? pieces[h >> 1].From : pieces[h >> 1].To;

    private Vector2<double> Direction(int h) => points[Origin(h ^ 1)] - points[Origin(h)];

    /// <summary>Whether a point of <paramref name="winding"/> is inside under <paramref name="rule"/>.</summary>
    internal static bool Inside(int winding, FillRule rule) => rule == FillRule.NonZero ? winding != 0 : (winding & 1) != 0;

    private int Next(List<(int From, int To)> kept, Dictionary<int, List<(double Angle, int Edge)>> outgoing, int current)
    {
        var (from, at) = kept[current];
        if (!outgoing.TryGetValue(at, out var candidates) || candidates.Count == 0)
        {
            return -1;
        }

        var back = Angle(points[from] - points[at]);
        int lo = 0, hi = candidates.Count;
        while (lo < hi)
        {
            var m = (lo + hi) / 2;
            if (candidates[m].Angle < back) lo = m + 1;
            else hi = m;
        }

        return candidates[lo > 0 ? lo - 1 : candidates.Count - 1].Edge;
    }

    private void AddSimpleRings(List<int> walk, List<Vector2<double>[]> rings)
    {
        var path = new List<int>();
        var at = new Dictionary<int, int>();
        foreach (var id in walk)
        {
            if (at.TryGetValue(id, out var first))
            {
                var loop = path.GetRange(first, path.Count - first);
                path.RemoveRange(first, path.Count - first);
                foreach (var gone in loop)
                {
                    at.Remove(gone);
                }

                AddRing(loop, rings);
            }

            at[id] = path.Count;
            path.Add(id);
        }

        AddRing(path, rings);
    }

    private void AddRing(List<int> ids, List<Vector2<double>[]> rings)
    {
        if (ids.Count < 3)
        {
            return;
        }

        var ring = new Vector2<double>[ids.Count];
        for (var i = 0; i < ids.Count; i++)
        {
            ring[i] = points[ids[i]];
        }

        rings.Add(ring);
    }

    private int Point(Vector2<double> p)
    {
        var id = points.Count;
        points.Add(p);
        alias.Add(id);
        return id;
    }

    private int Resolve(int id)
    {
        while (alias[id] != id)
        {
            alias[id] = alias[alias[id]];
            id = alias[id];
        }

        return id;
    }

    private bool Touch(List<int>[] cuts, int piece, int point, int from, int to)
    {
        var p = points[Resolve(point)];
        foreach (var end in new[] { from, to })
        {
            if ((points[Resolve(end)] - p).Length() <= WeldSteps * merge)
            {
                var (a, b) = (Resolve(point), Resolve(end));
                if (a != b)
                {
                    alias[Math.Max(a, b)] = Math.Min(a, b);
                }

                return true;
            }
        }

        return Add(cuts, piece, point);
    }

    private static double Angle(Vector2<double> d) => Math.Atan2(d.Y, d.X);

    private static double Cross(Vector2<double> a, Vector2<double> b) => a.X * b.Y - a.Y * b.X;
}
