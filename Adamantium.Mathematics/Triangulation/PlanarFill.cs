using System;
using System.Collections.Generic;

namespace Adamantium.Mathematics.Triangulation
{
    /// <summary>Fills a set of contours that may CROSS, by a fill rule.
    /// <para>Containment answers only for contours that nest. Where they cross, what is filled is not a property of any
    /// one contour but of each REGION the crossings cut the plane into - so the contours are cut at every crossing, the
    /// regions they bound are walked out, and the rule is asked once per region.</para></summary>
    public static class PlanarFill
    {
        private const double Tolerance = 1e-7;

        public static List<Vector3<double>> Fill(IReadOnlyList<Vector2<double>[]> rings, FillRule rule)
        {
            var result = new List<Vector3<double>>();

            if (rings == null || rings.Count == 0) return result;

            var faces = Faces(rings);

            if (faces.Count == 0) return result;

            var filled = new bool[faces.Count];

            for (var i = 0; i < faces.Count; i++)
            {
                var winding = Winding(Inside(faces[i]), rings);

                filled[i] = rule == FillRule.NonZero ? winding != 0 : (winding & 1) != 0;
            }

            for (var i = 0; i < faces.Count; i++)
            {
                if (!filled[i]) continue;

                // A filled region inside another filled one is already covered; an unfilled one directly inside this
                // region is a hole in it.
                var inside = false;
                List<IReadOnlyList<Vector2<double>>> holes = null;

                for (var j = 0; j < faces.Count; j++)
                {
                    if (i == j) continue;
                    if (!Contains(faces[j], Inside(faces[i]))) continue;

                    if (filled[j]) { inside = true; break; }
                }

                if (inside) continue;

                for (var j = 0; j < faces.Count; j++)
                {
                    if (i == j || filled[j]) continue;
                    if (!Contains(faces[i], Inside(faces[j]))) continue;
                    if (Nested(faces, filled, i, j)) continue;

                    (holes ??= new List<IReadOnlyList<Vector2<double>>>()).Add(faces[j]);
                }

                if (holes == null)
                {
                    result.AddRange(MathHelper.Polyline.IsConvex<double>(faces[i])
                        ? Triangulator.FanTriangulate(faces[i])
                        : Triangulator.EarcutTriangulate(faces[i]));
                }
                else
                {
                    result.AddRange(Triangulator.EarcutWithHoles(faces[i], holes));
                }
            }

            return result;
        }

        // Whether some OTHER region sits between i and the hole j - then j is that one's hole, not this one's.
        private static bool Nested(List<Vector2<double>[]> faces, bool[] filled, int i, int j)
        {
            var point = Inside(faces[j]);

            for (var k = 0; k < faces.Count; k++)
            {
                if (k == i || k == j) continue;
                if (!Contains(faces[k], point)) continue;
                if (Contains(faces[i], Inside(faces[k]))) return true;
            }

            return false;
        }

        /// <summary>The regions the contours bound: every contour cut at every crossing, then walked round.</summary>
        public static List<Vector2<double>[]> Faces(IReadOnlyList<Vector2<double>[]> rings)
        {
            var edges = Cut(rings);
            var faces = new List<Vector2<double>[]>();

            if (edges.Count < 3) return faces;

            // Out of each point, the edges leaving it - a walk chooses its next edge from here.
            var leaving = new Dictionary<Vector2<double>, List<(Vector2<double> To, int Id)>>();

            for (var i = 0; i < edges.Count; i++)
            {
                var (from, to) = edges[i];

                if (!leaving.TryGetValue(from, out var outgoing)) leaving[from] = outgoing = new List<(Vector2<double>, int)>();
                outgoing.Add((to, i));
            }

            var walked = new bool[edges.Count];

            for (var i = 0; i < edges.Count; i++)
            {
                if (walked[i]) continue;

                var ring = new List<Vector2<double>>();
                var at = i;

                while (!walked[at])
                {
                    walked[at] = true;
                    ring.Add(edges[at].From);

                    var next = Next(edges[at], leaving);

                    if (next < 0) break;

                    at = next;
                }

                // Every edge is laid down both ways, so every region is walked twice - once round its inside and once
                // round its outside. The inside walk is the one that comes out positive; the other is either the plane
                // around everything or the same region said inside out.
                if (ring.Count >= 3 && Area(ring) > Tolerance) faces.Add(ring.ToArray());
            }

            return faces;
        }

