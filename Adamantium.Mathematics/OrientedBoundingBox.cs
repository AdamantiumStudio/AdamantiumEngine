using System;
using System.Numerics;

namespace Adamantium.Mathematics;

/// <summary>Makes <see cref="OrientedBoundingBox{T}"/>s.</summary>
public static class OrientedBoundingBox
{
    /// <summary>A box around <paramref name="center"/>, <paramref name="halfExtent"/> to each side along its own axes,
    /// turned by <paramref name="orientation"/>.</summary>
    public static OrientedBoundingBox<T> Create<T>(Vector3<T> center, Vector3<T> halfExtent, Quaternion<T> orientation)
        where T : unmanaged, IFloatingPointIeee754<T> => new(center, halfExtent, orientation);

    /// <summary>The box of <paramref name="box"/>, along the world's axes; one endless both ways along an axis is
    /// centered on zero there.</summary>
    public static OrientedBoundingBox<T> CreateFromBox<T>(BoundingBox<T> box) where T : unmanaged, IFloatingPointIeee754<T>
    {
        var two = T.One + T.One;
        var (low, high) = (box.Minimum / two, box.Maximum / two);
        var center = low + high;
        for (var i = 0; i < 3; i++)
        {
            if (T.IsInfinity(low[i]) && T.IsInfinity(high[i]) && low[i] != high[i])
            {
                center[i] = T.Zero;
            }
        }

        return new OrientedBoundingBox<T>(center, high - low, Quaternion<T>.Identity);
    }

    /// <summary>The smallest box along the world's axes holding every one of <paramref name="points"/>.</summary>
    /// <exception cref="ArgumentException"><paramref name="points"/> is empty.</exception>
    public static OrientedBoundingBox<T> CreateFromPoints<T>(ReadOnlySpan<Vector3<T>> points) where T : unmanaged, IFloatingPointIeee754<T> =>
        CreateFromBox(BoundingBox.CreateFromPoints(points));

    /// <summary>The smallest box along the world's axes holding both; one that holds nothing adds nothing.</summary>
    public static OrientedBoundingBox<T> CreateMerged<T>(OrientedBoundingBox<T> value1, OrientedBoundingBox<T> value2)
        where T : unmanaged, IFloatingPointIeee754<T> =>
        value1.IsEmpty ? CreateFromBox(value2.GetBoundingBox())
        : value2.IsEmpty ? CreateFromBox(value1.GetBoundingBox())
        : CreateFromBox(BoundingBox.CreateMerged(value1.GetBoundingBox(), value2.GetBoundingBox()));
}
