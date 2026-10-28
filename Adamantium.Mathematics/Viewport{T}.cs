using System;
using System.Numerics;
using System.Runtime.InteropServices;

namespace Adamantium.Mathematics;

/// <summary>A viewport of fractional numbers: the rectangle of the target drawn to, and the depth range it maps to.</summary>
[StructLayout(LayoutKind.Sequential)]
public struct Viewport<T> : IEquatable<Viewport<T>> where T : unmanaged, IFloatingPointIeee754<T>
{
    /// <summary>The left edge.</summary>
    public T X;

    /// <summary>The top edge.</summary>
    public T Y;

    /// <summary>The width.</summary>
    public T Width;

    /// <summary>The height.</summary>
    public T Height;

    /// <summary>The depth the near plane maps to.</summary>
    public T MinDepth;

    /// <summary>The depth the far plane maps to.</summary>
    public T MaxDepth;

    /// <summary>A viewport at <paramref name="x"/>, <paramref name="y"/> of <paramref name="width"/> by
    /// <paramref name="height"/>, of depth <paramref name="minDepth"/> to <paramref name="maxDepth"/>.</summary>
    public Viewport(T x, T y, T width, T height, T minDepth, T maxDepth)
    {
        X = x;
        Y = y;
        Width = width;
        Height = height;
        MinDepth = minDepth;
        MaxDepth = maxDepth;
    }

    /// <summary>A viewport over <paramref name="bounds"/>, of depth 0 to 1.</summary>
    public Viewport(Rectangle<T> bounds) : this(bounds.X, bounds.Y, bounds.Width, bounds.Height, T.Zero, T.One)
    {
    }

    /// <summary>The rectangle drawn to.</summary>
    public Rectangle<T> Bounds
    {
        readonly get => new(X, Y, Width, Height);
        set => (X, Y, Width, Height) = (value.X, value.Y, value.Width, value.Height);
    }

    /// <summary>Width over height, zero for no height.</summary>
    public readonly T AspectRatio => Height == T.Zero ? T.Zero : Width / Height;

    /// <summary>Where <paramref name="point"/> of the space <paramref name="worldViewProjection"/> starts from lands in
    /// the viewport, its depth in the depth range; clip space runs Y down.</summary>
    public readonly Vector3<T> Project(Vector3<T> point, Matrix4x4<T> worldViewProjection) =>
        Vector3<T>.Project(point, X, Y, Width, Height, MinDepth, MaxDepth, worldViewProjection);

    /// <summary>The point that <see cref="Project"/> puts at <paramref name="point"/> of the viewport.</summary>
    public readonly Vector3<T> Unproject(Vector3<T> point, Matrix4x4<T> worldViewProjection) =>
        Vector3<T>.Unproject(point, X, Y, Width, Height, MinDepth, MaxDepth, worldViewProjection);

    /// <summary>Whether every value of the two is equal; NaN equals nothing.</summary>
    public static bool operator ==(Viewport<T> left, Viewport<T> right) =>
        left.X == right.X && left.Y == right.Y && left.Width == right.Width && left.Height == right.Height
        && left.MinDepth == right.MinDepth && left.MaxDepth == right.MaxDepth;

    /// <summary>Whether a value differs; NaN differs from everything.</summary>
    public static bool operator !=(Viewport<T> left, Viewport<T> right) => !(left == right);

    /// <summary>Whether every value of the two is equal, a NaN to a NaN too.</summary>
    public readonly bool Equals(Viewport<T> other) =>
        X.Equals(other.X) && Y.Equals(other.Y) && Width.Equals(other.Width) && Height.Equals(other.Height)
        && MinDepth.Equals(other.MinDepth) && MaxDepth.Equals(other.MaxDepth);

    /// <inheritdoc />
    public override readonly bool Equals(object obj) => obj is Viewport<T> other && Equals(other);

    /// <inheritdoc />
    public override readonly int GetHashCode() => HashCode.Combine(X, Y, Width, Height, MinDepth, MaxDepth);

    /// <inheritdoc />
    public override readonly string ToString() => $"X:{X} Y:{Y} Width:{Width} Height:{Height} Depth:{MinDepth}..{MaxDepth}";
}
