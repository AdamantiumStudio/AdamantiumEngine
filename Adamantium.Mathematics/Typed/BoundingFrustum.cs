using System.Numerics;

namespace Adamantium.Mathematics.Typed;

/// <summary>Makes <see cref="BoundingFrustum{T}"/>s.</summary>
public static class BoundingFrustum
{
    /// <summary>The frustum <paramref name="viewProjection"/> sees, checking its far plane when
    /// <paramref name="enableFarPlaneCheck"/>.</summary>
    public static BoundingFrustum<T> Create<T>(Matrix4x4<T> viewProjection, bool enableFarPlaneCheck = false)
        where T : unmanaged, IFloatingPointIeee754<T> => new(viewProjection, enableFarPlaneCheck);
}
