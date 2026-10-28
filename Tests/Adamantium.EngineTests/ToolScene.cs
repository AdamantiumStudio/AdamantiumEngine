using Adamantium.Core;
using Adamantium.ECS;
using Adamantium.ECS.Components;
using Adamantium.ECS.Components.Extensions;
using Adamantium.Mathematics;

namespace Adamantium.EngineTests;

internal sealed class ToolScene
{
    public const uint Width = 800;
    public const uint Height = 600;

    public ToolScene()
    {
        Camera = new Camera(60, Width, Height, 0.1f, 10000f);
        new Entity(null, "Camera").AddComponent(Camera);
        Camera.Update(new AppTime());
    }

    public Camera Camera { get; }

    public PickRay RayAt(Vector3<double> worldPoint)
    {
        return RayAt(PixelOf(worldPoint));
    }

    public PickRay RayAt(Vector2F pixel)
    {
        return PickRay.FromCamera(Camera, pixel);
    }

    public Vector2F PixelOf(Vector3<double> worldPoint)
    {
        var inRender = (worldPoint - Camera.WorldPosition).As<float>();
        var pixel = Vector3F.Project(inRender, 0, 0, Width, Height, 0, 1, Camera.ViewMatrix * Camera.ProjectionMatrix);
        return new Vector2F(pixel.X, pixel.Y);
    }

    public void LookFrom(Vector3<double> eye, Vector3<double> at)
    {
        Camera.Owner.Transform.Position = eye;
        Camera.SetFreeLookAt(at);
        Camera.Update(new AppTime());
    }

    public static Entity Target(Vector3<double> position)
    {
        var entity = new Entity(null, "Target");
        entity.Transform.Position = position;
        return entity;
    }
}
