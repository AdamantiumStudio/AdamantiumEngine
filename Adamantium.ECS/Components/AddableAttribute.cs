using System;

namespace Adamantium.ECS.Components;

/// <summary>Marks a component type the editor offers to add to an entity, in its <see cref="Group"/> and with its
/// <see cref="Icon"/>. An engine type without it is not offered: a transform, a placeholder, the component that makes an
/// entity a light or a shape. The game's components are offered all the same; it gives them a group and an icon of their
/// own. Not inherited: each type is marked itself.</summary>
[AttributeUsage(AttributeTargets.Class, Inherited = false)]
public sealed class AddableAttribute : Attribute
{
    public AddableAttribute(string group)
    {
        Group = group;
    }

    /// <summary>The group the editor lists it in: one of <see cref="ComponentGroups"/>, or a name of the game's own.
    /// </summary>
    public string Group { get; }

    /// <summary>The key of the editor's icon for it; null for the editor's icon of a component.</summary>
    public string Icon { get; set; }
}
