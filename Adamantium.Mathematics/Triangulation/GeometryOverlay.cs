using System;
using System.Collections.Generic;

namespace Adamantium.Mathematics.Triangulation;

/// <summary>
/// Two areas - closed rings, each set under its fill rule - combined by a rule of which side is filled. Points nearer
/// than a ten-billionth of the areas' size - or than a few hundred steps of the doubles that far from the origin - are
/// one point, and an end that near another edge lies on it: what is that close in the shapes given is meant to meet;
/// the points themselves stay where they are. Every edge is cut wherever another crosses or touches it, along a shared
/// edge too, again until no cut makes another, and the pieces lying on one another are made one, adding up how often,
/// and which way, each area's rings run along it. The pieces bound the faces of the plane they cut; one ray to each
/// connected set of pieces gives the winding around it, and crossing a piece changes the winding by the piece's runs,
/// so every face gets its winding from its neighbour's - the same on both sides of every piece. A piece is kept where
/// the rule fills one side and not the other, turned so the filled side is on its left, and the pieces are joined into
/// closed rings, each turning at a vertex to the first piece clockwise from the one it came in by, so rings meeting at
/// a point part there; a ring that passes a point twice is parted into simple rings. Rings of fewer than two points,
/// or with a coordinate not a finite number, are left out.
/// </summary>
public static class GeometryOverlay
{
    /// <summary>The rings bounding where <paramref name="fills"/> says the area is filled, given whether a point is in
    /// the first area and the second; each ring simple, crossing no other, its filled side on its left. An area is its
    /// rings under its rule. The rule must leave empty what is in neither area, or the result has no bound.</summary>
    public static List<Vector2<double>[]> Combine((IReadOnlyList<Vector2<double>[]> Rings, FillRule Rule) first,
        (IReadOnlyList<Vector2<double>[]> Rings, FillRule Rule) second, Func<bool, bool, bool> fills)
    {
        ArgumentNullException.ThrowIfNull(first.Rings);
        ArgumentNullException.ThrowIfNull(second.Rings);
        ArgumentNullException.ThrowIfNull(fills);
        if (fills(false, false))
        {
            throw new ArgumentException("What is in neither area must not be filled: the result would have no bound.",
                nameof(fills));
        }

        var exponent = RingMath.Scale(Math.Max(RingMath.Largest(first.Rings), RingMath.Largest(second.Rings)));
        if (exponent == 0)
        {
            return OverlayGraph.Combine(first, second, fills);
        }

        var rings = OverlayGraph.Combine((RingMath.Scaled(first.Rings, exponent), first.Rule),
            (RingMath.Scaled(second.Rings, exponent), second.Rule), fills);
        foreach (var ring in rings)
        {
            for (var i = 0; i < ring.Length; i++) ring[i] = RingMath.Scaled(ring[i], -exponent);
        }

        return rings;
    }

    /// <summary>The rings bounding what <paramref name="rings"/> fill under <paramref name="rule"/> - crossing,
    /// touching or lying on one another as they may - each simple, crossing no other, its filled side on its left.
    /// </summary>
    public static List<Vector2<double>[]> Outline(IReadOnlyList<Vector2<double>[]> rings, FillRule rule) =>
        Combine((rings, rule), ([], FillRule.EvenOdd), static (inFirst, _) => inFirst);
}
