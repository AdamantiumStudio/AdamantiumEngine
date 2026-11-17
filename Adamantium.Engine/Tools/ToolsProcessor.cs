using System.Collections.Generic;
using Adamantium.ECS;
using Adamantium.ECS.Components;
using Adamantium.ECS.Components.Extensions;
using Adamantium.Engine.EntityServices;

namespace Adamantium.Engine.Tools;

/// <summary>
/// A part of what <see cref="ToolsService"/> runs every frame - the grid, the icons, the selection's outline, the
/// orientation cube, the pointer tool in use - which may draw over the scene of every output and let a click select
/// what it drew.
/// </summary>
public abstract class ToolsProcessor : EntityProcessor<ToolsService>
{
    protected ToolsService Tools => AssociatedService;

    /// <summary>Draws into one output's frame, after its scene.</summary>
    public virtual void DrawOverlay(ToolsOverlayProcessor overlay)
    {
    }

    /// <summary>Draws into one output's frame first, before its scene: everything the scene draws covers it.</summary>
    public virtual void DrawUnderlay(ToolsOverlayProcessor overlay)
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

    /// <summary>The first processor of the kind the service runs, or null.</summary>
    protected T Find<T>() where T : ToolsProcessor
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
