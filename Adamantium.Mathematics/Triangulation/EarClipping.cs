using System;
using System.Collections.Generic;

namespace Adamantium.Mathematics.Triangulation;

/// <summary>
/// Cuts a polygon - an outer ring and the holes in it - into triangles by clipping ears: each hole is first joined to
/// the outer ring by a bridge to a vertex it sees, then a corner whose triangle holds nothing of the polygon's edge is
/// cut off, again and again. The triangles are indices into the points given, about one per point; no point is made.
/// Rings that touch one another at a point or along a side are cut as they are. Rings that cross themselves or each
/// other are not what this is for: they still end in triangles, but not the fill a fill rule would give, and the call
/// says so.
/// </summary>
public static class EarClipping
{
    private const int LeastForGrid = 64;
    private const long LeastForIndex = 2_000_000;

    /// <summary>Adds to <paramref name="triangles"/>, three indices into <paramref name="points"/> to a triangle, the
    /// triangles of the polygon whose outer ring runs from the first point to the first of
    /// <paramref name="holeStarts"/> and whose holes run from each of them to the next; a ring's last point is joined to
    /// its first. Either way round each ring goes is taken; the triangles turn the way the outer ring does. False when
    /// the rings were not those of a polygon - crossing, a hole outside, no area - so what was cut is not its fill.
    /// </summary>
    public static bool Triangulate(ReadOnlySpan<Vector2<double>> points, ReadOnlySpan<int> holeStarts, List<int> triangles)
    {
        if (triangles == null)
        {
            throw new ArgumentNullException(nameof(triangles));
        }

        for (var h = 0; h < holeStarts.Length; h++)
        {
            if (holeStarts[h] < 0 || holeStarts[h] > points.Length || h > 0 && holeStarts[h] < holeStarts[h - 1])
            {
                throw new ArgumentOutOfRangeException(nameof(holeStarts),
                    "Each hole starts within the points, after the one before it.");
            }
        }

        var outerEnd = holeStarts.Length > 0 ? holeStarts[0] : points.Length;
        if (outerEnd < 3)
        {
            return outerEnd == 0 && points.Length == 0;
        }

        var outerTurn = Turn(points, 0, outerEnd);
        if (outerTurn == 0)
        {
            return false;
        }

        var ring = new Ring(points, points.Length + 2 * holeStarts.Length);
        var start = ring.Link(0, outerEnd, counterclockwise: true);
        if (start < 0)
        {
            return false;
        }

        if (holeStarts.Length > 0)
        {
            start = ring.BridgeHoles(start, holeStarts);
        }

        var first = triangles.Count;
        ring.Clip(start, triangles, bridged: holeStarts.Length > 0);
        if (outerTurn < 0)
        {
            for (var i = first; i + 2 < triangles.Count; i += 3)
            {
                (triangles[i + 1], triangles[i + 2]) = (triangles[i + 2], triangles[i + 1]);
            }
        }

        return ring.IsClean;
    }

    private static double Turn(ReadOnlySpan<Vector2<double>> points, int from, int to)
    {
        var twice = 0.0;
        for (int i = from, j = to - 1; i < to; j = i++)
        {
            twice += (points[j].X - points[i].X) * (points[j].Y + points[i].Y);
        }

        return twice;
    }

    private static double Cross(Vector2<double> a, Vector2<double> b, Vector2<double> c) =>
        (b.X - a.X) * (c.Y - a.Y) - (b.Y - a.Y) * (c.X - a.X);

    private static bool InTriangle(Vector2<double> a, Vector2<double> b, Vector2<double> c, Vector2<double> p) =>
        Cross(a, b, p) >= 0 && Cross(b, c, p) >= 0 && Cross(c, a, p) >= 0;

    private static bool InTriangleEitherWay(Vector2<double> a, Vector2<double> b, Vector2<double> c, Vector2<double> p) =>
        Cross(a, b, c) >= 0 ? InTriangle(a, b, c, p) : InTriangle(a, c, b, p);

