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
        var ray = Through(camera, pixel, viewProjection);
        var next = Through(camera, new Vector2F(pixel.X + 1, pixel.Y), viewProjection);
        var atOrigin = (next.Position - ray.Position).Length();
        var perDepth = (next.Direction - ray.Direction).Length();
        return new PickRay(ray, camera, atOrigin, perDepth);
    }

    private static Ray<float> Through(CameraBase camera, Vector2F pixel, Matrix4x4F viewProjection)
    {
        var near = Vector3F.Unproject(new Vector3F(pixel.X, pixel.Y, 0), 0, 0, camera.Width, camera.Height, 0, 1, viewProjection);
        var far = Vector3F.Unproject(new Vector3F(pixel.X, pixel.Y, 1), 0, 0, camera.Width, camera.Height, 0, 1, viewProjection);
        return new Ray<float>(near, Vector3F.Normalize(far - near));
    }
}
