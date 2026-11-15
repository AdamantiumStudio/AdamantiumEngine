using Adamantium.ECS.Components;

namespace Adamantium.ECS;

/// <summary>The entities of a scene enabled in their tree that have a component of each of two types: what
/// <see cref="SceneComponents.With{T1, T2}"/> gives: each entity once, with its first component of each type, as
/// <see cref="Entity.GetComponent{T}"/> finds it. Walks the fewer of the two; <c>foreach</c>, nothing allocated, on the
/// world's thread.</summary>
public readonly struct ComponentPairSelection<T1, T2>
    where T1 : class, IComponent
    where T2 : class, IComponent
{
    private readonly SceneComponents _index;
    private readonly ComponentPool[] _pools;
    private readonly bool _walksSecond;

    internal ComponentPairSelection(SceneComponents index, ComponentPool[] pools, bool walksSecond)
    {
        _index = index;
        _pools = pools;
        _walksSecond = walksSecond;
    }

    public ComponentPairEnumerator<T1, T2> GetEnumerator() => new(_index, _pools, _walksSecond);
}
