using Adamantium.ECS.Components;

namespace Adamantium.ECS;

/// <summary>The components of type <typeparamref name="T"/> - or derived from it - in a scene, on entities enabled in their
/// tree: what <see cref="SceneComponents.Of{T}"/> gives. Walked with <c>foreach</c>, nothing allocated; on the world's
/// thread.</summary>
public readonly struct ComponentSelection<T> where T : class, IComponent
{
    private readonly SceneComponents _index;
    private readonly ComponentPool[] _pools;

    internal ComponentSelection(SceneComponents index, ComponentPool[] pools)
    {
        _index = index;
        _pools = pools;
    }

    public ComponentEnumerator<T> GetEnumerator() => new(_index, _pools);
}