    private static bool InCorner(Vector2<double> corner, Vector2<double> next, Vector2<double> last, Vector2<double> p) =>
        Cross(corner, next, p) > 0 && Cross(corner, p, last) > 0;

    private ref struct Ring
    {
        private readonly ReadOnlySpan<Vector2<double>> _points;
        private readonly int[] _point;
        private readonly int[] _previous;
        private readonly int[] _next;
        private int _count;
        private bool _forced;
        private int[] _twin;
        private bool[] _merged;
        private EdgeRows _rows;

        public Ring(ReadOnlySpan<Vector2<double>> points, int capacity)
        {
            _points = points;
            _point = new int[capacity];
            _previous = new int[capacity];
            _next = new int[capacity];
            _count = 0;
            _forced = false;
            _twin = null;
            _merged = null;
            _rows = default;
        }

        public bool IsClean => !_forced;

        public int Count => _count;

        public Vector2<double> At(int node) => _points[_point[node]];

        public int Next(int node) => _next[node];

        public bool IsConvex(int node) => Cross(At(_previous[node]), At(node), At(_next[node])) > 0;

        public int Link(int from, int to, bool counterclockwise)
        {
            var forward = Turn(_points, from, to) > 0 == counterclockwise;
            var first = -1;
            var last = -1;
            var length = 0;
            for (var k = 0; k < to - from; k++)
            {
                var index = forward ? from + k : to - 1 - k;
                if (last >= 0 && At(last) == _points[index])
                {
                    continue;
                }

                var node = _count++;
                _point[node] = index;
                if (last < 0)
                {
                    first = node;
                }
                else
                {
                    _next[last] = node;
                    _previous[node] = last;
                }

                last = node;
                length++;
            }

            if (length > 1 && At(last) == At(first))
            {
                last = _previous[last];
                _count--;
                length--;
            }

            if (length < 3)
            {
                _count -= length;
                return -1;
            }

            _next[last] = first;
            _previous[first] = last;
            return first;
        }

        public int BridgeHoles(int start, ReadOnlySpan<int> holeStarts)
        {
            var holes = new List<(double X, int Node)>(holeStarts.Length);
            for (var h = 0; h < holeStarts.Length; h++)
            {
                var end = h + 1 < holeStarts.Length ? holeStarts[h + 1] : _points.Length;
                var hole = holeStarts[h] < end ? Link(holeStarts[h], end, counterclockwise: false) : -1;
                if (hole < 0)
                {
                    continue;
                }

                var rightmost = Rightmost(hole);
                holes.Add((At(rightmost).X, rightmost));
            }

            if ((long)holes.Count * _count >= LeastForIndex)
            {
                Index(start);
            }

            holes.Sort((a, b) => b.X.CompareTo(a.X));
            foreach (var (_, node) in holes)
            {
                if (Visible(start, node) is var bridge and >= 0)
                {
                    Splice(bridge, node);
                }
                else
                {
                    _forced = true;
                }
            }

            return start;
        }

        public void Clip(int start, List<int> triangles, bool bridged)
        {
            var left = 1;
            var convex = IsConvex(start);
            for (var node = _next[start]; node != start; node = _next[node])
            {
                left++;
                convex &= IsConvex(node);
            }

            if (convex && !bridged)
            {
                if (!TurnsOnce(start))
                {
                    _forced = true;
                }

                for (var node = _next[start]; _next[node] != start; node = _next[node])
                {
                    triangles.Add(_point[start]);
                    triangles.Add(_point[node]);
                    triangles.Add(_point[_next[node]]);
                }

                return;
            }

            var grid = left >= LeastForGrid ? new Grid(this, start, left, Shared(start, left)) : default;
            var gone = new bool[_count];
            var queued = new bool[_count];
            var queue = new List<int>(left * 2);
            var head = 0;
            var live = start;
            Enqueue(start, queue, queued);
            for (var node = _next[start]; node != start; node = _next[node])
            {
                Enqueue(node, queue, queued);
            }

            var preferred = -1;
            var askedAgain = false;
            while (left > 3)
            {
                int ear;
                if (preferred >= 0 && queued[preferred] && !gone[preferred])
                {
                    ear = preferred;
                }
                else
                {
                    ear = -1;
                    while (head < queue.Count)
                    {
                        var node = queue[head++];
                        if (queued[node] && !gone[node])
                        {
                            ear = node;
                            break;
                        }
                    }
                }

                preferred = -1;
                if (ear < 0)
                {
                    if (!askedAgain)
                    {
                        askedAgain = true;
                        queue.Clear();
                        head = 0;
                        Enqueue(live, queue, queued);
                        for (var node = _next[live]; node != live; node = _next[node])
                        {
                            Enqueue(node, queue, queued);
                        }

                        continue;
                    }

                    _forced = true;
                    ear = LeastHarm(live);
                    Cut(ear, triangles, grid, gone, queue, queued);
                    live = _next[ear];
                    left--;
                    askedAgain = false;
                    continue;
                }

                queued[ear] = false;
                var a = _previous[ear];
                var c = _next[ear];
                var turn = Cross(At(a), At(ear), At(c));
                if (turn == 0 || turn > 0 && Opens(a, At(c)) && Opens(c, At(a)) && Empty(a, ear, c, grid))
                {
                    Cut(ear, triangles, grid, gone, queue, queued);
                    live = c;
                    left--;
                    preferred = _next[c];
                    askedAgain = false;
                }
            }

            var x = _previous[live];
            var z = _next[live];
            if (left == 3 && Cross(At(x), At(live), At(z)) > 0)
            {
                triangles.Add(_point[x]);
                triangles.Add(_point[live]);
                triangles.Add(_point[z]);
            }
        }

        public bool Blocks(int node, int a, int b, int c)
        {
            var p = At(node);
            var pa = At(a);
            var pb = At(b);
            var pc = At(c);
            if (p == pa)
            {
                return CornerBlocked(node, pa, pb, pc);
            }

            if (p == pb)
            {
                return CornerBlocked(node, pb, pc, pa);
            }

            if (p == pc)
            {
                return CornerBlocked(node, pc, pa, pb);
            }

            return Cross(At(_previous[node]), p, At(_next[node])) <= 0 && InTriangle(pa, pb, pc, p);
        }

        private void Index(int start)
        {
            var capacity = _point.Length;
            _twin = new int[capacity];
            _merged = new bool[capacity];
            for (var node = 0; node < capacity; node++)
            {
                _twin[node] = node;
            }

            var xs = new double[_count];
            var nodes = new int[_count];
            for (var node = 0; node < _count; node++)
            {
                xs[node] = At(node).X;
                nodes[node] = node;
            }

            Array.Sort(xs, nodes);
            var ys = new double[_count];
            for (var from = 0; from < _count;)
            {
                var to = from + 1;
                while (to < _count && xs[to] == xs[from])
                {
                    to++;
                }

                if (to - from > 1)
                {
                    for (var i = from; i < to; i++)
                    {
                        ys[i] = At(nodes[i]).Y;
                    }

                    Array.Sort(ys, nodes, from, to - from);
                    for (var i = from + 1; i < to; i++)
                    {
                        if (ys[i] == ys[i - 1])
                        {
                            Twin(nodes[i - 1], nodes[i]);
                        }
                    }
                }

                from = to;
            }

            var walk = start;
            do
            {
                _merged[walk] = true;
                walk = _next[walk];
            }
            while (walk != start);

            _rows = new EdgeRows(this, capacity);
        }

        private void Twin(int node, int other)
        {
            _twin[other] = _twin[node];
            _twin[node] = other;
        }

        private int Rightmost(int start)
        {
            var best = start;
            var node = _next[start];
            while (node != start)
            {
                var p = At(node);
                var b = At(best);
                if (p.X > b.X || p.X == b.X && p.Y < b.Y)
                {
                    best = node;
                }

                node = _next[node];
            }

            return best;
        }

        private int Visible(int start, int hole)
        {
            var m = At(hole);
            var ray = new Ray(m);
            if (_rows.IsMade)
            {
                _rows.Begin();
                foreach (var node in _rows.Row(m.Y))
                {
                    if (_merged[node] && _rows.Visit(node) && ray.Meets(this, node))
                    {
                        return Cone(ray.Candidate, m);
                    }
                }
            }
            else
            {
                var node = start;
                do
                {
                    if (ray.Meets(this, node))
                    {
                        return Cone(ray.Candidate, m);
                    }

                    node = _next[node];
                }
                while (node != start);
            }

            if (ray.Candidate < 0 || ray.Crossings % 2 == 0)
            {
                return -1;
            }

            var hit = new Vector2<double>(ray.Nearest, m.Y);
            var p = At(ray.Candidate);
            if (p == hit)
            {
                return Cone(ray.Candidate, m);
            }

            var best = ray.Candidate;
            var bestTangent = double.PositiveInfinity;
            if (_rows.IsMade)
            {
                _rows.Begin();
                var low = Math.Min(m.Y, p.Y);
                var high = Math.Max(m.Y, p.Y);
                for (var row = _rows.RowOf(low); row <= _rows.RowOf(high); row++)
                {
                    foreach (var node in _rows.Row(row))
                    {
                        if (_merged[node] && _rows.Visit(node))
                        {
                            Nearer(node, m, hit, p, ref best, ref bestTangent);
                        }
                    }
                }
            }
            else
            {
                var node = start;
                do
                {
                    Nearer(node, m, hit, p, ref best, ref bestTangent);
                    node = _next[node];
                }
                while (node != start);
            }

            return Cone(best, m);
        }

        private void Nearer(int node, Vector2<double> m, Vector2<double> hit, Vector2<double> p, ref int best,
            ref double bestTangent)
        {
            var r = At(node);
            if (r != p && r.X >= m.X && InTriangleEitherWay(m, hit, p, r) && IsReflex(node))
            {
                var tangent = Math.Abs(r.Y - m.Y) / Math.Max(r.X - m.X, double.Epsilon);
                if (tangent < bestTangent || tangent == bestTangent && r.X < At(best).X)
                {
                    bestTangent = tangent;
                    best = node;
                }
            }
        }

        private int Cone(int node, Vector2<double> m)
        {
            var point = At(node);
            var other = node;
            do
            {
                if (At(other) == point && Opens(other, m))
                {
                    return other;
                }

                other = _twin != null ? _twin[other] : _next[other];
            }
            while (other != node);

            return node;
        }

        private void Splice(int bridge, int hole)
        {
            var bridgeAgain = _count++;
            var holeAgain = _count++;
            _point[bridgeAgain] = _point[bridge];
            _point[holeAgain] = _point[hole];

            var afterBridge = _next[bridge];
            var beforeHole = _previous[hole];

            _next[bridge] = hole;
            _previous[hole] = bridge;

            _next[beforeHole] = holeAgain;
            _previous[holeAgain] = beforeHole;
            _next[holeAgain] = bridgeAgain;
            _previous[bridgeAgain] = holeAgain;
            _next[bridgeAgain] = afterBridge;
            _previous[afterBridge] = bridgeAgain;

            if (!_rows.IsMade)
            {
                return;
            }

            Twin(bridge, bridgeAgain);
            Twin(hole, holeAgain);
            var node = hole;
            do
            {
                _merged[node] = true;
                node = _next[node];
            }
            while (node != afterBridge);

            _rows.Insert(this, bridge);
            _rows.Insert(this, holeAgain);
            _rows.Insert(this, bridgeAgain);
        }

        private void Cut(int ear, List<int> triangles, Grid grid, bool[] gone, List<int> queue, bool[] queued)
        {
            var a = _previous[ear];
            var c = _next[ear];
            if (Cross(At(a), At(ear), At(c)) > 0)
            {
                triangles.Add(_point[a]);
                triangles.Add(_point[ear]);
                triangles.Add(_point[c]);
            }

            gone[ear] = true;
            queued[ear] = false;
            Remove(ear, grid);
            if (grid.IsMade)
            {
                grid.Settle(this, a);
                grid.Settle(this, c);
            }

            Enqueue(a, queue, queued);
            Enqueue(c, queue, queued);
        }

        private static void Enqueue(int node, List<int> queue, bool[] queued)
        {
            if (!queued[node])
            {
                queued[node] = true;
                queue.Add(node);
            }
        }

        private bool TurnsOnce(int start)
        {
            var turned = 0.0;
            var node = start;
            do
            {
                var a = At(_previous[node]);
                var b = At(node);
                var c = At(_next[node]);
                var inX = b.X - a.X;
                var inY = b.Y - a.Y;
                var outX = c.X - b.X;
                var outY = c.Y - b.Y;
                turned += Math.Atan2(inX * outY - inY * outX, inX * outX + inY * outY);
                node = _next[node];
            }
            while (node != start);

            return Math.Abs(turned - 2 * Math.PI) < 1e-6;
        }

        private bool[] Shared(int start, int count)
        {
            var xs = new double[count];
            var nodes = new int[count];
            var node = start;
            for (var i = 0; i < count; i++, node = _next[node])
            {
                xs[i] = At(node).X;
                nodes[i] = node;
            }

            Array.Sort(xs, nodes);
            var shared = new bool[_count];
            var ys = new double[count];
            for (var from = 0; from < count;)
            {
                var to = from + 1;
                while (to < count && xs[to] == xs[from])
                {
                    to++;
                }

                if (to - from > 1)
                {
                    for (var i = from; i < to; i++)
                    {
                        ys[i] = At(nodes[i]).Y;
                    }

                    Array.Sort(ys, nodes, from, to - from);
                    for (var i = from + 1; i < to; i++)
                    {
                        if (ys[i] == ys[i - 1])
                        {
                            shared[nodes[i]] = true;
                            shared[nodes[i - 1]] = true;
                        }
                    }
                }

                from = to;
            }

            return shared;
        }

        private int LeastHarm(int start)
        {
            var best = start;
            var least = double.PositiveInfinity;
            var node = start;
            do
            {
                var turn = Cross(At(_previous[node]), At(node), At(_next[node]));
                if (turn > 0 && turn < least)
                {
                    least = turn;
                    best = node;
                }

                node = _next[node];
            }
            while (node != start);

            return best;
        }

        private void Remove(int node, Grid grid)
        {
            if (grid.IsMade)
            {
                grid.Remove(node);
            }

            _next[_previous[node]] = _next[node];
            _previous[_next[node]] = _previous[node];
        }

        private bool Empty(int a, int b, int c, Grid grid)
        {
            if (grid.IsMade)
            {
                return grid.Empty(this, a, b, c);
            }

            for (var node = _next[c]; node != a; node = _next[node])
            {
                if (Blocks(node, a, b, c))
                {
                    return false;
                }
            }

            return true;
        }

        private bool Opens(int node, Vector2<double> m)
        {
            var a = At(_previous[node]);
            var b = At(node);
            var c = At(_next[node]);
            return Cross(a, b, c) >= 0
                ? Cross(b, c, m) >= 0 && Cross(a, b, m) >= 0
                : Cross(b, c, m) >= 0 || Cross(a, b, m) >= 0;
        }

        private bool IsReflex(int node) => Cross(At(_previous[node]), At(node), At(_next[node])) < 0;

        private bool CornerBlocked(int node, Vector2<double> corner, Vector2<double> next, Vector2<double> last) =>
            InCorner(corner, next, last, At(_previous[node])) || InCorner(corner, next, last, At(_next[node]));
    }

