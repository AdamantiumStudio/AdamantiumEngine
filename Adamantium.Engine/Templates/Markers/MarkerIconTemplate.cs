using Adamantium.ECS;
using Adamantium.ECS.Components;
using Adamantium.Graphics.Core.Models;
using Adamantium.Mathematics;

namespace Adamantium.Engine.Templates.Markers;

/// <summary>A flat icon for an entity with a <see cref="Marker"/>, drawn facing the eye, up along +Y.</summary>
public abstract class MarkerIconTemplate
{
    public Entity BuildEntity(Entity owner, string name)
    {
        var entity = new Entity(owner, name);
        entity.AddComponent(new MeshData { Mesh = Generate() });
        entity.AddComponent(new BoxCollider());
        entity.AddComponent(new Material
        {
            MeshColor = Colors.White.ToVector3(),
            HighlightColor = Colors.Yellow.ToVector4(),
            Transparency = 1
        });
        return entity;
    }

    protected abstract Mesh Generate();
}
