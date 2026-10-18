using Adamantium.ECS;
using Adamantium.ECS.Components;

namespace Adamantium.Engine.Tools;

/// <summary>
/// Selects what a click lands on; an entity with a <see cref="Marker"/> - its icon or, if it has one, its form - is also
/// dragged in the plane facing the eye.
/// </summary>
public class SelectTool : ToolProcessor
{
    private readonly MoveHandles mover = new();

    protected override Handles DragsClicked(Entity entity)
    {
        return entity?.GetComponent<Marker>() is { IsEnabled: true } ? mover : null;
    }
}