        // The next edge round a junction: the FIRST one clockwise from the way back, which is what keeps a walk
        // hugging one region's border instead of cutting across into another.
        private static int Next((Vector2<double> From, Vector2<double> To) came, Dictionary<Vector2<double>, List<(Vector2<double> To, int Id)>> leaving)
        {
            if (!leaving.TryGetValue(came.To, out var outgoing)) return -1;

            var back = Math.Atan2(came.From.Y - came.To.Y, came.From.X - came.To.X);

            var best = -1;
            var bestTurn = double.MaxValue;

            foreach (var (to, id) in outgoing)
            {
                if (Same(to, came.From) && outgoing.Count > 1) continue;   // straight back, unless it is the only way

                var away = Math.Atan2(to.Y - came.To.Y, to.X - came.To.X);
                var turn = back - away;

                while (turn <= Tolerance) turn += Math.PI * 2;
                while (turn > Math.PI * 2) turn -= Math.PI * 2;

                if (turn >= bestTurn) continue;

                bestTurn = turn;
                best = id;
            }

            if (best >= 0) return best;

            // Only the way back was on offer - a dead end, which a border may have.
            foreach (var (to, id) in outgoing)
            {
                if (Same(to, came.From)) return id;
            }

            return -1;
        }

        // Every ring's edges, cut at every point where they meet another - and each edge laid down BOTH ways, since a
        // region can be bordered from either side.
        private static List<(Vector2<double> From, Vector2<double> To)> Cut(IReadOnlyList<Vector2<double>[]> rings)
        {
            var pieces = new List<(Vector2<double> From, Vector2<double> To)>();
            var whole = new List<(Vector2<double> From, Vector2<double> To)>();

            foreach (var ring in rings)
            {
                if (ring == null || ring.Length < 3) continue;

                for (var i = 0; i < ring.Length; i++)
                {
                    var from = Snap(ring[i]);
                    var to = Snap(ring[(i + 1) % ring.Length]);

                    if (!Same(from, to)) whole.Add((from, to));
                }
            }

            for (var i = 0; i < whole.Count; i++)
            {
                var (from, to) = whole[i];
                var along = new List<(double At, Vector2<double> Point)> { (0, from), (1, to) };

                for (var j = 0; j < whole.Count; j++)
                {
                    if (i == j) continue;

                    var other = whole[j];

                    if (!Crossing(from, to, other.From, other.To, out var point)) continue;

                    var span = to - from;
                    var at = Math.Abs(span.X) >= Math.Abs(span.Y)
                        ? (point.X - from.X) / span.X
                        : (point.Y - from.Y) / span.Y;

                    if (at > Tolerance && at < 1 - Tolerance) along.Add((at, Snap(point)));
                }

                along.Sort((a, b) => a.At.CompareTo(b.At));

                for (var k = 0; k + 1 < along.Count; k++)
                {
                    var a = along[k].Point;
                    var b = along[k + 1].Point;

                    if (Same(a, b)) continue;

                    pieces.Add((a, b));
                    pieces.Add((b, a));
                }
            }

            // The same piece can arrive from two contours that share an edge; one copy of each direction is enough.
            var once = new HashSet<(Vector2<double>, Vector2<double>)>();
            var result = new List<(Vector2<double> From, Vector2<double> To)>();

            foreach (var piece in pieces)
            {
                if (once.Add(piece)) result.Add(piece);
            }

            return result;
        }

