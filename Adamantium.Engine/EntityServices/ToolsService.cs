using Adamantium.Core;
using Adamantium.ECS;
using Adamantium.ECS.Components.Extensions;
using Adamantium.Engine.Tools;

namespace Adamantium.Engine.EntityServices;

/// <summary>
/// Runs the editor's processors once per frame: what sits over the scene first, then the one tool in use. Only an editor
/// creates it; a universe without the editor has no tools.
/// </summary>
public class ToolsService : EntityService
{
    private ToolProcessor tool;
    private volatile ToolProcessor requested;

    public ToolsService(EntityWorld world)
        : base(world)
    {
        Priority = 1;
    }

    public override bool IsUpdateService => true;
    public override bool IsRenderingService => false;
    public override EntityServiceType ServiceType => EntityServiceType.Update;

    public Observatory Observatory { get; private set; }

    public Selection Selection { get; private set; }

    /// <summary>The tool in use: one at a time, or none. Set from any thread; the change takes effect with the next frame.</summary>
    public ToolProcessor Tool
    {
        get => requested;
        set => requested = value;
    }

    /// <summary>Whether something over the scene has taken the pointer this frame; the tool then leaves it alone.</summary>
    public bool IsPointerTaken { get; private set; }

    /// <summary>What a click would select where the pointer is now; the tool in use finds it every frame.</summary>
    public Entity Hovered { get; set; }

    public void TakePointer()
    {
        IsPointerTaken = true;
    }

    /// <summary>What a click along <paramref name="ray"/> selects: the nearest part of the scene, or of what the editor shows over it.</summary>
    public PickHit PickEntity(in PickRay ray)
    {
        var nearest = Picking.Pick(EntityWorld.RootEntities, ray, PickMode.Triangles, withDisabled: true);
        var processors = Processors;
        for (int i = 0; i < processors.Count; i++)
        {
            if (processors[i] is EditorProcessor editor)
            {
                nearest = PickHit.Nearest(nearest, editor.PickEntity(ray));
            }
        }

        return nearest;
    }

    public override void Initialize()
    {
        Observatory = EntityWorld.Satellites.Get<Observatory>();
        Selection = EntityWorld.Satellites.Get<Selection>();
    }

    public override void Update(AppTime appTime)
    {
        var next = requested;
        if (next != tool)
        {
            DetachProcessor(tool);
            tool = next;
            AttachProcessor(tool);
        }

        IsPointerTaken = false;
        Hovered = null;
        base.Update(appTime);
    }
}