    private struct Ray(Vector2<double> m)
    {
        public double Nearest = double.PositiveInfinity;
        public int Candidate = -1;
        public int Crossings = 0;

        public bool Meets(Ring ring, int node)
        {
            var a = ring.At(node);
            var b = ring.At(ring.Next(node));
            if (a.Y > m.Y != b.Y > m.Y && a.X + (m.Y - a.Y) * (b.X - a.X) / (b.Y - a.Y) > m.X)
            {
                Crossings++;
            }

            if (!(a.Y > m.Y != b.Y > m.Y || a.Y == m.Y && b.Y == m.Y))
            {
                return false;
            }

            var x = a.Y == b.Y ? Math.Min(a.X, b.X) : a.X + (m.Y - a.Y) * (b.X - a.X) / (b.Y - a.Y);
            if (x < m.X || x >= Nearest)
            {
                return false;
            }

            Nearest = x;
            Candidate = a.Y == m.Y && a.X == x ? node
                : b.Y == m.Y && b.X == x ? ring.Next(node)
                : a.X >= b.X ? node : ring.Next(node);
            return x == m.X;
        }
    }

    private struct EdgeRows
    {
        private readonly double _top;
        private readonly double _height;
        private readonly int _count;
        private readonly int[] _first;
        private readonly List<int> _node;
        private readonly List<int> _after;
        private readonly int[] _seen;
        private int _now;

