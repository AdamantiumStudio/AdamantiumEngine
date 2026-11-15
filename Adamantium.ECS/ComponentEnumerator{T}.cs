using System;
using Adamantium.ECS.Components;

namespace Adamantium.ECS;

/// <summary>Walks a <see cref="ComponentSelection{T}"/>. While a walk goes on, components taken out of the scene leave gaps
/// rather than moving the others; <c>foreach</c> disposes of it, which lets the scene close them.</summary>
public struct ComponentEnumerator<T> : IDisposable where T : class, IComponent
{
    private readonly SceneComponents _index;
    private PoolWalk _walk;
    private bool _ended;

    internal ComponentEnumerator(SceneComponents index, ComponentPool[] pools)
    {
        _index = index;
        _walk = new PoolWalk(pools);
        _ended = false;
        index.BeginWalk();
    }

    public T Current => (T)_walk.Component;

    public bool MoveNext() => _walk.MoveNext();

    public void Dispose()
    {
        if (_ended)
            return;

        _ended = true;
        _index.EndWalk();
    }
}
