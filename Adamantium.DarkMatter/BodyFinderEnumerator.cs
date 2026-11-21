using System;

namespace Adamantium.DarkMatter;

/// <summary>Walks the bodies a search finds, from the last added, each once: a body taken out on the way, the one walked
/// or another, leaves the rest walked; a body added on the way is not walked.</summary>
public ref struct BodyFinderEnumerator<TFilter>
    where TFilter : IBodyFilter
{
    private readonly PhysicsSimulation _simulation;
    private readonly FindConditions _conditions;
    private readonly TFilter _filter;
    private readonly long _walk;
    private int _index;
    private int _skip;
    private int _take;

    internal BodyFinderEnumerator(PhysicsSimulation simulation, FindConditions conditions, TFilter filter, int skip,
        int take)
    {
        simulation.ThrowIfDisposed();
        _simulation = simulation;
        _conditions = conditions;
        _filter = filter;
        _skip = skip;
        _take = take;
        _walk = simulation.StartWalk();
        _index = simulation.LiveBodyCount;
        Current = default;
    }

    public BodyView Current { get; private set; }

    public bool MoveNext()
    {
        if (_take == 0)
            return false;

        _index = Math.Min(_index, _simulation.LiveBodyCount);
        while (--_index >= 0)
        {
            if (!_simulation.TryViewLiveBody(_index, _conditions, out var body) || !_conditions.Matches(body)
                || !_filter.Matches(body) || !body.TakeIntoWalk(_walk))
                continue;

            if (_skip > 0)
            {
                _skip--;
                continue;
            }

            _take--;
            Current = body;
            return true;
        }

        return false;
    }
}
