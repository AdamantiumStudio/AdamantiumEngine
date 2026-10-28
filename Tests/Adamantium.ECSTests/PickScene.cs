using Adamantium.Core;
using Adamantium.ECS;
using Adamantium.ECS.Components;
using Adamantium.ECS.Components.Extensions;
using Adamantium.Graphics.Core;
using Adamantium.Graphics.Core.Models;
using Adamantium.Mathematics;

namespace Adamantium.ECSTests;

internal sealed class PickScene
{
    public const uint Width = 800;
    public const uint Height = 600;

    public PickScene()
    {
        Camera = new Camera(60, Width, Height, 0.1f, 10000f);
        var owner = new Entity(null, "Camera");
        owner.AddComponent(Camera);
        Camera.Update(new AppTime());
    }

    public Camera Camera { get; }

    public Vector2F Center => new(Width / 2f, Height / 2f);

    public PickRay RayThroughCenter()
    {
        return PickRay.FromCamera(Camera, Center);
    }

    public float PixelWidthAt(float viewDepth)
    {
        var projection = Camera.ProjectionMatrix;
        var w = viewDepth * projection.M34 + projection.M44;
        return 2f / Width * w / projection.M11;
    }

    public void Place(Entity root)
    {
        root.TraverseByLayer(current =>
        {
            var parentWorld = current.Owner != null
                ? current.Owner.Transform.GetMetadata(Camera).AbsoluteWorld
                : Matrix4x4F.Identity;
            current.Transform.CalculateFinalTransform(Camera, parentWorld);
        });
    }

    public static Entity Box(Entity owner, string name, Vector3<double> center, double half)
    {
        return Solid(owner, name, center, half, new BoxCollider());
    }

    public static Entity Ball(Entity owner, string name, Vector3<double> center, double radius)
    {
        return Solid(owner, name, center, radius, new SphereCollider());
    }

    public static void Bounds(Entity model, Vector3F minimum, Vector3F maximum)
    {
        var collider = new BoxCollider();
        model.AddComponent(collider);
        collider.CalculateFromPoints([minimum, maximum]);
    }

    public static Entity Shape(Entity owner, string name, PrimitiveType topology, Vector3<double>[] points, int[] indices = null)
    {
        var mesh = new Mesh(topology).SetPoints(points).SetIndices(indices);
        return WithMesh(owner, name, mesh);
    }

    private static Entity Solid(Entity owner, string name, Vector3<double> center, double half, Collider collider)
    {
        var mesh = new Mesh(PrimitiveType.TriangleList).SetPoints(
        [
            new Vector3<double>(-half, -half, -half), new Vector3<double>(half, half, half)
        ]);
        var entity = WithMesh(owner, name, mesh);
        entity.Transform.Position = center;
        entity.AddComponent(collider);
        collider.Initialize();
        return entity;
    }

    private static Entity WithMesh(Entity owner, string name, Mesh mesh)
    {
        var entity = new Entity(owner, name);
        entity.AddComponent(new MeshData { Mesh = mesh });
        return entity;
    }
}
