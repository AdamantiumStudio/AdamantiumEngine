using System;

namespace Adamantium.Engine.Tools;

/// <summary>The kinds of icon <see cref="EntityIcons"/> shows, each on or off: an editor hides those in the way.</summary>
[Flags]
public enum EntityIconKinds
{
    None = 0,

    /// <summary>Empty entities: a <see cref="ECS.Components.Marker"/> and nothing else to see.</summary>
    Entities = 1,

    /// <summary>Groups: a marker with the group's icon key.</summary>
    Groups = 2,

    /// <summary>Lights, with the rays of a directional one.</summary>
    Lights = 4,

    /// <summary>The scene's cameras, with the shape of their view.</summary>
    Cameras = 8,

    All = Entities | Groups | Lights | Cameras
}
