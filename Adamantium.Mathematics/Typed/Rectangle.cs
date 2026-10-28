using System;
using System.Numerics;

namespace Adamantium.Mathematics.Typed;

/// <summary>Makes <see cref="Rectangle{T}"/>s.</summary>
public static class Rectangle
{
    /// <summary>A rectangle at <paramref name="x"/>, <paramref name="y"/> of <paramref name="width"/> by
    /// <paramref name="height"/>.</summary>
    public static Rectangle<T> Create<T>(T x, T y, T width, T height) where T : unmanaged, INumber<T> => new(x, y, width, height);

    /// <summary>A rectangle at <paramref name="position"/> of <paramref name="size"/>.</summary>
    public static Rectangle<T> Create<T>(Vector2<T> position, Size<T> size) where T : unmanaged, INumber<T> => new(position, size);

    /// <summary>The rectangle from <paramref name="left"/>, <paramref name="top"/> to <paramref name="right"/>,
    /// <paramref name="bottom"/>.</summary>
    public static Rectangle<T> CreateFromCorners<T>(T left, T top, T right, T bottom) where T : unmanaged, INumber<T> =>
        new(left, top, right - left, bottom - top);

    /// <summary>The smallest rectangle holding every one of <paramref name="points"/>.</summary>
    /// <exception cref="ArgumentException"><paramref name="points"/> is empty.</exception>
    public static Rectangle<T> CreateBounding<T>(ReadOnlySpan<Vector2<T>> points) where T : unmanaged, INumber<T>
    {
        if (points.IsEmpty)
        {
            throw new ArgumentException("A bounding rectangle needs at least one point.", nameof(points));
        }

        var (minimum, maximum) = (points[0], points[0]);
        foreach (var point in points[1..])
        {
            minimum = Vector2<T>.Min(minimum, point);
            maximum = Vector2<T>.Max(maximum, point);
        }

        return CreateFromCorners(minimum.X, minimum.Y, maximum.X, maximum.Y);
    }
}
