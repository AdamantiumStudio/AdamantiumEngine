using System.Collections.Generic;
using Adamantium.DarkMatter.Shapes;
using Adamantium.ECS;

namespace Adamantium.Engine.Physics;

/// <summary>What a body is made of: its entities - its own first, then those it watches - with their change counts when
/// the body was made, so a change to any of them is seen, and the hull and mesh shapes it took.</summary>
internal sealed class BodyParts
{
    private readonly List<Entity> _entities = [];
    private readonly List<long> _counts = [];
    // A static body's parents: one changed, the body stands where they put it now.
    private readonly List<Entity> _ancestors = [];
    private readonly List<long> _ancestorCounts = [];
    private bool _isStale;

    /// <summary>The number of the last look over the scene's bodies that found this one.</summary>
    public long SeenInLook { get; set; }

    /// <summary>The hull and mesh shapes the body took, to give back when it goes.</summary>
    public List<CarriedShapeKey> CarriedShapes { get; } = [];

    /// <summary>The shapes the body was made of, where they stand on it, to tell whether a body made again differs.
    /// </summary>
    public CompoundPart[] ShapeParts { get; set; } = [];

    /// <summary>Whether the body could not be made for how it or its parent is flattened, mirrored, skewed or
    /// stretched: tried again once that is undone.</summary>
    public bool WaitsToStand { get; set; }

    public void Add(Entity entity)
    {
        _entities.Add(entity);
        _counts.Add(entity.ChangeCount);
    }

    /// <summary>Whether any of the entities changed since it was added or last reread, or the body was marked stale.
    /// </summary>
    public bool HaveChanged()
    {
        if (_isStale)
            return true;

        for (var index = 0; index < _entities.Count; index++)
        {
            if (_entities[index].ChangeCount != _counts[index])
                return true;
        }

        return false;
    }

    public void AddAncestor(Entity ancestor)
    {
        _ancestors.Add(ancestor);
        _ancestorCounts.Add(ancestor.ChangeCount);
    }

    /// <summary>Whether a parent above the body changed since it was added or last reread.</summary>
    public bool HaveAncestorsChanged()
    {
        for (var index = 0; index < _ancestors.Count; index++)
        {
            if (_ancestors[index].ChangeCount != _ancestorCounts[index])
                return true;
        }

        return false;
    }

    public void RereadAncestors()
    {
        for (var index = 0; index < _ancestors.Count; index++)
        {
            _ancestorCounts[index] = _ancestors[index].ChangeCount;
        }
    }

    /// <summary>Marks the body for making anew: something it does not watch changed what it is made of.</summary>
    public void MarkStale() => _isStale = true;

    /// <summary>Whether the body's own entity is as it was added or last reread.</summary>
    public bool IsBodyAsRead() => _entities[0].ChangeCount == _counts[0];

    /// <summary>Takes the body's own entity as it is now: the physics changed it itself.</summary>
    public void RereadBody() => _counts[0] = _entities[0].ChangeCount;
}
