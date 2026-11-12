using System.Collections.Generic;

namespace Adamantium.Mathematics.Triangulation;

/// <summary>
/// Rings that cross nowhere - touching at points as they may - asked which ring lies straight around each. A line swept
/// across x (<see cref="ContactSweep"/>) reaches a ring first at its leftmost corner; once every edge there is on the
/// line, the edge of another ring just below the ring's lowest edge there tells: the ring around is that edge's ring
/// when its inside lies above the edge, else the ring around that one - already known, its leftmost corner lying further
/// left.
/// </summary>
internal sealed class NestingSweep
{
    private readonly IReadOnlyList<Vector2<double>[]> rings;

    /// <summary>The sweep over <paramref name="rings"/>.</summary>
    public NestingSweep(IReadOnlyList<Vector2<double>[]> rings)
    {
        this.rings = rings;
    }

    /// <summary>The ring straight around each ring, or -1; <paramref name="order"/> gets the rings in an order where
    /// each comes after the ring around it.</summary>
    public int[] Parents(List<int> order)
    {
        var count = rings.Count;
        var leftmost = new Vector2<double>[count];
        var segments = new List<(Vector2<double> Left, Vector2<double> Right)>();
        var owner = new List<int>();
        var insideAbove = new List<bool>();
        for (var i = 0; i < count; i++)
        {
            var ring = rings[i];
            var counterclockwise = MathHelper.RingMath.SignedArea(ring) >= 0;
            leftmost[i] = ring[0];
            for (var k = 0; k < ring.Length; k++)
            {
                var (a, b) = (ring[k], ring[(k + 1) % ring.Length]);
                if (a.X < leftmost[i].X || (a.X == leftmost[i].X && a.Y < leftmost[i].Y)) leftmost[i] = a;
                segments.Add(ContactSweep.Ordered(a, b));
                owner.Add(i);
                insideAbove.Add(b.X > a.X == counterclockwise);
            }
        }

        var parent = new int[count];
        var placed = new bool[count];
        var joinedThere = new List<int>();
        var waiting = new List<(int Lowest, int Ring)>();
        var slot = new Dictionary<int, int>();
        var sweep = new ContactSweep(segments, 0);
        sweep.Run(static (_, _) => false, joined: (s, _) =>
        {
            var ring = owner[s];
            if (segments[s].Left == leftmost[ring] && !placed[ring])
            {
                joinedThere.Add(s);
            }
        }, passed: _ =>
        {
            if (joinedThere.Count == 0) return;
            var line = sweep.Line;
            waiting.Clear();
            slot.Clear();
            foreach (var s in joinedThere)
            {
                var ring = owner[s];
                var at = sweep.Position(s);
                if (!slot.TryGetValue(ring, out var known))
                {
                    slot[ring] = waiting.Count;
                    waiting.Add((at, ring));
                }
                else if (at < waiting[known].Lowest)
                {
                    waiting[known] = (at, ring);
                }

                placed[ring] = true;
            }

            joinedThere.Clear();
            waiting.Sort((a, b) => a.Lowest.CompareTo(b.Lowest));
            foreach (var (lowest, ring) in waiting)
            {
                var j = lowest - 1;
                while (j >= 0 && (owner[line[j]] == ring || segments[line[j]].Left.X == segments[line[j]].Right.X)) j--;
                if (j < 0)
                {
                    parent[ring] = -1;
                }
                else
                {
                    var under = line[j];
                    parent[ring] = insideAbove[under] ? owner[under] : parent[owner[under]];
                }

                order.Add(ring);
            }
        });

        return parent;
    }
}
