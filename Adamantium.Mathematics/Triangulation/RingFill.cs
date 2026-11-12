using System;
using System.Collections.Generic;

namespace Adamantium.Mathematics.Triangulation;

/// <summary>
/// Triangulates closed rings by a fill rule, whether or not they cross, touch or overlap. Rings that meet go through
/// <see cref="GeometryOverlay.Outline"/> first.
/// </summary>
public static class RingFill
{
    /// <summary>The triangles covering what <paramref name="rings"/> fill under <paramref name="rule"/>, three points to a
    /// triangle, each turning the way <see cref="MathHelper.Polyline.IsClockwise{T}"/> calls clockwise; none of no area.
    /// Rings of fewer than three points, or with a coordinate not a finite number, are left out.</summary>
    public static List<Vector3<double>> Fill(IReadOnlyList<Vector2<double>[]> rings, FillRule rule)
    {
        ArgumentNullException.ThrowIfNull(rings);
        var usable = new List<Vector2<double>[]>(rings.Count);
        foreach (var ring in rings)
        {
            if (ring != null && ring.Length >= 3 && MathHelper.RingMath.Finite(ring)) usable.Add(ring);
        }

        if (usable.Count == 0) return [];
        var exponent = MathHelper.RingMath.Scale(MathHelper.RingMath.Largest(usable));
        if (exponent == 0) return FillUsable(usable, rule);
        var triangles = FillUsable(new List<Vector2<double>[]>(MathHelper.RingMath.Scaled(usable, exponent)), rule);
        for (var i = 0; i < triangles.Count; i++) triangles[i] = MathHelper.RingMath.Scaled(triangles[i], -exponent);
        return triangles;
    }

    private static List<Vector3<double>> FillUsable(List<Vector2<double>[]> usable, FillRule rule)
    {
        if (usable.Count == 1 && MathHelper.Polyline.IsConvex<double>(usable[0])) return Triangulator.FanTriangulate(usable[0]);
        if (RingContacts.AnyMeet(usable))
        {
            return FaceFill.Fill(OverlayGraph.Combine(new RingArea(usable, rule), new RingArea([], FillRule.EvenOdd),
                AreaCombineMode.Union));
        }
        if (usable.Count > 1) return FillNested(usable, rule);
        var alone = new List<Vector3<double>>();
        FaceFill.FillFace(usable[0], [], alone);
        return alone;
    }

    private static List<Vector3<double>> FillNested(List<Vector2<double>[]> rings, FillRule rule)
    {
        var count = rings.Count;
        var order = new List<int>(count);
        var parent = new NestingSweep(rings).Parents(order);
        var depth = new int[count];
        var winding = new int[count];
        foreach (var ring in order)
        {
            var around = parent[ring];
            depth[ring] = around < 0 ? 0 : depth[around] + 1;
            winding[ring] = (MathHelper.RingMath.SignedArea(rings[ring]) >= 0 ? 1 : -1) + (around < 0 ? 0 : winding[around]);
        }

        var children = new List<int>[count];
        for (var i = 0; i < count; i++)
        {
            if (parent[i] >= 0) (children[parent[i]] ??= []).Add(i);
        }

        var filled = new bool[count];
        for (var i = 0; i < count; i++) filled[i] = rule == FillRule.NonZero ? winding[i] != 0 : (depth[i] & 1) == 0;

        var result = new List<Vector3<double>>();
        var below = new Stack<int>();
        for (var i = 0; i < count; i++)
        {
            if (!filled[i] || (parent[i] >= 0 && filled[parent[i]])) continue;

            var holes = new List<Vector2<double>[]>();
            below.Push(i);
            while (below.Count > 0)
            {
                var ring = below.Pop();
                if (children[ring] == null) continue;
                foreach (var child in children[ring])
                {
                    if (filled[child]) below.Push(child);
                    else holes.Add(rings[child]);
                }
            }

            if (holes.Count == 0 && MathHelper.Polyline.IsConvex<double>(rings[i]))
                result.AddRange(Triangulator.FanTriangulate(rings[i]));
            else
                FaceFill.FillFace(rings[i], holes, result);
        }

        return result;
    }
}
