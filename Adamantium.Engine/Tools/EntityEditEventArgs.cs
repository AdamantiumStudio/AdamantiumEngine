using System;

namespace Adamantium.Engine.Tools;

/// <summary>A gesture that changed entities, as <see cref="EntityServices.ToolsService.Edited"/> tells it.</summary>
public sealed class EntityEditEventArgs : EventArgs
{
    public EntityEditEventArgs(EntityEdit edit)
    {
        Edit = edit;
    }

    /// <summary>What the gesture changed.</summary>
    public EntityEdit Edit { get; }
}
