using System;
using Adamantium.ECS.Components;
using Adamantium.Mathematics;

namespace Adamantium.Engine.Tools;

internal static class ScreenSpace
{
    public static float UnitsPerPixel(CameraBase camera, Vector3<double> worldPoint)
    {
        var projection = camera.ProjectionMatrix;
        var depth = Vector3F.Dot((worldPoint - camera.WorldPosition).As<float>(), camera.Forward.As<float>());
        var w = depth * projection.M34 + projection.M44;
        return 2f * Math.Abs(w) / (camera.Width * projection.M11);
    }

    public static float UnitsPerPoint(CameraBase camera, Vector3<double> worldPoint)
    {
        return UnitsPerPixel(camera, worldPoint) * camera.PixelsPerPoint;
    }

    public static Vector3F InRender(Vector3<double> worldPoint, CameraBase camera)
    {
        return (worldPoint - camera.WorldPosition).As<float>();
    }

    public static QuaternionF Facing(CameraBase camera)
    {
        return QuaternionF.Conjugate(camera.Rotation);
    }

    public static Vector2F ToPixel(Vector3F inRender, CameraBase camera)
    {
        var pixel = Vector3F.Project(inRender, 0, 0, camera.Width, camera.Height, 0, 1, camera.ViewMatrix * camera.ProjectionMatrix);
        return new Vector2F(pixel.X, pixel.Y);
    }

    // The point at the same depth whose pixel coordinates are whole plus `phase` (0 for a pixel's corner, 0.5 for its center).
    public static Vector3F OnPixelGrid(Vector3F inRender, CameraBase camera, float phase)
    {
        var viewProjection = camera.ViewMatrix * camera.ProjectionMatrix;
        var pixel = Vector3F.Project(inRender, 0, 0, camera.Width, camera.Height, 0, 1, viewProjection);
        pixel.X = MathF.Round(pixel.X - phase) + phase;
        pixel.Y = MathF.Round(pixel.Y - phase) + phase;
        return Vector3F.Unproject(pixel, 0, 0, camera.Width, camera.Height, 0, 1, viewProjection);
    }
}
