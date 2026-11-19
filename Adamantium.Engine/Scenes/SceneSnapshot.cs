using System;
using System.Collections.Generic;
using Adamantium.ECS;

namespace Adamantium.Engine.Scenes;

/// <summary>A scene's state at one moment, to put it back later: what the scene keeps, as its file has it, and how each
/// entity stands - placed, enabled, shown, under which entity and where - which the file does not all keep. Taking it
/// starts recording the scene's changes (<see cref="Scene.Changes"/>); putting it back undoes only what changed, so its
/// cost follows the changes, not the scene's size. On the world's thread.</summary>
public sealed class SceneSnapshot
{
    private readonly SceneState _state;

    private SceneSnapshot(SceneState state)
    {
        _state = state;
    }

    /// <summary>The scene.</summary>
    public Scene Scene => _state.Scene;

    /// <summary>The state of <paramref name="scene"/> now; its changes are recorded from here.</summary>
    public static SceneSnapshot Take(Scene scene)
    {
        ArgumentNullException.ThrowIfNull(scene);
        var state = SceneState.Take(scene);
        scene.Changes.Start();
        return new SceneSnapshot(state);
    }

    /// <summary>Whether <paramref name="entity"/> was in the scene when the state was taken.</summary>
    public bool Contains(Entity entity) => _state.Contains(entity);

    /// <summary>Puts the scene back as it was: an entity that joined it since is taken out - unless it, or one it stands
    /// in, is in <paramref name="keep"/>; one taken out comes back where it stood, the same object; one changed gets its
    /// state back, its game's components with it. Recording stops.</summary>
    public void Restore(IReadOnlySet<Entity> keep = null)
    {
        var changes = Scene.Changes;
        changes.Stop();
        _state.PutOn(changes.Changed, changes.Removed, changes.Added, keep);
        changes.Clear();
    }

    /// <summary>Leaves the scene as it stands and stops recording: what changed since the state was taken, with how it
    /// stood then and how it stands now, either of which <see cref="KeptChanges"/> puts back.</summary>
    public KeptChanges KeepChanges()
    {
        var changes = Scene.Changes;
        changes.Stop();
        var kept = new KeptChanges(_state, SceneState.Take(Scene), [.. changes.Changed], [.. changes.Added],
            [.. changes.Removed]);
        changes.Clear();
        return kept;
    }
}
