using System;
using System.Collections.Generic;

namespace Adamantium.Mathematics.Triangulation;

/// <summary>
/// Whether closed rings keep clear of one another and of themselves - the case where which ring lies in which, told by
/// any of its points, says what they fill. The edges are swept across (<see cref="ContactSweep"/>), so only edges that
/// come next to each other are tried, however long they are.
/// </summary>
public static class RingContacts
{
    private const double NearFraction = 1e-10;
    private const double PositionFraction = 2.5e-14;

    /// <summary>True when two edges of <paramref name="rings"/> cross, touch or come nearer than a ten-billionth of the
    /// rings' size (or than a few hundred steps of the doubles that far from the origin), or a corner is passed twice -
    /// but for neighbours in one ring meeting at their shared corner, unless one folds back along or near the other.
    /// A ring of two points runs back along itself, so it meets; rings of fewer, or with a coordinate not a finite
    /// number, are left out.</summary>
    public static bool AnyMeet(IReadOnlyList<Vector2<double>[]> rings) => AnyMeet(rings, 1, 0);

    /// <summary><see cref="AnyMeet(IReadOnlyList{Vector2{double}[]})"/> with the distance that counts as meeting no
    /// shorter than <paramref name="weld"/>, and then <paramref name="reach"/> times as long.</summary>
    internal static bool AnyMeet(IReadOnlyList<Vector2<double>[]> rings, double reach, double weld)
    {
        ArgumentNullException.ThrowIfNull(rings);
        var exponent = MathHelper.RingMath.Scale(MathHelper.RingMath.Largest(rings));
        rings = MathHelper.RingMath.Scaled(rings, exponent);
        weld = Math.ScaleB(weld, exponent);
        var segments = new List<(Vector2<double> Left, Vector2<double> Right)>();
        var edges = new List<(int Ring, int Index, int Count)>();
        var corners = new HashSet<Vector2<double>>();
        double minX = double.MaxValue, minY = double.MaxValue, maxX = double.MinValue, maxY = double.MinValue;
        for (var r = 0; r < rings.Count; r++)
        {
            var ring = rings[r];
            if (ring == null || ring.Length < 2 || !MathHelper.RingMath.Finite(ring)) continue;
            for (var i = 0; i < ring.Length; i++)
            {
                var (a, b) = (ring[i], ring[(i + 1) % ring.Length]);
                if (!corners.Add(a)) return true;
                (minX, minY, maxX, maxY) = (Math.Min(minX, a.X), Math.Min(minY, a.Y), Math.Max(maxX, a.X), Math.Max(maxY, a.Y));
                segments.Add(ContactSweep.Ordered(a, b));
                edges.Add((r, i, ring.Length));
            }
        }

        if (segments.Count == 0) return false;
        var position = Math.Max(Math.Max(Math.Abs(minX), Math.Abs(maxX)), Math.Max(Math.Abs(minY), Math.Abs(maxY)));
        var near = reach * Math.Max(Math.Max(Math.Max(maxX - minX, maxY - minY) * NearFraction, position * PositionFraction), weld);
        return new ContactSweep(segments, near).Run((first, second) => Meet(segments[first], segments[second], edges[first], edges[second], near));
    }

    private static bool Meet((Vector2<double> A, Vector2<double> B) one, (Vector2<double> A, Vector2<double> B) other,
        (int Ring, int Index, int Count) e, (int Ring, int Index, int Count) o, double near)
    {
        var (a1, b1) = one;
        var (a2, b2) = other;
        var neighbours = e.Ring == o.Ring && ((e.Index + 1) % e.Count == o.Index || (o.Index + 1) % o.Count == e.Index);
        if (neighbours)
        {
            var shared = a1 == a2 || a1 == b2 ? a1 : b1;
            var far1 = shared == a1 ? b1 : a1;
            var far2 = shared == a2 ? b2 : a2;
            return FoldsBack(shared, far1, far2, near) || Distance(far1, a2, b2) <= near || Distance(far2, a1, b1) <= near;
        }

        return Cross(a1, b1, a2, b2) || Distance(a1, a2, b2) <= near || Distance(b1, a2, b2) <= near ||
               Distance(a2, a1, b1) <= near || Distance(b2, a1, b1) <= near;
    }

    private static bool FoldsBack(Vector2<double> shared, Vector2<double> far1, Vector2<double> far2, double near)
    {
        var d1 = far1 - shared;
        var d2 = far2 - shared;
        var length1 = d1.Length();
        var length2 = d2.Length();
        if (length1 == 0 || length2 == 0) return true;
        return Math.Abs(d1.X * d2.Y - d1.Y * d2.X) <= near * Math.Max(length1, length2) && d1.X * d2.X + d1.Y * d2.Y > 0;
    }

    private static bool Cross(Vector2<double> p1, Vector2<double> p2, Vector2<double> p3, Vector2<double> p4)
    {
        var o1 = Side(p1, p2, p3);
        var o2 = Side(p1, p2, p4);
        var o3 = Side(p3, p4, p1);
        var o4 = Side(p3, p4, p2);
        return o1 * o2 < 0 && o3 * o4 < 0;
    }

    private static int Side(Vector2<double> a, Vector2<double> b, Vector2<double> c) =>
        Math.Sign((b.X - a.X) * (c.Y - a.Y) - (b.Y - a.Y) * (c.X - a.X));

    private static double Distance(Vector2<double> p, Vector2<double> a, Vector2<double> b)
    {
        var d = b - a;
        var lengthSquared = d.X * d.X + d.Y * d.Y;
        var t = lengthSquared > 0 ? Math.Clamp(((p.X - a.X) * d.X + (p.Y - a.Y) * d.Y) / lengthSquared, 0, 1) : 0;
        return (a + d * t - p).Length();
    }
}
