using Adamantium.ECS.Components;

namespace Adamantium.ECS;

/// <summary>What a service looks for in a scene: the components of type <typeparamref name="T"/> whose entities also have a
/// component of each type given with <see cref="With{U}"/> - any number of them. Made once and kept, as a field of the
/// service; each frame it is walked in the scene played (<see cref="SceneComponents.Select{T}"/>) without allocating.
/// The walk goes over the type of the fewest components and asks each entity for the others.</summary>
public sealed class ComponentQuery<T> where T : class, IComponent
{
    public ComponentQuery()
    {
        Requirements = [];
    }

    private ComponentQuery(ComponentRequirement[] requirements)
    {
        Requirements = requirements;
    }

    internal ComponentRequirement[] Requirements { get; }

    /// <summary>A query of the same components whose entities also have a component of <typeparamref name="U"/>.
    /// </summary>
    public ComponentQuery<T> With<U>() where U : class, IComponent
    {
        var requirements = new ComponentRequirement[Requirements.Length + 1];
        Requirements.CopyTo(requirements, 0);
        requirements[^1] = new ComponentRequirement(typeof(U), static entity => entity.GetComponent<U>());
        return new ComponentQuery<T>(requirements);
    }
}
