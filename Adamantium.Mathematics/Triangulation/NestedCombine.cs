using System;
using System.Collections.Generic;

namespace Adamantium.Mathematics.Triangulation;

/// <summary>
/// <see cref="GeometryOverlay.Combine"/> for rings that keep clear of one another (<see cref="RingContacts.AnyMeet"/>).
/// The ring straight around each (<see cref="NestingSweep"/>) gives the windings just outside it, its own turn the ones
/// just inside; a ring bounds the result where the mode keeps one side of it and not the other.
/// </summary>
internal static class NestedCombine
{
    /// <summary>The rings bounding what <paramref name="mode"/> keeps, each a new array turned so the kept side is on
    /// its left; the rings of both areas must keep clear of one another.</summary>
    public static List<Vector2<double>[]> Combine(RingArea first, RingArea second, AreaCombineMode mode)
    {
        var rings = new List<Vector2<double>[]>(first.Rings.Count + second.Rings.Count);
        var areas = new List<int>(rings.Capacity);
        Collect(first.Rings, 0, rings, areas);
        Collect(second.Rings, 1, rings, areas);

        var order = new List<int>(rings.Count);
        var parent = new NestingSweep(rings).Parents(order);
        var windingFirst = new int[rings.Count];
        var windingSecond = new int[rings.Count];
        var result = new List<Vector2<double>[]>();
        foreach (var ring in order)
        {
            var around = parent[ring];
            var (outFirst, outSecond) = around < 0 ? (0, 0) : (windingFirst[around], windingSecond[around]);
            var turn = MathHelper.RingMath.SignedArea(rings[ring]) >= 0 ? 1 : -1;
            windingFirst[ring] = outFirst + (areas[ring] == 0 ? turn : 0);
            windingSecond[ring] = outSecond + (areas[ring] == 1 ? turn : 0);

            var keptInside = OverlayGraph.Keeps(mode, OverlayGraph.Inside(windingFirst[ring], first.Rule),
                OverlayGraph.Inside(windingSecond[ring], second.Rule));
            var keptOutside = OverlayGraph.Keeps(mode, OverlayGraph.Inside(outFirst, first.Rule),
                OverlayGraph.Inside(outSecond, second.Rule));
            if (keptInside == keptOutside)
            {
                continue;
            }

            var copy = (Vector2<double>[])rings[ring].Clone();
            if (turn > 0 != keptInside)
            {
                Array.Reverse(copy);
            }

            result.Add(copy);
        }

        return result;
    }

    private static void Collect(IReadOnlyList<Vector2<double>[]> given, int area, List<Vector2<double>[]> rings, List<int> areas)
    {
        foreach (var ring in given)
        {
            if (ring is { Length: >= 3 } && MathHelper.RingMath.Finite(ring))
            {
                rings.Add(ring);
                areas.Add(area);
            }
        }
    }
}
