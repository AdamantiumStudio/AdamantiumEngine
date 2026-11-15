using Adamantium.ECS.Components;

namespace Adamantium.ECS;

/// <summary>The entities of a scene enabled in their tree that have a component of each of two types: what
/// <see cref="SceneComponents.With{T1, T2}"/> gives. Walks the fewer of the two and asks each entity for the other;
/// <c>foreach</c>, nothing allocated, on the world's thread.</summary>
public readonly struct ComponentPairSelection<T1, T2>
    where T1 : class, IComponent
    where T2 : class, IComponent
{
    private readonly SceneComponents _index;
    private readonly ComponentPool[] _pools;

    internal ComponentPairSelection(SceneComponents index, ComponentPool[] pools)
    {
        _index = index;
        _pools = pools;
    }

    public ComponentPairEnumerator<T1, T2> GetEnumerator() => new(_index, _pools);
}
