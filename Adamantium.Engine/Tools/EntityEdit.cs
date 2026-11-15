using System.Collections.Generic;

namespace Adamantium.Engine.Tools;

/// <summary>A gesture of the editor that changed entities, from the press to the release - a handle dragged, a scene
/// camera steered: each entity with how it stood before and after, to be put back either way.</summary>
public sealed class EntityEdit
{
    public EntityEdit(IReadOnlyList<EditedEntity> entities)
    {
        Entities = entities;
    }

    /// <summary>The entities changed.</summary>
    public IReadOnlyList<EditedEntity> Entities { get; }

    /// <summary>Puts back on every entity what the gesture changed, as it stood before; on the world's thread. A camera's
    /// projection and isometric view flow back as they flowed.</summary>
    public void ApplyBefore()
    {
        foreach (var edited in Entities)
        {
            edited.Before.ApplyTo(edited.Entity, edited.After);
        }
    }

    /// <summary>Puts on every entity what the gesture changed, as it left it; on the world's thread.</summary>
    public void ApplyAfter()
    {
        foreach (var edited in Entities)
        {
            edited.After.ApplyTo(edited.Entity, edited.Before);
        }
    }
}
