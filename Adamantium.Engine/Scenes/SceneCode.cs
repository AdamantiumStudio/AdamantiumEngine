using System;
using System.Collections.Generic;
using System.Reflection;
using Adamantium.ECS;
using Adamantium.ECS.Components;
using Serilog;

namespace Adamantium.Engine.Scenes;

/// <summary>Moves the game's components of a world from one build of its code to the next, the way a scene file holds
/// them, and its game services with them.</summary>
public static class SceneCode
{
    /// <summary>Takes <paramref name="leaving"/> out of the world's code (its <see cref="IWorldCode"/> satellite) and puts
    /// <paramref name="arriving"/> in: each
    /// component of the leaving code, on an entity of any scene of the world, becomes its scene entry; then each entry an
    /// entity keeps whose type the code now has becomes a component again, its fields from the entry. The game services
    /// of the leaving code leave the world; one of each kind the arriving code brings joins it, asleep until a game plays
    /// (<see cref="GameRun"/>). Either list may be empty. On the world's thread, between its frames. Returns the
    /// components whose code threw; their entries are kept.</summary>
    public static IReadOnlyList<GameComponentProblem> Replace(EntityWorld world, IReadOnlyList<Assembly> leaving,
        IReadOnlyList<Assembly> arriving)
    {
        var code = world.Satellites.Get<IWorldCode>();
        var entities = CollectEntities(world);
        foreach (var entity in entities)
        {
            Detach(entity, leaving);
        }

        code.Replace(leaving, arriving);
        ReplaceServices(world, code, leaving, arriving);

        var problems = new List<GameComponentProblem>();
        foreach (var entity in entities)
        {
            Reattach(entity, code, problems);
        }

        return problems;
    }

    /// <summary>Makes the component <paramref name="entity"/> keeps as <paramref name="entry"/> - one whose type the code
    /// does not have, renamed, say - a component of <paramref name="type"/>: the fields of the entry that type has, by
    /// name; the entry is no longer kept. On the world's thread. Returns the problem when the type's code throws; the
    /// entry stays then.</summary>
    /// <exception cref="ArgumentException"><paramref name="type"/> is no component a scene keeps by type, or the entity
    /// has one of it already.</exception>
    public static GameComponentProblem Load(Entity entity, SceneComponent entry, Type type)
    {
        ArgumentNullException.ThrowIfNull(entity);
        ArgumentNullException.ThrowIfNull(entry);
        ArgumentNullException.ThrowIfNull(type);
        CheckMadeType(entity, type);
        if (!SceneComponents.Keeps(entity, entry))
            throw new ArgumentException($"{entity.Name} does not keep that entry.", nameof(entry));

        var problems = new List<GameComponentProblem>(1);
        var component = SceneComponents.Make(entity, entry, type, problems);
        if (component == null || !SceneComponents.TryAdd(entity, component, type.FullName, problems))
            return problems[0];

        SceneComponents.Drop(entity, entry);
        return null;
    }

    /// <summary>Puts a new component of <paramref name="type"/> on <paramref name="entity"/>, its fields as its code
    /// sets them: one of the game's, or one of the engine's a scene keeps by type. On the world's thread. Returns the
    /// problem when the type's code throws; the entity is left as it was.</summary>
    /// <exception cref="ArgumentException"><paramref name="type"/> is no component a scene keeps by type that can be
    /// made, or the entity has one of it already.</exception>
    public static GameComponentProblem Add(Entity entity, Type type)
    {
        ArgumentNullException.ThrowIfNull(entity);
        ArgumentNullException.ThrowIfNull(type);
        CheckMadeType(entity, type);

        var problems = new List<GameComponentProblem>(1);
        var component = SceneComponents.Make(entity, new SceneComponent { On = entity.Uid, Type = type.FullName }, type, problems);
        if (component == null || !SceneComponents.TryAdd(entity, component, type.FullName, problems))
            return problems[0];

        return null;
    }

    /// <summary>Whether a scene keeps a component of <paramref name="type"/> by its type: one of the game's, or one of the
    /// engine's marked <see cref="InspectorComponentAttribute"/> that no section of an entry keeps - not a rigid body, a collider or
    /// a joint.</summary>
    public static bool KeepsByType(Type type)
    {
        ArgumentNullException.ThrowIfNull(type);
        return typeof(Component).IsAssignableFrom(type) && SceneComponents.IsKept(type);
    }

    /// <summary>Takes off <paramref name="entity"/> the entry it keeps for a component whose code is not loaded: the
    /// component is gone from the scene. On the world's thread.</summary>
    public static void Remove(Entity entity, SceneComponent entry)
    {
        ArgumentNullException.ThrowIfNull(entity);
        ArgumentNullException.ThrowIfNull(entry);
        SceneComponents.Drop(entity, entry);
    }

    /// <summary>Puts back on <paramref name="entity"/> the entry taken off with <see cref="Remove"/> or made a component
    /// with <see cref="Load"/> - as an undo does: the component whose code is not loaded is in the scene again. On the
    /// world's thread. Nothing for an entry the entity keeps already.</summary>
    public static void Restore(Entity entity, SceneComponent entry)
    {
        ArgumentNullException.ThrowIfNull(entity);
        ArgumentNullException.ThrowIfNull(entry);
        if (!SceneComponents.Keeps(entity, entry))
        {
            SceneComponents.Keep(entity, entry);
        }
    }

    private static void CheckMadeType(Entity entity, Type type)
    {
        if (!KeepsByType(type) || type.IsAbstract || type.ContainsGenericParameters ||
            type.GetConstructor(Type.EmptyTypes) == null)
            throw new ArgumentException($"{type.FullName} is not a component a scene keeps by type that can be made.", nameof(type));

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
        List<GameComponent> leavingGameComponents = null;
        foreach (var component in entity.Components)
        {
            if (component is GameComponent gameComponent && IsAmong(leaving, gameComponent.GetType().Assembly))
            {
                leavingGameComponents ??= [];
                leavingGameComponents.Add(gameComponent);
            }
        }

        if (leavingGameComponents == null)
            return;

        foreach (var gameComponent in leavingGameComponents)
        {
            SceneComponents.Keep(entity, SceneComponents.Write(gameComponent, entity.Uid));
            entity.RemoveComponent(gameComponent);
        }
    }

    private static void ReplaceServices(EntityWorld world, IWorldCode code, IReadOnlyList<Assembly> leaving,
        IReadOnlyList<Assembly> arriving)
    {
        foreach (var service in world.GetServices<IGameService>())
        {
            if (!IsAmong(leaving, service.GetType().Assembly))
                continue;

            service.IsActive = false;
            world.RemoveService(service);
        }

        foreach (var type in code.GetGivenTypes<IGameService>([typeof(EntityWorld)]))
        {
            if (!IsAmong(arriving, type.Assembly))
                continue;

            try
            {
                if (Activator.CreateInstance(type, world) is IGameService service)
                {
                    service.IsActive = false;
                    world.AddService(service);
                }
            }
            catch (Exception exception)
            {
                Log.Logger.Error(exception, "The game service {Service} could not be made", type.FullName);
            }
        }

        world.ServiceManager.SyncServices();
    }

    private static void Reattach(Entity entity, IWorldCode code, List<GameComponentProblem> problems)
    {
        var unloaded = entity.GetComponent<UnloadedComponents>();
        if (unloaded == null)
            return;

        entity.RemoveComponent(unloaded);
        foreach (var entry in unloaded.Entries)
        {
            SceneComponents.Attach(entity, entry, code, problems);
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
