using System;
using System.Collections.Concurrent;
using System.Collections.Generic;
using System.Reflection;
using Adamantium.ECS;
using Adamantium.ECS.Components;
using Serilog;

namespace Adamantium.Engine.Scenes;

/// <summary>Moves the components a scene keeps by type (<see cref="SceneComponent"/>) between an entity and their scene
/// entries: the game's, and the engine's addable ones that no section of an entry keeps.</summary>
internal static class SceneComponents
{
    // Whether each engine type is kept by type: an engine type never unloads, so what it is is remembered.
    private static readonly ConcurrentDictionary<Type, bool> EngineTypesKept = new();
    private static readonly Func<Type, bool> IsMarkedAndUnsectioned = IsAddableWithoutSection;

    /// <summary>Whether the scene keeps a component of <paramref name="type"/> by its type: one of the game's, or one of
    /// the engine's marked <see cref="AddableAttribute"/> - but a rigid body, a collider and a joint, which the entry
    /// keeps in sections of its own.</summary>
    public static bool IsKept(Type type)
    {
        if (typeof(GameComponent).IsAssignableFrom(type))
            return true;

        // A type of code that unloads is asked each time: nothing here holds it.
        return type.Assembly.IsCollectible ? IsAddableWithoutSection(type) : EngineTypesKept.GetOrAdd(type, IsMarkedAndUnsectioned);
    }

    /// <summary>Whether <paramref name="entity"/> has a component the scene keeps by type, or keeps entries of ones not
    /// loaded.</summary>
    public static bool HasKept(Entity entity)
    {
        foreach (var component in entity.Components)
        {
            if (component is UnloadedComponents || IsKept(component.GetType()))
                return true;
        }

        return false;
    }

    /// <summary>Puts the component <paramref name="entry"/> describes on <paramref name="entity"/>, its type found in
    /// <paramref name="code"/>. The entry is kept on the entity instead when the type is not loaded or is none the scene
    /// keeps by type, the entity has one of that type already, or its code throws - then the throw is added to
    /// <paramref name="problems"/>.</summary>
    public static void Attach(Entity entity, SceneComponent entry, IWorldCode code, List<GameComponentProblem> problems)
    {
        var type = FindType(code, entry.Type);
        if (type == null)
        {
            Keep(entity, entry);
            return;
        }

        if (entity.ContainsComponent(type))
        {
            Log.Logger.Warning("Scene: {Name} already has a {Type}, and an entity can have only one of each; the second one is kept unloaded",
                entity.Name, entry.Type);
            Keep(entity, entry);
            return;
        }

        var component = Make(entity, entry, type, problems);
        if (component == null || !TryAdd(entity, component, entry.Type, problems))
        {
            Keep(entity, entry);
        }
    }

    /// <summary>The type of the code named <paramref name="name"/> that the scene keeps by type; null when there is none
    /// - one that is not kept so is logged.</summary>
    public static Type FindType(IWorldCode code, string name)
    {
        var type = code.FindType<Component>(name);
        if (type == null || IsKept(type))
            return type;

        Log.Logger.Warning("Scene: {Type} is no component a scene keeps by its type; its entry is kept as it was read", name);
        return null;
    }

    /// <summary>Puts <paramref name="component"/> on <paramref name="entity"/>; false when its code throws as it is
    /// added - its <c>Initialize</c>, say - then it is taken off again and the throw added to
    /// <paramref name="problems"/>.</summary>
    public static bool TryAdd(Entity entity, Component component, string type, List<GameComponentProblem> problems)
    {
        try
        {
            entity.AddComponent(component);
            return true;
        }
        catch (Exception exception)
        {
            // The game's own code: whatever it throws is its fault, and must cost the scene no more than this component.
            Log.Logger.Error(exception, "Scene: {Type} on {Name} threw as it was added; its saved values are kept unloaded",
                type, entity.Name);
            problems.Add(new GameComponentProblem(entity, type, exception));
            entity.RemoveComponent(component);
            return false;
        }
    }

