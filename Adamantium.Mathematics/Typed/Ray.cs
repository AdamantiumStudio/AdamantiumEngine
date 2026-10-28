using System.Numerics;

namespace Adamantium.Mathematics.Typed;

/// <summary>Makes <see cref="Ray{T}"/>s.</summary>
public static class Ray
{
    /// <summary>A ray from <paramref name="position"/> along <paramref name="direction"/>.</summary>
    public static Ray<T> Create<T>(Vector3<T> position, Vector3<T> direction) where T : unmanaged, IFloatingPointIeee754<T> =>
        new(position, direction);

    /// <summary>The ray through the point <paramref name="x"/>, <paramref name="y"/> of <paramref name="viewport"/>,
    /// from the near plane towards the far one, in the space <paramref name="worldViewProjection"/> starts from; its
    /// direction is of length one. The ray picking what lies under a cursor.</summary>
    public static Ray<T> CreateFromViewport<T>(T x, T y, Viewport<T> viewport, Matrix4x4<T> worldViewProjection)
        where T : unmanaged, IFloatingPointIeee754<T>
    {
        var near = viewport.Unproject(new Vector3<T>(x, y, T.Zero), worldViewProjection);
        var far = viewport.Unproject(new Vector3<T>(x, y, T.One), worldViewProjection);
        return new Ray<T>(near, Vector3<T>.Normalize(far - near));
    }
}
