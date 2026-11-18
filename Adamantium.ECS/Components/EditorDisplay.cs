using Adamantium.MVVM;

namespace Adamantium.ECS.Components;

/// <summary>How an editor draws an entity: its <see cref="Display"/>, set for its whole subtree - a part's own setting
/// overrides its owner's - and whether its icon shows. A game's drawing ignores it.</summary>
[ViewModel]
public partial class EditorDisplay : ActivatableComponent
{
    /// <summary>How its subtree is drawn; <see cref="EntityDisplay.Inherited"/>, the default, as its owner's is.</summary>
    [Bindable] private EntityDisplay display;

    /// <summary>Whether the entity's icon shows even where its meshes are drawn - a group's or a marked entity's, to
    /// take it by in the view; the entity's own, not its subtree's.</summary>
    [Bindable] private bool showsIcon;
}

/// <summary>What an editor draws of an entity's mesh: as the whole view is drawn, or one of the view's own ways in
/// its place, whichever way the view is drawn.</summary>
public enum EntityDisplay
{
    /// <summary>As its owner is drawn - as the whole view, when none of its owners sets it; the default.</summary>
    Inherited,

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
