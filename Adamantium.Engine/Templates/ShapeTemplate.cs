using System.Threading.Tasks;
using Adamantium.ECS;
using Adamantium.ECS.Components;
using Adamantium.ECS.Templates;

namespace Adamantium.Engine.Templates;

/// <summary>
/// An entity drawing a <see cref="ProceduralShape"/>: the shape itself, kept on it to change later, the mesh made from
/// it, and a box collider round it.
/// </summary>
public class ShapeTemplate : IEntityTemplate
{
    private readonly ProceduralShape shape;

    public ShapeTemplate(ProceduralShape shape)
    {
        this.shape = shape;
    }

    public Task<Entity> BuildEntity(Entity owner)
    {
        owner.AddComponent(new MeshData { Mesh = shape.Build() });
        owner.AddComponent(new BoxCollider());
        owner.AddComponent(shape);
        owner.IconKey = EntityIconKeys.Mesh;
        return Task.FromResult(owner);
    }
}
