using Adamantium.ECS.Components;

namespace Adamantium.ECS;

/// <summary>The components of type <typeparamref name="T"/> - or derived from it - in a scene, on entities enabled in their
/// tree that meet the conditions of a query: what <see cref="SceneComponents.Of{T}"/> and
/// <see cref="SceneComponents.Select{T}"/> give. Walked with <c>foreach</c>, nothing allocated; on the world's thread. The
/// default one is empty.</summary>
public readonly struct ComponentSelection<T> where T : class, IComponent
{
    private readonly SceneComponents _index;
    private readonly ComponentPool[] _pools;
    private readonly ComponentRequirement[] _requirements;
    private readonly int _walked;

    internal ComponentSelection(SceneComponents index, ComponentPool[] pools, ComponentRequirement[] requirements,
        int walked)
    {
        _index = index;
        _pools = pools;
        _requirements = requirements;
        _walked = walked;
    }

    public ComponentEnumerator<T> GetEnumerator() => new(_index, _pools ?? [], _requirements ?? [], _walked);
}
