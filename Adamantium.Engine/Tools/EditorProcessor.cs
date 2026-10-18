using System.Collections.Generic;
using Adamantium.ECS;
using Adamantium.ECS.Components;
using Adamantium.ECS.Components.Extensions;
using Adamantium.Engine.EntityServices;

namespace Adamantium.Engine.Tools;

/// <summary>
/// Editor work that <see cref="ToolsService"/> runs every frame, and that may draw over the scene of every output.
/// </summary>
public abstract class EditorProcessor : EntityProcessor<ToolsService>
{
    protected ToolsService Tools => AssociatedService;

    /// <summary>Draws into one output's frame, after its scene.</summary>
    public virtual void DrawOverlay(EditorOverlayProcessor overlay)
    {
    }

    /// <summary>The scene entity a click along <paramref name="ray"/> would select through what this draws, if any.</summary>
    public virtual PickHit PickEntity(in PickRay ray)
    {
        return default;
    }

    /// <summary>Adds to <paramref name="into"/> the scene entities <paramref name="frame"/> takes on
    /// <paramref name="camera"/>'s output through what this draws, as a click selects them through it.</summary>
    public virtual void PickEntities(CameraBase camera, in ScreenFrame frame, List<Entity> into)
    {
    }

    /// <summary>The first editor processor of the kind the service runs, or null.</summary>
    protected T Find<T>() where T : EditorProcessor
    {
        var processors = Tools.Processors;
        for (int i = 0; i < processors.Count; i++)
        {
            if (processors[i] is T processor)
            {
                return processor;
            }
        }

        return null;
    }
}
