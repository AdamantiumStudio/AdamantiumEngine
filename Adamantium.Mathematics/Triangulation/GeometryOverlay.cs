using System;
using System.Collections.Generic;

namespace Adamantium.Mathematics.Triangulation;

/// <summary>
/// Combines areas bounded by rings that may cross, touch or overlap, and returns simple, non-crossing rings with the
/// filled side on the left. Points closer than a ten-billionth of the areas' size are treated as one. Rings with fewer
/// than two points or a non-finite coordinate are ignored.
/// </summary>
public static class GeometryOverlay
{
    /// <summary>The rings bounding what <paramref name="mode"/> keeps of <paramref name="first"/> and
    /// <paramref name="second"/>; each ring simple, crossing no other, its filled side on its left.</summary>
    public static List<Vector2<double>[]> Combine(RingArea first, RingArea second, AreaCombineMode mode)
    {
        ArgumentNullException.ThrowIfNull(first.Rings, nameof(first));
        ArgumentNullException.ThrowIfNull(second.Rings, nameof(second));
        if (!Enum.IsDefined(mode))
        {
            throw new ArgumentOutOfRangeException(nameof(mode), mode, null);
        }

        var exponent = MathHelper.RingMath.Scale(Math.Max(MathHelper.RingMath.Largest(first.Rings),
            MathHelper.RingMath.Largest(second.Rings)));
        if (exponent == 0)
        {
            return OverlayGraph.Combine(first, second, mode);
        }

        var rings = OverlayGraph.Combine(new RingArea(MathHelper.RingMath.Scaled(first.Rings, exponent), first.Rule),
            new RingArea(MathHelper.RingMath.Scaled(second.Rings, exponent), second.Rule), mode);
        foreach (var ring in rings)
        {
            for (var i = 0; i < ring.Length; i++) ring[i] = MathHelper.RingMath.Scaled(ring[i], -exponent);
        }

        return rings;
    }

    /// <summary>The rings bounding what <paramref name="rings"/> fill under <paramref name="rule"/> - crossing,
    /// touching or lying on one another as they may - each simple, crossing no other, its filled side on its left.
    /// </summary>
    public static List<Vector2<double>[]> Outline(IReadOnlyList<Vector2<double>[]> rings, FillRule rule) =>
        Combine(new RingArea(rings, rule), new RingArea([], FillRule.EvenOdd), AreaCombineMode.Union);
}