        // Where two segments meet, INCLUDING a touch at an end - a contour cut only at proper crossings leaves a
        // junction the walk cannot turn at.
        private static bool Crossing(Vector2<double> a1, Vector2<double> a2, Vector2<double> b1, Vector2<double> b2, out Vector2<double> point)
        {
            point = default;

            var r = a2 - a1;
            var s = b2 - b1;
            var denominator = r.X * s.Y - r.Y * s.X;

            if (Math.Abs(denominator) < 1e-12) return false;   // parallel: any overlap is handled by the shared ends

            var t = ((b1.X - a1.X) * s.Y - (b1.Y - a1.Y) * s.X) / denominator;
            var u = ((b1.X - a1.X) * r.Y - (b1.Y - a1.Y) * r.X) / denominator;

            if (t < -Tolerance || t > 1 + Tolerance || u < -Tolerance || u > 1 + Tolerance) return false;

            point = new Vector2<double>(a1.X + t * r.X, a1.Y + t * r.Y);
            return true;
        }

        // How many times the ORIGINAL contours wind round a point: a horizontal ray to the right, +1 for a contour
        // crossing it upwards and -1 downwards. Asked of the originals, not of the pieces - the pieces no longer say
        // which way anything ran.
        private static int Winding(Vector2<double> point, IReadOnlyList<Vector2<double>[]> rings)
        {
            var winding = 0;

            foreach (var ring in rings)
            {
                if (ring == null || ring.Length < 3) continue;

                for (var i = 0; i < ring.Length; i++)
                {
                    var a = ring[i];
                    var b = ring[(i + 1) % ring.Length];

                    var up = a.Y <= point.Y && b.Y > point.Y;
                    var down = b.Y <= point.Y && a.Y > point.Y;

                    if (!up && !down) continue;
                    if (a.X + (point.Y - a.Y) / (b.Y - a.Y) * (b.X - a.X) <= point.X) continue;

                    winding += up ? 1 : -1;
                }
            }

            return winding;
        }

        /// <summary>A point in the region a ring BORDERS - just inside one of its own edges.
        /// <para>Not the middle of the ring: a region with a hole in it is still bounded by its outer ring alone, and
        /// the middle of that ring is in the hole. Asked there, a body with a hole reads as unfilled and disappears.
        /// </para></summary>
        private static Vector2<double> Inside(IReadOnlyList<Vector2<double>> ring)
        {
            for (var i = 0; i < ring.Count; i++)
            {
                var a = ring[i];
                var b = ring[(i + 1) % ring.Count];
                var along = b - a;
                var length = along.Length();

                if (length <= Tolerance) continue;

                // The left of the way round is the inside, the ring being wound positively.
                var into = new Vector2<double>(-along.Y / length, along.X / length) * (length * 1e-4);
                var point = new Vector2<double>((a.X + b.X) / 2 + into.X, (a.Y + b.Y) / 2 + into.Y);

                if (Contains(ring, point)) return point;
            }

            double x = 0, y = 0;

            foreach (var point in ring) { x += point.X; y += point.Y; }

            return new Vector2<double>(x / ring.Count, y / ring.Count);
        }

        private static bool Contains(IReadOnlyList<Vector2<double>> ring, Vector2<double> point)
        {
            var inside = false;

            for (int i = 0, j = ring.Count - 1; i < ring.Count; j = i++)
            {
                if (ring[i].Y > point.Y == ring[j].Y > point.Y) continue;

                var at = (ring[j].X - ring[i].X) * (point.Y - ring[i].Y) / (ring[j].Y - ring[i].Y) + ring[i].X;

                if (point.X < at) inside = !inside;
            }

            return inside;
        }

        private static double Area(IReadOnlyList<Vector2<double>> ring)
        {
            double sum = 0;

            for (var i = 0; i < ring.Count; i++)
            {
                var a = ring[i];
                var b = ring[(i + 1) % ring.Count];

                sum += a.X * b.Y - b.X * a.Y;
            }

            return sum / 2;
        }

        private static Vector2<double> Snap(Vector2<double> point) =>
            new(Math.Round(point.X, 6), Math.Round(point.Y, 6));

        private static bool Same(Vector2<double> a, Vector2<double> b) =>
            Math.Abs(a.X - b.X) <= Tolerance && Math.Abs(a.Y - b.Y) <= Tolerance;
    }
}