    /// <summary>A component of <paramref name="type"/> with the fields <paramref name="entry"/> keeps, by name; null when
    /// its code throws - then the throw is added to <paramref name="problems"/>.</summary>
    public static Component Make(Entity entity, SceneComponent entry, Type type, List<GameComponentProblem> problems)
    {
        try
        {
            var component = (Component)Activator.CreateInstance(type);
            ComponentFields.Read(entry.Values, component, FieldsBelow(type));
            return component;
        }
        catch (Exception exception) when (exception is TargetInvocationException or TypeInitializationException)
        {
            var thrown = exception.InnerException ?? exception;
            Log.Logger.Error(thrown, "Scene: could not create {Type} on {Name}; its saved values are kept unloaded",
                type.FullName, entity.Name);
            problems.Add(new GameComponentProblem(entity, entry.Type, thrown));
            return null;
        }
    }

    /// <summary>Sets <paramref name="entry"/>'s values on <paramref name="component"/>, of its type.</summary>
    public static void Read(SceneComponent entry, Component component) =>
        ComponentFields.Read(entry.Values, component, FieldsBelow(component.GetType()));

    /// <summary>The entry the scene writes for <paramref name="component"/> on the entity <paramref name="on"/>.</summary>
    public static SceneComponent Write(Component component, UInt128 on)
    {
        var type = component.GetType();
        var entry = new SceneComponent { On = on, Type = type.FullName };
        ComponentFields.Write(component, FieldsBelow(type), entry.Values, SceneComponent.OwnAttributes);
        return entry;
    }

    /// <summary>Keeps <paramref name="entry"/> on <paramref name="entity"/> as it was read, among its
    /// <see cref="UnloadedComponents"/>.</summary>
    public static void Keep(Entity entity, SceneComponent entry)
    {
        var unloaded = entity.GetComponent<UnloadedComponents>();
        var entries = new List<SceneComponent>();
        if (unloaded != null)
        {
            entries.AddRange(unloaded.Entries);
            entity.RemoveComponent(unloaded);
        }

        entries.Add(entry);
        entity.AddComponent(new UnloadedComponents(entries));
    }

    /// <summary>Takes <paramref name="entry"/> out of those <paramref name="entity"/> keeps.</summary>
    public static void Drop(Entity entity, SceneComponent entry)
    {
        if (entity.GetComponent<UnloadedComponents>() is not { } unloaded)
        {
            return;
        }

        var kept = new List<SceneComponent>(unloaded.Entries.Count);
        foreach (var each in unloaded.Entries)
        {
            if (!ReferenceEquals(each, entry))
            {
                kept.Add(each);
            }
        }

        entity.RemoveComponent(unloaded);
        if (kept.Count > 0)
        {
            entity.AddComponent(new UnloadedComponents(kept));
        }
    }

    /// <summary>Whether <paramref name="entity"/> keeps <paramref name="entry"/>.</summary>
    public static bool Keeps(Entity entity, SceneComponent entry)
    {
        if (entity.GetComponent<UnloadedComponents>() is not { } unloaded)
        {
            return false;
        }

        foreach (var each in unloaded.Entries)
        {
            if (ReferenceEquals(each, entry))
            {
                return true;
            }
        }

        return false;
    }

    // The fields of the type's own, above the bases every component of its kind shares.
    private static Type FieldsBelow(Type type)
    {
        if (typeof(GameComponent).IsAssignableFrom(type))
            return typeof(GameComponent);

        return typeof(ActivatableComponent).IsAssignableFrom(type) ? typeof(ActivatableComponent) : typeof(Component);
    }

    private static bool IsAddableWithoutSection(Type type) =>
        type.IsDefined(typeof(AddableAttribute), false) && !typeof(RigidBody).IsAssignableFrom(type) &&
        !typeof(Collider).IsAssignableFrom(type) && !typeof(Joint).IsAssignableFrom(type);
}
