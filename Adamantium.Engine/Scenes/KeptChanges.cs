using System;
using System.Collections.Generic;
using System.Linq;
using Adamantium.ECS;
using Adamantium.ECS.Components;

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

    /// <summary>Puts the bodies and colliders on the physics layer <paramref name="from"/> on <paramref name="to"/>,
    /// at both moments and in the entities it keeps out of the scene: the layer renamed while the change is kept, put on
    /// the scene again it names the new one.</summary>
    public void RenameLayer(string from, string to)
    {
        ArgumentException.ThrowIfNullOrEmpty(from);
        ArgumentException.ThrowIfNullOrEmpty(to);
        _before.RenameLayer(from, to);
        _after.RenameLayer(from, to);
        foreach (var component in PhysicsOf(_added).Concat(PhysicsOf(_removed)))
        {
            if (component is RigidBody body && body.Layer == from)
                body.Layer = to;
            else if (component is Collider collider && collider.Layer == from)
                collider.Layer = to;
        }
    }

    /// <summary>Whether a body or collider it keeps, at either moment or out of the scene, is on the physics layer
    /// <paramref name="layer"/>.</summary>
    public bool NamesLayer(string layer) =>
        _before.NamesLayer(layer) || _after.NamesLayer(layer)
        || PhysicsOf(_added).Concat(PhysicsOf(_removed)).Any(component =>
            component is RigidBody body && body.Layer == layer || component is Collider collider && collider.Layer == layer);

    // The bodies and colliders of the entities and of all they hold.
    private static IEnumerable<IComponent> PhysicsOf(Entity[] entities)
    {
        var stack = new Stack<Entity>(entities);
        while (stack.Count > 0)
        {
            var entity = stack.Pop();
            foreach (var component in entity.Components)
            {
                if (component is RigidBody or Collider)
                    yield return component;
            }

            foreach (var part in entity.Dependencies)
                stack.Push(part);
        }
    }
}
