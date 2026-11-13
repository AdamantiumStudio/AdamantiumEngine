using System;
using Adamantium.ECS;
using Adamantium.ECS.Components;
using Adamantium.Graphics.Core.Models;

namespace Adamantium.Engine.Templates.Lights;

public class LightTemplate
{
    public Entity BuildEntity(Entity owner, string name, LightType type)
    {
        var entity = new Entity(owner, name);
        Build(entity, type);
        return entity;
    }

    /// <summary>Makes <paramref name="entity"/> a light of <paramref name="type"/>; the light it is given.</summary>
    public Light Build(Entity entity, LightType type)
    {
        ArgumentNullException.ThrowIfNull(entity);
        if (type is not (LightType.Directional or LightType.Point or LightType.Spot))
        {
            throw new NotSupportedException(nameof(type));
        }

        var light = new Light(type);
        entity.AddComponent(light);
        return light;
    }
}
