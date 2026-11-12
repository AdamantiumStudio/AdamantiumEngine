using System;
using System.Collections.Generic;

namespace Adamantium.Mathematics.Triangulation;

/// <summary>
/// Combines areas bounded by rings that may cross, touch or overlap, and returns simple, non-crossing rings with the
/// filled side on the left. Points closer than a ten-billionth of the areas' size, or than a weld distance given, are
/// treated as one. Rings with fewer
/// than two points or a non-finite coordinate are ignored. Rings that keep clear of one another are combined by how
/// they nest, without cutting.
/// </summary>
public static class GeometryOverlay
{
    /// <summary>The rings bounding what <paramref name="mode"/> keeps of <paramref name="first"/> and
    /// <paramref name="second"/>; each ring simple, crossing no other, its filled side on its left.</summary>
    public static List<Vector2<double>[]> Combine(RingArea first, RingArea second, AreaCombineMode mode) =>
        Combine(first, second, mode, 0);

    /// <summary><see cref="Combine(RingArea, RingArea, AreaCombineMode)"/> with points nearer than
    /// <paramref name="weld"/> taken as one, and a corner that near an edge as lying on it - for coordinates rounded to
    /// that step, which would otherwise leave seams and slivers where the shapes meet. The least distance stays a
    /// ten-billionth of the areas' size.</summary>
    public static List<Vector2<double>[]> Combine(RingArea first, RingArea second, AreaCombineMode mode, double weld)
    {
        ArgumentNullException.ThrowIfNull(first.Rings, nameof(first));
        ArgumentNullException.ThrowIfNull(second.Rings, nameof(second));
        if (!Enum.IsDefined(mode))
        {
            throw new ArgumentOutOfRangeException(nameof(mode), mode, null);
        }

        if (!(weld >= 0) || double.IsInfinity(weld))
        {
            throw new ArgumentOutOfRangeException(nameof(weld), weld, null);
        }

        var exponent = MathHelper.RingMath.Scale(Math.Max(MathHelper.RingMath.Largest(first.Rings),
            MathHelper.RingMath.Largest(second.Rings)));
        if (exponent == 0)
        {
            return CombineScaled(first, second, mode, weld);
        }

        var rings = CombineScaled(new RingArea(MathHelper.RingMath.Scaled(first.Rings, exponent), first.Rule),
            new RingArea(MathHelper.RingMath.Scaled(second.Rings, exponent), second.Rule), mode, Math.ScaleB(weld, exponent));
        foreach (var ring in rings)
        {
            for (var i = 0; i < ring.Length; i++) ring[i] = MathHelper.RingMath.Scaled(ring[i], -exponent);
        }

        return rings;
    }

    private static List<Vector2<double>[]> CombineScaled(RingArea first, RingArea second, AreaCombineMode mode, double weld)
    {
        var all = new List<Vector2<double>[]>(first.Rings.Count + second.Rings.Count);
        all.AddRange(first.Rings);
        all.AddRange(second.Rings);
        return RingContacts.AnyMeet(all, ClearReach, weld)
            ? OverlayGraph.Combine(first, second, mode, weld)
            : NestedCombine.Combine(first, second, mode);
    }

    private const double ClearReach = 2;

    /// <summary>The rings bounding what <paramref name="rings"/> fill under <paramref name="rule"/> - crossing,
    /// touching or lying on one another as they may - each simple, crossing no other, its filled side on its left.
    /// </summary>
    public static List<Vector2<double>[]> Outline(IReadOnlyList<Vector2<double>[]> rings, FillRule rule) =>
        Combine(new RingArea(rings, rule), new RingArea([], FillRule.EvenOdd), AreaCombineMode.Union);
}
