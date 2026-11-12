using System;
using System.Collections.Generic;

namespace Adamantium.Mathematics.Triangulation;

/// <summary>An area of the plane: closed rings, each last point joined to its first, filled under a rule.</summary>
public readonly struct RingArea
{
    /// <summary>An area of <paramref name="rings"/> filled under <paramref name="rule"/>.</summary>
    public RingArea(IReadOnlyList<Vector2<double>[]> rings, FillRule rule)
    {
        ArgumentNullException.ThrowIfNull(rings);
        Rings = rings;
        Rule = rule;
    }

    /// <summary>The rings bounding the area.</summary>
    public IReadOnlyList<Vector2<double>[]> Rings { get; }

    /// <summary>Which points the rings fill.</summary>
    public FillRule Rule { get; }
}
