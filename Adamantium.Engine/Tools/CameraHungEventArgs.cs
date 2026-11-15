using System;
using Adamantium.ECS;
using Adamantium.ECS.Components;
using Adamantium.Graphics.Core;

namespace Adamantium.Engine.Tools;

/// <summary>A camera the keys hung on an entity to follow it, or set free again, with how it was before - what an undo
/// puts back.</summary>
public sealed class CameraHungEventArgs : EventArgs
{
    public CameraHungEventArgs(Entity camera, CameraType typeBefore, Entity hostBefore, EditedState stateBefore,
        CameraType typeAfter, Entity hostAfter)
    {
        Camera = camera;
        TypeBefore = typeBefore;
        HostBefore = hostBefore;
        StateBefore = stateBefore;
        TypeAfter = typeAfter;
        HostAfter = hostAfter;
    }

    /// <summary>The camera's entity.</summary>
    public Entity Camera { get; }

    /// <summary>How the camera was before: free, first person, third person.</summary>
    public CameraType TypeBefore { get; }

    /// <summary>The entity it rode on before; null for none.</summary>
    public Entity HostBefore { get; }

    /// <summary>Where it stood and how it looked before.</summary>
    public EditedState StateBefore { get; }

    /// <summary>How the camera is now.</summary>
    public CameraType TypeAfter { get; }

    /// <summary>The entity it rides on now; null for none.</summary>
    public Entity HostAfter { get; }
}
