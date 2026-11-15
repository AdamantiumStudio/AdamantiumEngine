using System;
using Adamantium.ECS.Components;

namespace Adamantium.ECS;

/// <summary>Walks a <see cref="ComponentPairSelection{T1, T2}"/>; as <see cref="ComponentEnumerator{T}"/>, <c>foreach</c>
/// disposes of it.</summary>
public struct ComponentPairEnumerator<T1, T2> : IDisposable
    where T1 : class, IComponent
    where T2 : class, IComponent
{
    private readonly SceneComponents _index;
    private PoolWalk _walk;
    private ComponentPair<T1, T2> _current;
    private bool _ended;

    internal ComponentPairEnumerator(SceneComponents index, ComponentPool[] pools)
    {
        _index = index;
        _walk = new PoolWalk(pools);
        _current = default;
        _ended = false;
        index.BeginWalk();
    }

    public ComponentPair<T1, T2> Current => _current;

    public bool MoveNext()
    {
        while (_walk.MoveNext())
        {
            var owner = _walk.Owner;
            if (owner.GetComponent<T1>() is { } first && owner.GetComponent<T2>() is { } second)
            {
                _current = new ComponentPair<T1, T2>(owner, first, second);
                return true;
            }
        }

        _current = default;
        return false;
    }

    public void Dispose()
    {
        if (_ended)
            return;

        _ended = true;
        _index.EndWalk();
    }
}
