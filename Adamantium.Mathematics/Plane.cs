using System.Numerics;

namespace Adamantium.Mathematics;

/// <summary>Makes <see cref="Plane{T}"/>s.</summary>
public static class Plane
{
    /// <summary>A plane of <paramref name="normal"/> and <paramref name="d"/>.</summary>
    public static Plane<T> Create<T>(Vector3<T> normal, T d) where T : unmanaged, IFloatingPointIeee754<T> => new(normal, d);

    /// <summary>The plane through <paramref name="point"/> facing <paramref name="normal"/>.</summary>
    public static Plane<T> CreateFromPointNormal<T>(Vector3<T> point, Vector3<T> normal) where T : unmanaged, IFloatingPointIeee754<T> =>
        new(normal, -Vector3<T>.Dot(normal, point));

    /// <summary>The plane through the three points, facing the side they run counterclockwise around as seen from it,
    /// its normal of length one.</summary>
    public static Plane<T> CreateFromVertices<T>(Vector3<T> point1, Vector3<T> point2, Vector3<T> point3)
        where T : unmanaged, IFloatingPointIeee754<T>
    {
        var cross = Vector3<T>.Cross(point2 - point1, point3 - point1);
        var normal = cross * (T.One / cross.Length());
        return new Plane<T>(normal, -Vector3<T>.Dot(normal, point1));
    }
}
