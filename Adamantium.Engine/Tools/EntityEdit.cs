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

    /// <summary>Puts every entity back as it stood before the gesture; on the world's thread.</summary>
    public void ApplyBefore()
    {
        foreach (var edited in Entities)
        {
            edited.Before.ApplyTo(edited.Entity);
        }
    }

    /// <summary>Puts every entity as the gesture left it; on the world's thread.</summary>
    public void ApplyAfter()
    {
        foreach (var edited in Entities)
        {
            edited.After.ApplyTo(edited.Entity);
        }
    }
}