        public EdgeRows(Ring ring, int capacity)
        {
            double top = double.MaxValue, bottom = double.MinValue;
            for (var node = 0; node < ring.Count; node++)
            {
                top = Math.Min(top, ring.At(node).Y);
                bottom = Math.Max(bottom, ring.At(node).Y);
            }

            _count = Math.Max(1, (int)Math.Sqrt(ring.Count) * 2);
            _top = top;
            _height = Math.Max((bottom - top) / _count, double.Epsilon);
            _first = new int[_count];
            Array.Fill(_first, -1);
            _node = new List<int>(ring.Count * 2);
            _after = new List<int>(ring.Count * 2);
            _seen = new int[capacity];
            _now = 0;
            for (var node = 0; node < ring.Count; node++)
            {
                Insert(ring, node);
            }
        }

        public bool IsMade => _first != null;

        public int RowOf(double y) => Math.Clamp((int)((y - _top) / _height), 0, _count - 1);

        public void Insert(Ring ring, int node)
        {
            var a = ring.At(node).Y;
            var b = ring.At(ring.Next(node)).Y;
            for (var row = RowOf(Math.Min(a, b)); row <= RowOf(Math.Max(a, b)); row++)
            {
                _node.Add(node);
                _after.Add(_first[row]);
                _first[row] = _node.Count - 1;
            }
        }

