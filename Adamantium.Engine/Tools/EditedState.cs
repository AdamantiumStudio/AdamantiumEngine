using Adamantium.ECS;
using Adamantium.ECS.Components;

namespace Adamantium.Engine.Tools;

/// <summary>What the editor's gestures change on an entity, as it stood at one moment: its transform, the reach of its
/// light when it is one, and how its camera looks when it is one.</summary>
/// <param name="Transform">The entity's transform.</param>
/// <param name="Light">Its light's reach; null for an entity with no light.</param>
/// <param name="Camera">Its camera's pose; null for an entity with no camera.</param>
public readonly record struct EditedState(TransformState Transform, LightReach? Light, CameraPose? Camera)
{
    /// <summary>How <paramref name="entity"/> stands now.</summary>
    public static EditedState Of(Entity entity)
    {
        var light = entity.GetComponent<Light>();
        var camera = entity.GetComponent<Camera>();
        return new EditedState(entity.Transform.State, light == null ? null : LightReach.Of(light),
            camera == null ? null : CameraPose.Of(camera));
    }

    /// <summary>Puts <paramref name="entity"/> back as this state has it; on the world's thread.</summary>
    public void ApplyTo(Entity entity)
    {
        entity.Transform.State = Transform;
        if (Light is { } reach && entity.GetComponent<Light>() is { } light)
        {
            reach.ApplyTo(light);
        }

        if (Camera is { } pose && entity.GetComponent<Camera>() is { } camera)
        {
            pose.ApplyTo(camera);
        }

        entity.NotifyUpdated();
    }
}
