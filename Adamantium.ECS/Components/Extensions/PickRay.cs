using Adamantium.Mathematics;

namespace Adamantium.ECS.Components.Extensions;

/// <summary>
/// The ray under one pixel, in the render space of <see cref="Camera"/>: the space its entity transforms are kept in.
/// </summary>
public readonly struct PickRay
{
    private readonly float pixelAtOrigin;
    private readonly float pixelPerDepth;

    public PickRay(Ray<float> ray, CameraBase camera, float pixelAtOrigin, float pixelPerDepth)
    {
        Ray = ray;
        Camera = camera;
        this.pixelAtOrigin = pixelAtOrigin;
        this.pixelPerDepth = pixelPerDepth;
    }

    public Ray<float> Ray { get; }

    public CameraBase Camera { get; }

    /// <summary>How wide a pixel is at <paramref name="depth"/> along the ray.</summary>
    public float PixelSize(float depth)
    {
        return pixelAtOrigin + pixelPerDepth * depth;
    }

    /// <summary>The same ray starting <paramref name="offset"/> away: in this render space, the ray an eye standing there
    /// would cast.</summary>
    public PickRay Moved(Vector3F offset)
    {
        return new PickRay(new Ray<float>(Ray.Position + offset, Ray.Direction), Camera, pixelAtOrigin, pixelPerDepth);
    }

    /// <summary>Through the camera's own view and projection.</summary>
    public static PickRay FromCamera(CameraBase camera, Vector2F pixel)
    {
        return FromViewProjection(camera, pixel, camera.ViewMatrix * camera.ProjectionMatrix);
    }

    /// <summary>Through another projection, such as the camera's screen-space one for what it draws over the scene.</summary>
    public static PickRay FromViewProjection(CameraBase camera, Vector2F pixel, Matrix4x4F viewProjection)
    {
        // Inverted once for the ray and its neighbour a pixel over, as Vector3F.Unproject would for every point.
        var inverse = Matrix4x4F.Invert(viewProjection);
        var ray = Through(camera, pixel, in inverse);
        var next = Through(camera, new Vector2F(pixel.X + 1, pixel.Y), in inverse);
        var atOrigin = (next.Position - ray.Position).Length();
        var perDepth = (next.Direction - ray.Direction).Length();
        return new PickRay(ray, camera, atOrigin, perDepth);
    }

    // From the near plane through the middle of the depth range, not the far plane: with a far plane all but at
    // infinity, a point unprojected there goes to infinity at some turns of the view, and the direction with it - NaN,
    // or pointing back. Halfway, a perspective point stands about twice the near distance off: always a number. The
    // pixel goes to clip space as Vector3F.Unproject takes it there.
    private static Ray<float> Through(CameraBase camera, Vector2F pixel, in Matrix4x4F inverse)
    {
        var clipX = pixel.X / camera.Width * 2 - 1;
        var clipY = pixel.Y / camera.Height * 2 - 1;
        var near = Vector3F.TransformCoordinate(new Vector3F(clipX, clipY, 0), inverse);
        var beyond = Vector3F.TransformCoordinate(new Vector3F(clipX, clipY, 0.5f), inverse);
        return new Ray<float>(near, Vector3F.Normalize(beyond - near));
    }
}
