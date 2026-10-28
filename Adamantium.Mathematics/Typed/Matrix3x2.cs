using System.Numerics;

namespace Adamantium.Mathematics.Typed;

/// <summary>Makes <see cref="Matrix3x2{T}"/>s: 2D scales, turns, moves and skews.</summary>
public static class Matrix3x2
{
    /// <summary>A matrix of the three rows.</summary>
    public static Matrix3x2<T> Create<T>(Vector2<T> row1, Vector2<T> row2, Vector2<T> row3)
        where T : unmanaged, IFloatingPointIeee754<T> => new(row1, row2, row3);

    /// <summary>A scale by <paramref name="scale"/> along each axis.</summary>
    public static Matrix3x2<T> CreateScale<T>(Vector2<T> scale) where T : unmanaged, IFloatingPointIeee754<T> =>
        CreateScale(scale.X, scale.Y);

    /// <summary>A scale by <paramref name="x"/> and <paramref name="y"/> along the axes.</summary>
    public static Matrix3x2<T> CreateScale<T>(T x, T y) where T : unmanaged, IFloatingPointIeee754<T> =>
        new(x, T.Zero, T.Zero, y, T.Zero, T.Zero);

    /// <summary>A scale by <paramref name="scale"/> along both axes.</summary>
    public static Matrix3x2<T> CreateScale<T>(T scale) where T : unmanaged, IFloatingPointIeee754<T> => CreateScale(scale, scale);

    /// <summary>A scale by <paramref name="x"/> and <paramref name="y"/> that leaves <paramref name="center"/> in
    /// place.</summary>
    public static Matrix3x2<T> CreateScale<T>(T x, T y, Vector2<T> center) where T : unmanaged, IFloatingPointIeee754<T> =>
        new(x, T.Zero, T.Zero, y, center.X - x * center.X, center.Y - y * center.Y);

    /// <summary>A turn by <paramref name="angle"/> radians around the origin.</summary>
    public static Matrix3x2<T> CreateRotation<T>(T angle) where T : unmanaged, IFloatingPointIeee754<T>
    {
        var (sin, cos) = T.SinCos(angle);
        return new Matrix3x2<T>(cos, sin, -sin, cos, T.Zero, T.Zero);
    }

    /// <summary>A turn by <paramref name="angle"/> radians around <paramref name="center"/>.</summary>
    public static Matrix3x2<T> CreateRotation<T>(T angle, Vector2<T> center) where T : unmanaged, IFloatingPointIeee754<T> =>
        CreateTranslation(-center) * CreateRotation(angle) * CreateTranslation(center);

    /// <summary>A scale by <paramref name="xScale"/> and <paramref name="yScale"/>, then a turn by
    /// <paramref name="angle"/> radians, then a move by <paramref name="xOffset"/> and <paramref name="yOffset"/>.</summary>
    public static Matrix3x2<T> CreateTransformation<T>(T xScale, T yScale, T angle, T xOffset, T yOffset)
        where T : unmanaged, IFloatingPointIeee754<T> =>
        CreateScale(xScale, yScale) * CreateRotation(angle) * CreateTranslation(xOffset, yOffset);

    /// <summary>A move by <paramref name="value"/>.</summary>
    public static Matrix3x2<T> CreateTranslation<T>(Vector2<T> value) where T : unmanaged, IFloatingPointIeee754<T> =>
        new(Vector2<T>.UnitX, Vector2<T>.UnitY, value);

    /// <summary>A move by <paramref name="x"/> and <paramref name="y"/>.</summary>
    public static Matrix3x2<T> CreateTranslation<T>(T x, T y) where T : unmanaged, IFloatingPointIeee754<T> =>
        CreateTranslation(new Vector2<T>(x, y));

    /// <summary>A skew of X by <paramref name="angleX"/> radians as Y grows, and of Y by <paramref name="angleY"/>
    /// radians as X grows.</summary>
    public static Matrix3x2<T> CreateSkew<T>(T angleX, T angleY) where T : unmanaged, IFloatingPointIeee754<T> =>
        new(T.One, T.Tan(angleY), T.Tan(angleX), T.One, T.Zero, T.Zero);
}
