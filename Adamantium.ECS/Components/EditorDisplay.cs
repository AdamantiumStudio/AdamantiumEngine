using Adamantium.MVVM;

namespace Adamantium.ECS.Components;

/// <summary>How an editor draws an entity, set on it for its whole subtree: a part's own setting overrides its
/// owner's. A game's drawing ignores it.</summary>
[ViewModel]
public partial class EditorDisplay : ActivatableComponent
{
    [Bindable] private EntityDisplay display;

    /// <summary>A wireframe drawn over the shaded mesh as well.</summary>
    [Bindable] private bool wireOver;
}

/// <summary>What an editor draws of an entity's mesh.</summary>
public enum EntityDisplay
{
    /// <summary>As the whole view is drawn.</summary>
    AsView,

    /// <summary>Only its wireframe.</summary>
    Wireframe,

    /// <summary>Only the box its collider holds.</summary>
    Bounds
}