        public void Begin() => _now++;

        public bool Visit(int node)
        {
            if (_seen[node] == _now)
            {
                return false;
            }

            _seen[node] = _now;
            return true;
        }

        public RowEntries Row(double y) => Row(RowOf(y));

        public RowEntries Row(int row) => new(_node, _after, _first[row]);
    }

    private struct RowEntries(List<int> nodes, List<int> after, int first)
    {
        private int _entry = -2;

        public RowEntries GetEnumerator() => this;

        public int Current => nodes[_entry];

        public bool MoveNext()
        {
            _entry = _entry == -2 ? first : after[_entry];
            return _entry >= 0;
        }
    }

    private readonly struct Grid
    {
        private readonly double _left;
        private readonly double _top;
        private readonly double _cellWidth;
        private readonly double _cellHeight;
        private readonly int _columns;
        private readonly int _rows;
        private readonly int[] _first;
        private readonly int[] _after;
        private readonly int[] _before;
        private readonly int[] _cellOf;
        private readonly bool[] _shared;

        public Grid(Ring ring, int start, int count, bool[] shared)
        {
            _shared = shared;
            double left = double.MaxValue, top = double.MaxValue, right = double.MinValue, bottom = double.MinValue;
            var blockers = 0;
            var node = start;
            do
            {
                if (shared[node] || !ring.IsConvex(node))
                {
                    var p = ring.At(node);
                    left = Math.Min(left, p.X);
                    top = Math.Min(top, p.Y);
                    right = Math.Max(right, p.X);
                    bottom = Math.Max(bottom, p.Y);
                    blockers++;
                }

                node = ring.Next(node);
            }
            while (node != start);

            if (blockers == 0)
            {
                left = top = right = bottom = 0;
            }

            var side = Math.Max(1, (int)Math.Sqrt(blockers / 2.0));
            _left = left;
            _top = top;
            _columns = side;
            _rows = side;
            _cellWidth = Math.Max((right - left) / side, double.Epsilon);
            _cellHeight = Math.Max((bottom - top) / side, double.Epsilon);
            _first = new int[side * side];
            _after = new int[ring.Count];
            _before = new int[ring.Count];
            _cellOf = new int[ring.Count];
            Array.Fill(_first, -1);
            Array.Fill(_cellOf, -1);

            node = start;
            do
            {
                if (!shared[node] && ring.IsConvex(node))
                {
                    node = ring.Next(node);
                    continue;
                }

                var cell = CellOf(ring.At(node));
                _cellOf[node] = cell;
                _before[node] = -1;
                _after[node] = _first[cell];
                if (_first[cell] >= 0)
                {
                    _before[_first[cell]] = node;
                }

                _first[cell] = node;
                node = ring.Next(node);
            }
            while (node != start);
        }

        public bool IsMade => _first != null;

        public void Settle(Ring ring, int node)
        {
            if (_cellOf[node] >= 0 && !_shared[node] && ring.IsConvex(node))
            {
                Remove(node);
            }
        }

        public void Remove(int node)
        {
            if (_cellOf[node] < 0)
            {
                return;
            }

            if (_before[node] >= 0)
            {
                _after[_before[node]] = _after[node];
            }
            else
            {
                _first[_cellOf[node]] = _after[node];
            }

            if (_after[node] >= 0)
            {
                _before[_after[node]] = _before[node];
            }

            _cellOf[node] = -1;
        }

        public bool Empty(Ring ring, int na, int nb, int nc)
        {
            var a = ring.At(na);
            var b = ring.At(nb);
            var c = ring.At(nc);
            var firstColumn = Column(Math.Min(a.X, Math.Min(b.X, c.X)));
            var lastColumn = Column(Math.Max(a.X, Math.Max(b.X, c.X)));
            var firstRow = Row(Math.Min(a.Y, Math.Min(b.Y, c.Y)));
            var lastRow = Row(Math.Max(a.Y, Math.Max(b.Y, c.Y)));
            for (var row = firstRow; row <= lastRow; row++)
            {
                for (var column = firstColumn; column <= lastColumn; column++)
                {
                    for (var node = _first[row * _columns + column]; node >= 0; node = _after[node])
                    {
                        if (node != na && node != nb && node != nc && ring.Blocks(node, na, nb, nc))
                        {
                            return false;
                        }
                    }
                }
            }

            return true;
        }

        private int Column(double x) => Math.Clamp((int)((x - _left) / _cellWidth), 0, _columns - 1);

        private int Row(double y) => Math.Clamp((int)((y - _top) / _cellHeight), 0, _rows - 1);

        private int CellOf(Vector2<double> p) => Row(p.Y) * _columns + Column(p.X);
    }
}
