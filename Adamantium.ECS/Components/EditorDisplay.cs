using Adamantium.MVVM;

namespace Adamantium.ECS.Components;

/// <summary>How an editor draws an entity, set on it for its whole subtree: a part's own setting overrides its
/// owner's. A game's drawing ignores it.</summary>
[ViewModel]
public partial class EditorDisplay : ActivatableComponent
{
    [Bindable] private EntityDisplay display;
}

/// <summary>What an editor draws of an entity's mesh: as the whole view is drawn, or one of the view's own ways in
/// its place, whichever way the view is drawn.</summary>
public enum EntityDisplay
{
    /// <summary>As the whole view is drawn.</summary>
    AsView,

    /// <summary>Shaded.</summary>
    Shaded,

    /// <summary>Only its wireframe.</summary>
    Wireframe,

    /// <summary>Shaded, with its wireframe over.</summary>
    ShadedWireframe,

    /// <summary>Only its collider, the mesh left out.</summary>
    Collider
}
