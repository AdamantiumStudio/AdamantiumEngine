using System;
using Adamantium.Core;
using Adamantium.Core.Collections;

namespace Adamantium.ECS;

/// <summary>
/// One scene of a world: its own roots, cameras and one-of-a-kind objects. A world holds any number of them; whatever
/// shows or runs a scene - an output and its processor - works with that one alone, and nothing walks every scene.
/// Made by <see cref="EntityWorld.CreateScene"/>; a world always has its <see cref="EntityWorld.MainScene"/>.
/// </summary>
public sealed class Scene
{
    internal Scene(EntityWorld world, string name, UInt128 uid)
    {
        World = world;
        Name = name;
        Uid = uid;
        Roots = new TrackingCollection<Entity>();
        RootEntities = new ReadOnlyTrackingCollection<Entity>(Roots);
        Cameras = new EntityGroup(nameof(Cameras));
        Index = new SceneIndex();
        Selection = new Selection();
        Satellites = new Satellites();
    }

    /// <summary>The scene's own id - not one of an entity.</summary>
    public UInt128 Uid { get; }

    public string Name { get; set; }

    public EntityWorld World { get; }

    /// <summary>The roots of the scene's trees: its entities with no owner, raising a change as they are added and
    /// removed, so a view can bind to them. One given an owner becomes a branch of its owner's tree and leaves them,
    /// and comes back once it has none. The changes come on the world's thread.</summary>
    public ReadOnlyTrackingCollection<Entity> RootEntities { get; }

    /// <summary>The scene's cameras, wherever they hang in its trees: an entity with a camera joins as its tree joins
    /// the scene, and leaves with it; on the world's thread.</summary>
    public EntityGroup Cameras { get; }

    /// <summary>Where the meshes of the scene stand, kept by whoever walks its transforms while it is shown.</summary>
    public SceneIndex Index { get; }

    /// <summary>What is selected in the scene: each scene its own.</summary>
    public Selection Selection { get; }

    /// <summary>The scene's other one-of-a-kind objects - its lights.</summary>
    public Satellites Satellites { get; }

    /// <summary>Whether the scene is gone from its world: removed, with its trees.</summary>
    public bool IsRemoved { get; internal set; }

    /// <summary>Raised once, as the scene is removed from its world - for what serves it alone to let go.</summary>
    public event EventHandler Removed;

    internal void RaiseRemoved() => Removed?.Invoke(this, EventArgs.Empty);

    internal TrackingCollection<Entity> Roots { get; }

    public override string ToString() => Name;
}
