using System;
using Adamantium.ECS.Components;

namespace Adamantium.ECS;

/// <summary>Walks a <see cref="ComponentSelection{T}"/>. While a walk goes on, components taken out of the scene leave gaps
/// rather than moving the others; <c>foreach</c> disposes of it, which lets the scene close them.</summary>
public struct ComponentEnumerator<T> : IDisposable where T : class, IComponent
{
    private readonly SceneComponents _index;
    private readonly ComponentRequirement[] _requirements;
    private readonly int _walked;
    private PoolWalk _walk;
    private Entity _entity;
    private int _component;
    private T _current;
    private bool _ended;

    internal ComponentEnumerator(SceneComponents index, ComponentPool[] pools, ComponentRequirement[] requirements,
        int walked)
    {
        _index = index;
        _requirements = requirements;
        _walked = walked;
        _walk = new PoolWalk(pools);
        _entity = null;
        _component = -1;
        _current = null;
        _ended = false;
        index?.BeginWalk();
    }

    public T Current => _current;

    public bool MoveNext()
    {
        while (true)
        {
            if (_entity != null && MoveToNextOfEntity())
                return true;

            if (!_walk.MoveNext())
            {
                _current = null;
                return false;
            }

            var owner = _walk.Owner;
            if (_walked < 0)
            {
                if (MeetsAll(owner))
                {
                    _current = (T)_walk.Component;
                    return true;
                }

                continue;
            }

            if (ReferenceEquals(_requirements[_walked].Find(owner), _walk.Component) && MeetsAll(owner))
            {
                _entity = owner;
                _component = -1;
            }
        }
    }

    public void Dispose()
    {
        if (_ended)
            return;

        _ended = true;
        _index?.EndWalk();
    }

    private bool MoveToNextOfEntity()
    {
        var components = _entity.Components.AsSpan();
        while (++_component < components.Length)
        {
            if (components[_component] is T component)
            {
                _current = component;
                return true;
            }
        }

        _entity = null;
        return false;
    }

    private bool MeetsAll(Entity owner)
    {
        foreach (var requirement in _requirements)
        {
            if (requirement.Find(owner) == null)
                return false;
        }

        return true;
    }
}
