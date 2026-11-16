using System;
using Adamantium.ECS.Components;

namespace Adamantium.ECS;

/// <summary>A condition of a <see cref="ComponentQuery{T}"/>: the entity has a component of <see cref="Type"/>, the first
/// of which <see cref="Find"/> gives.</summary>
internal sealed class ComponentRequirement
{
    public ComponentRequirement(Type type, Func<Entity, IComponent> find)
    {
        Type = type;
        Find = find;
    }

    public Type Type { get; }

    public Func<Entity, IComponent> Find { get; }
}
