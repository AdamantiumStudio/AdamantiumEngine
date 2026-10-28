using System;
using System.Numerics;

namespace Adamantium.Mathematics.Typed;

/// <summary>Makes <see cref="BoundingBox{T}"/>s.</summary>
public static class BoundingBox
{
    /// <summary>The box from <paramref name="minimum"/> to <paramref name="maximum"/>.</summary>
    public static BoundingBox<T> Create<T>(Vector3<T> minimum, Vector3<T> maximum) where T : unmanaged, IFloatingPointIeee754<T> =>
        new(minimum, maximum);

    /// <summary>The smallest box holding every one of <paramref name="points"/>.</summary>
    /// <exception cref="ArgumentException"><paramref name="points"/> is empty.</exception>
    public static BoundingBox<T> CreateFromPoints<T>(ReadOnlySpan<Vector3<T>> points) where T : unmanaged, IFloatingPointIeee754<T>
    {
        if (points.IsEmpty)
        {
            throw new ArgumentException("A bounding box needs at least one point.", nameof(points));
        }

        var (minimum, maximum) = (points[0], points[0]);
        foreach (var point in points[1..])
        {
            minimum = Vector3<T>.Min(minimum, point);
            maximum = Vector3<T>.Max(maximum, point);
        }

        return new BoundingBox<T>(minimum, maximum);
    }

    /// <summary>The smallest box holding <paramref name="sphere"/>.</summary>
    public static BoundingBox<T> CreateFromSphere<T>(BoundingSphere<T> sphere) where T : unmanaged, IFloatingPointIeee754<T> =>
        new(sphere.Center - new Vector3<T>(sphere.Radius), sphere.Center + new Vector3<T>(sphere.Radius));

    /// <summary>The smallest box holding both.</summary>
    public static BoundingBox<T> CreateMerged<T>(BoundingBox<T> value1, BoundingBox<T> value2) where T : unmanaged, IFloatingPointIeee754<T> =>
        new(Vector3<T>.Min(value1.Minimum, value2.Minimum), Vector3<T>.Max(value1.Maximum, value2.Maximum));
}
