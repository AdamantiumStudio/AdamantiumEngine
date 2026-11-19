using System.Collections.Generic;
using Adamantium.ECS;

namespace Adamantium.Engine.Scenes;

/// <summary>What changed in a scene between two moments - the entities changed, joined and taken out - with how the
/// scene stood at each, either put back on it. On the world's thread.</summary>
public sealed class KeptChanges
{
    private readonly SceneState _before;
    private readonly SceneState _after;
    private readonly Entity[] _changed;
    private readonly Entity[] _added;
    private readonly Entity[] _removed;

    internal KeptChanges(SceneState before, SceneState after, Entity[] changed, Entity[] added, Entity[] removed)
    {
        _before = before;
        _after = after;
        _changed = changed;
        _added = added;
        _removed = removed;
    }

    /// <summary>The scene.</summary>
    public Scene Scene => _before.Scene;

    /// <summary>The entities changed, those taken out among them.</summary>
    public IReadOnlyList<Entity> Changed => _changed;

    /// <summary>Whether anything changed, joined or was taken out.</summary>
    public bool HasChanges => _changed.Length > 0 || _added.Length > 0 || _removed.Length > 0;

    /// <summary>Puts the scene as it stood at first: what joined goes, what was taken out comes back.</summary>
    public void PutBefore() => _before.PutOn(_changed, _removed, _added, null);

    /// <summary>Puts the scene as it stood at last: what joined comes back, what was taken out goes again.</summary>
    public void PutAfter() => _after.PutOn(_changed, _added, _removed, null);
}
