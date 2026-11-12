using System;
using System.Collections.Generic;

namespace Adamantium.Mathematics.Triangulation;

/// <summary>
/// Fills closed rings by a fill rule, whatever they do to one another. Rings that keep clear of each other
/// (<see cref="RingContacts.AnyMeet"/>) fill by how they nest: a line swept across x finds the ring straight around each
/// by the edge just below its leftmost corner - that ring if its inside lies above the edge, else the ring around that
/// one - so the winding inside every ring is the one around it and its own turn; each region a ring bounds less the
/// rings straight inside is filled or not by its winding, and filled regions next to each other are cut as one face.
/// Rings that cross, touch or lie along one another are laid over one another first
/// (<see cref="GeometryOverlay.Outline"/>), into the simple rings bounding what the rule fills, and the faces those
/// bound are filled. Each face is cut by clipping ears, or into the trapezoids between its edges where the clipping does
/// not cover it whole.
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
            if (ring != null && ring.Length >= 3 && RingMath.Finite(ring)) usable.Add(ring);
        }

        if (usable.Count == 0) return [];
        var exponent = RingMath.Scale(RingMath.Largest(usable));
        if (exponent == 0) return FillUsable(usable, rule);
        var triangles = FillUsable(new List<Vector2<double>[]>(RingMath.Scaled(usable, exponent)), rule);
        for (var i = 0; i < triangles.Count; i++) triangles[i] = RingMath.Scaled(triangles[i], -exponent);
        return triangles;
    }

    private static List<Vector3<double>> FillUsable(List<Vector2<double>[]> usable, FillRule rule)
    {
        if (usable.Count == 1 && MathHelper.Polyline.IsConvex<double>(usable[0])) return Triangulator.FanTriangulate(usable[0]);
        if (RingContacts.AnyMeet(usable)) return FaceFill.Fill(GeometryOverlay.Outline(usable, rule));
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
            winding[ring] = (RingMath.SignedArea(rings[ring]) >= 0 ? 1 : -1) + (around < 0 ? 0 : winding[around]);
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
