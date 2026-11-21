using System;

namespace Adamantium.DarkMatter;

/// <summary>The groups of a grouped search: runs of one key in the caller's buffer, each a slice of it. Good until the
/// buffer is written again or the simulation reads a step: a body walked since taken out is refused.</summary>
public readonly ref struct BodyGrouping<TKey>
{
    private readonly PhysicsSimulation _simulation;
    private readonly ReadOnlySpan<BodyId> _bodies;
    private readonly Func<BodyView, TKey> _key;

    internal BodyGrouping(PhysicsSimulation simulation, ReadOnlySpan<BodyId> bodies, Func<BodyView, TKey> key)
    {
        _simulation = simulation;
        _bodies = bodies;
        _key = key;
    }

    /// <summary>All the bodies grouped, group after group.</summary>
    public ReadOnlySpan<BodyId> Bodies => _bodies;

    /// <summary>How many groups there are.</summary>
    public int Count
    {
        get
        {
            var count = 0;
            foreach (var _ in this)
                count++;

            return count;
        }
    }

    public BodyGroupingEnumerator<TKey> GetEnumerator() => new(_simulation, _bodies, _key);
}
