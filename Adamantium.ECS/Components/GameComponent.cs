using System;
using System.Collections.Generic;
using System.Linq;
using System.Reflection;
using System.Runtime.CompilerServices;

namespace Adamantium.ECS.Components;

/// <summary>A component of the game's or a mod's code: data its services act on. Its public properties with a getter
/// and a setter, declared on its own type, are what a scene keeps of it.</summary>
public abstract class GameComponent : Component
{
    // Weak by type, so the game's code still unloads.
    private static readonly ConditionalWeakTable<Type, PropertyInfo[]> EntityProperties = new();

    /// <summary>A property of its own type naming an entity of the trees copied names its copy in
    /// <paramref name="clone"/>; one mapped to null names none.</summary>
    public override void CloneReferences(IComponent clone, IReadOnlyDictionary<Entity, Entity> copies)
    {
        foreach (var property in EntityProperties.GetValue(GetType(), FindEntityProperties))
        {
            property.SetValue(clone, CopyOf((Entity)property.GetValue(this), copies));
        }
    }

    /// <summary>Says all its values changed, for a writer outside the game's code - the editor's undo: its own properties
    /// say nothing. On the world's thread.</summary>
    public void TellChanged() => RaisePropertyChanged(string.Empty);

    private static PropertyInfo[] FindEntityProperties(Type type) =>
        type.GetProperties(BindingFlags.Instance | BindingFlags.Public)
            .Where(property => property.PropertyType == typeof(Entity) && property.CanRead && property.CanWrite &&
                               property.DeclaringType != typeof(GameComponent) &&
                               typeof(GameComponent).IsAssignableFrom(property.DeclaringType) &&
                               property.GetIndexParameters().Length == 0 && !property.IsDefined(typeof(DoNotCloneAttribute)))
            .ToArray();
}
