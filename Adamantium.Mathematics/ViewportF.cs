using System.Runtime.InteropServices;

namespace Adamantium.Mathematics;

/// <summary>A viewport: the rectangle of the target drawn to, and the depth range it maps to.</summary>
[StructLayout(LayoutKind.Sequential, Pack = 4)]
public record struct ViewportF(float X, float Y, float Width, float Height, float MinDepth = 0f, float MaxDepth = 1f)
{
    /// <summary>A viewport over <paramref name="bounds"/>, of depth 0 to 1.</summary>
    public ViewportF(RectangleF bounds) : this(bounds.X, bounds.Y, bounds.Width, bounds.Height)
    {
    }

    /// <summary>The rectangle drawn to.</summary>
    public RectangleF Bounds
    {
        readonly get => new(X, Y, Width, Height);
        set => (X, Y, Width, Height) = (value.X, value.Y, value.Width, value.Height);
    }

    /// <summary>Width over height, 0 for no height.</summary>
    public readonly float AspectRatio => Height == 0 ? 0 : Width / Height;

    /// <summary>Where <paramref name="point"/> of world space lands in the viewport, depth in its range - as
    /// <see cref="Vector3F.Project(Vector3F, float, float, float, float, float, float, Matrix4x4F)"/> puts it, clip space
    /// running y down.</summary>
    public readonly Vector3F Project(Vector3F point, Matrix4x4F projection, Matrix4x4F view, Matrix4x4F world) =>
        Vector3F.Project(point, X, Y, Width, Height, MinDepth, MaxDepth, world * view * projection);

    /// <summary>The point of world space that lands at <paramref name="point"/> of the viewport, as
    /// <see cref="Vector3F.Unproject(Vector3F, float, float, float, float, float, float, Matrix4x4F)"/> finds it.</summary>
    public readonly Vector3F Unproject(Vector3F point, Matrix4x4F projection, Matrix4x4F view, Matrix4x4F world) =>
        Vector3F.Unproject(point, X, Y, Width, Height, MinDepth, MaxDepth, world * view * projection);
}
