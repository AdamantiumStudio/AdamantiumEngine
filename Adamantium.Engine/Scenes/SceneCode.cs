using System;
using System.Collections.Generic;
using System.Reflection;
using Adamantium.ECS;
using Adamantium.ECS.Components;

namespace Adamantium.Engine.Scenes;

/// <summary>Moves the game's components of a world from one build of its code to the next, the way a scene file holds
/// them.</summary>
public static class SceneCode
{
    /// <summary>Takes <paramref name="leaving"/> out of the world's code and puts <paramref name="arriving"/> in: each
    /// component of the leaving code, on an entity of any scene of the world, becomes its scene entry; then each entry an
    /// entity keeps whose type the code now has becomes a component again, its fields from the entry. Either list may be
    /// empty. On the world's thread, between its frames. Returns the components whose code threw; their entries are
    /// kept.</summary>
    public static IReadOnlyList<BehaviorProblem> Replace(EntityWorld world, IReadOnlyList<Assembly> leaving,
        IReadOnlyList<Assembly> arriving)
    {
        var entities = CollectEntities(world);
        foreach (var entity in entities)
        {
            Detach(entity, leaving);
        }

        world.Code.Replace(leaving, arriving);

        var problems = new List<BehaviorProblem>();
        foreach (var entity in entities)
        {
            Reattach(entity, world.Code, problems);
        }

        return problems;
    }

    /// <summary>Makes the component <paramref name="entity"/> keeps as <paramref name="entry"/> - one whose type the code
    /// does not have, renamed, say - a component of <paramref name="type"/>: the fields of the entry that type has, by
    /// name; the entry is no longer kept. On the world's thread. Returns the problem when the type's code throws; the
    /// entry stays then.</summary>
    /// <exception cref="ArgumentException"><paramref name="type"/> is no component of the game's code, or the entity has
    /// one of it already.</exception>
    public static BehaviorProblem Load(Entity entity, SceneBehavior entry, Type type)
    {
        ArgumentNullException.ThrowIfNull(entity);
        ArgumentNullException.ThrowIfNull(entry);
        ArgumentNullException.ThrowIfNull(type);
        CheckMadeType(entity, type);
        if (!SceneBehaviors.Keeps(entity, entry))
            throw new ArgumentException($"{entity.Name} does not keep that entry.", nameof(entry));

        var problems = new List<BehaviorProblem>(1);
        var behavior = SceneBehaviors.Make(entity, entry, type, problems);
        if (behavior == null || !SceneBehaviors.TryAdd(entity, behavior, type.FullName, problems))
            return problems[0];

        SceneBehaviors.Drop(entity, entry);
        return null;
    }

    /// <summary>Puts a new component of <paramref name="type"/> on <paramref name="entity"/>, its fields as its code
    /// sets them. On the world's thread. Returns the problem when the type's code throws; the entity is left as it was.
    /// </summary>
    /// <exception cref="ArgumentException"><paramref name="type"/> is no component of the game's code that can be
    /// made, or the entity has one of it already.</exception>
    public static BehaviorProblem Add(Entity entity, Type type)
    {
        ArgumentNullException.ThrowIfNull(entity);
        ArgumentNullException.ThrowIfNull(type);
        CheckMadeType(entity, type);

        var problems = new List<BehaviorProblem>(1);
        var behavior = SceneBehaviors.Make(entity, new SceneBehavior { On = entity.Uid, Type = type.FullName }, type, problems);
        if (behavior == null || !SceneBehaviors.TryAdd(entity, behavior, type.FullName, problems))
            return problems[0];

        return null;
    }

    /// <summary>Takes off <paramref name="entity"/> the entry it keeps for a component whose code is not loaded: the
    /// component is gone from the scene. On the world's thread.</summary>
    public static void Remove(Entity entity, SceneBehavior entry)
    {
        ArgumentNullException.ThrowIfNull(entity);
        ArgumentNullException.ThrowIfNull(entry);
        SceneBehaviors.Drop(entity, entry);
    }

    private static void CheckMadeType(Entity entity, Type type)
    {
        if (!typeof(Behavior).IsAssignableFrom(type) || type.IsAbstract || type.ContainsGenericParameters ||
            type.GetConstructor(Type.EmptyTypes) == null)
            throw new ArgumentException($"{type.FullName} is not a component of the game's code that can be made.", nameof(type));

        if (entity.ContainsComponent(type))
            throw new ArgumentException($"{entity.Name} already has a {type.FullName}.", nameof(type));
    }

    private static List<Entity> CollectEntities(EntityWorld world)
    {
        var entities = new List<Entity>();
        var stack = new Stack<Entity>();
        foreach (var scene in world.Scenes)
        {
            foreach (var root in scene.RootEntities)
            {
                stack.Push(root);
            }
        }

        while (stack.Count > 0)
        {
            var entity = stack.Pop();
            entities.Add(entity);
            foreach (var child in entity.Dependencies)
            {
                stack.Push(child);
            }
        }

        return entities;
    }

    private static void Detach(Entity entity, IReadOnlyList<Assembly> leaving)
    {
        List<Behavior> leavingBehaviors = null;
        foreach (var component in entity.Components)
        {
            if (component is Behavior behavior && IsAmong(leaving, behavior.GetType().Assembly))
            {
                leavingBehaviors ??= [];
                leavingBehaviors.Add(behavior);
            }
        }

        if (leavingBehaviors == null)
            return;

        foreach (var behavior in leavingBehaviors)
        {
            SceneBehaviors.Keep(entity, SceneBehaviors.Write(behavior, entity.Uid));
            entity.RemoveComponent(behavior);
        }
    }

    private static void Reattach(Entity entity, WorldCode code, List<BehaviorProblem> problems)
    {
        var unloaded = entity.GetComponent<UnloadedBehaviors>();
        if (unloaded == null)
            return;

        entity.RemoveComponent(unloaded);
        foreach (var entry in unloaded.Entries)
        {
            SceneBehaviors.Attach(entity, entry, code, problems);
        }
    }

    private static bool IsAmong(IReadOnlyList<Assembly> assemblies, Assembly assembly)
    {
        foreach (var each in assemblies)
        {
            if (ReferenceEquals(each, assembly))
                return true;
        }

        return false;
    }
}
