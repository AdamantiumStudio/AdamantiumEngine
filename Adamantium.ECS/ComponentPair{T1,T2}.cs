using Adamantium.ECS.Components;

namespace Adamantium.ECS;

/// <summary>An entity with a component of each of two types, as <see cref="SceneComponents.With{T1, T2}"/> gives it.
/// </summary>
public readonly struct ComponentPair<T1, T2>
    where T1 : class, IComponent
    where T2 : class, IComponent
{
    internal ComponentPair(Entity entity, T1 first, T2 second)
    {
        Entity = entity;
        First = first;
        Second = second;
    }

    public Entity Entity { get; }

    public T1 First { get; }

    public T2 Second { get; }
}
