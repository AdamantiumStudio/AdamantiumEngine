using System.Numerics;

namespace Adamantium.Mathematics;

/// <summary>Makes <see cref="Ray2{T}"/>s.</summary>
public static class Ray2
{
    /// <summary>A ray from <paramref name="position"/> along <paramref name="direction"/>.</summary>
    public static Ray2<T> Create<T>(Vector2<T> position, Vector2<T> direction) where T : unmanaged, IFloatingPointIeee754<T> =>
        new(position, direction);
}
