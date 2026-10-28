using System;
using System.Numerics;

namespace Adamantium.Mathematics.Typed;

/// <summary>Makes <see cref="BoundingSphere{T}"/>s.</summary>
public static class BoundingSphere
{
    /// <summary>The sphere of <paramref name="radius"/> around <paramref name="center"/>.</summary>
    public static BoundingSphere<T> Create<T>(Vector3<T> center, T radius) where T : unmanaged, IFloatingPointIeee754<T> =>
        new(center, radius);

    /// <summary>A sphere holding every one of <paramref name="points"/>: around their average, out to the farthest - not
    /// the smallest such sphere, but a quick one.</summary>
    /// <exception cref="ArgumentException"><paramref name="points"/> is empty.</exception>
    public static BoundingSphere<T> CreateFromPoints<T>(ReadOnlySpan<Vector3<T>> points) where T : unmanaged, IFloatingPointIeee754<T>
    {
        if (points.IsEmpty)
        {
            throw new ArgumentException("A bounding sphere needs at least one point.", nameof(points));
        }

        var center = Vector3<T>.Zero;
        foreach (var point in points)
        {
            center += point;
        }

        center /= T.CreateTruncating(points.Length);
        var farthest = T.Zero;
        foreach (var point in points)
        {
            farthest = T.Max(farthest, Vector3<T>.DistanceSquared(center, point));
        }

        return new BoundingSphere<T>(center, T.Sqrt(farthest));
    }

    /// <summary>The smallest sphere holding <paramref name="box"/>.</summary>
    public static BoundingSphere<T> CreateFromBox<T>(BoundingBox<T> box) where T : unmanaged, IFloatingPointIeee754<T> =>
        new(box.Center, Vector3<T>.Distance(box.Minimum, box.Maximum) * T.CreateTruncating(0.5));

    /// <summary>The smallest sphere holding both.</summary>
    public static BoundingSphere<T> CreateMerged<T>(BoundingSphere<T> value1, BoundingSphere<T> value2)
        where T : unmanaged, IFloatingPointIeee754<T>
    {
        var difference = value2.Center - value1.Center;
        var length = difference.Length();
        var (radius1, radius2) = (value1.Radius, value2.Radius);
        if (radius1 + radius2 >= length)
        {
            if (radius1 - radius2 >= length)
            {
                return value1;
            }

            if (radius2 - radius1 >= length)
            {
                return value2;
            }
        }

        var direction = difference * (T.One / length);
        var near = T.Min(-radius1, length - radius2);
        var radius = (T.Max(radius1, length + radius2) - near) * T.CreateTruncating(0.5);
        return new BoundingSphere<T>(value1.Center + direction * (radius + near), radius);
    }
}
