using Adamantium.ECS;
using Adamantium.ECS.Components;

namespace Adamantium.Engine.Tools;

/// <summary>What the editor's handles change on an entity, as it stood at one moment: its transform, and the reach of
/// its light when it is one.</summary>
/// <param name="Transform">The entity's transform.</param>
/// <param name="LightRange">Its light's range; 0 for an entity with no light.</param>
/// <param name="LightOuterSpotAngle">Its light's outer spot angle; 0 for an entity with no light.</param>
public readonly record struct EditedState(TransformState Transform, float LightRange, float LightOuterSpotAngle)
{
    /// <summary>How <paramref name="entity"/> stands now.</summary>
    public static EditedState Of(Entity entity)
    {
        var light = entity.GetComponent<Light>();
        return new EditedState(entity.Transform.State, light?.Range ?? 0, light?.OuterSpotAngle ?? 0);
    }

    /// <summary>Puts <paramref name="entity"/> back as this state has it; on the world's thread.</summary>
    public void ApplyTo(Entity entity)
    {
        entity.Transform.State = Transform;
        if (entity.GetComponent<Light>() is { } light)
        {
            light.Range = LightRange;
            light.OuterSpotAngle = LightOuterSpotAngle;
        }
    }
}
