using System.Threading.Tasks;
using Adamantium.ECS;
using Adamantium.ECS.Components;
using Adamantium.ECS.Templates;
using Adamantium.Graphics;
using Adamantium.Graphics.Core.Models;
using Adamantium.Mathematics;
using Adamantium.ProceduralGeometry;

namespace Adamantium.Engine.Templates.GeometricPrimitives;

public abstract class PrimitiveTemplate : IEntityTemplate
{
    protected int Tessellation { get; }
    protected Matrix4x4? Transform { get; }
    protected GeometryType GeometryType { get; }

    protected PrimitiveTemplate(
        GeometryType geometryType,
        int tessellation,
        Matrix4x4? transform = null)
    {
        Tessellation = tessellation;
        Transform = transform;
        GeometryType = geometryType;
    }

    protected Entity BuildEntityFromPrimitive(
        Entity entity,
        Mesh geometry,
        BoundingVolume volume = BoundingVolume.OrientedBox)
    {
        MeshData meshData = new MeshData();
        meshData.Mesh = geometry;
        FillMetadata(meshData.Metadata);

        Collider collisionComponent;

        switch (volume)
        {
            case BoundingVolume.Sphere:
                collisionComponent = new SphereCollider();
                break;
            default:
                collisionComponent = new BoxCollider();
                break;
        }

        entity.AddComponent(meshData);
        entity.AddComponent(collisionComponent);
        entity.IconKey = EntityIconKeys.Mesh;

        return entity;
    }

    protected abstract void FillMetadata(MeshMetadata metadata);

    public abstract Task<Entity> BuildEntity(Entity owner);

}