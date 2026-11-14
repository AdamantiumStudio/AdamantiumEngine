using System;
using System.Collections.Generic;
using System.Reflection;
using Adamantium.ECS;
using Adamantium.ECS.Components;
using Serilog;

namespace Adamantium.Engine.Scenes;

/// <summary>Moves the game's components between an entity and their scene entries.</summary>
internal static class SceneGameComponents
{
    /// <summary>Puts the component <paramref name="entry"/> describes on <paramref name="entity"/>, its type found in
    /// <paramref name="code"/>. The entry is kept on the entity instead when the type is not loaded, the entity has one
    /// of that type already, or its code throws - then the throw is added to <paramref name="problems"/>.</summary>
    public static void Attach(Entity entity, SceneGameComponent entry, WorldCode code, List<GameComponentProblem> problems)
    {
        var type = code.FindType<GameComponent>(entry.Type);
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

        var gameComponent = Make(entity, entry, type, problems);
        if (gameComponent == null || !TryAdd(entity, gameComponent, entry.Type, problems))
        {
            Keep(entity, entry);
        }
    }

    /// <summary>Puts <paramref name="gameComponent"/> on <paramref name="entity"/>; false when its code throws as it is
    /// added - its <c>Initialize</c>, say - then it is taken off again and the throw added to
    /// <paramref name="problems"/>.</summary>
    public static bool TryAdd(Entity entity, GameComponent gameComponent, string type, List<GameComponentProblem> problems)
    {
        try
        {
            entity.AddComponent(gameComponent);
            return true;
        }
        catch (Exception exception)
        {
            // The game's own code: whatever it throws is its fault, and must cost the scene no more than this component.
            Log.Logger.Error(exception, "Scene: {Type} on {Name} threw as it was added; its saved values are kept unloaded",
                type, entity.Name);
            problems.Add(new GameComponentProblem(entity, type, exception));
            entity.RemoveComponent(gameComponent);
            return false;
        }
    }

    /// <summary>A component of <paramref name="type"/> with the fields <paramref name="entry"/> keeps, by name; null when
    /// its code throws - then the throw is added to <paramref name="problems"/>.</summary>
    public static GameComponent Make(Entity entity, SceneGameComponent entry, Type type, List<GameComponentProblem> problems)
    {
        try
        {
            var gameComponent = (GameComponent)Activator.CreateInstance(type);
            ComponentFields.Read(entry.Values, gameComponent, typeof(GameComponent));
            return gameComponent;
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

    /// <summary>The entry the scene writes for <paramref name="gameComponent"/> on the entity <paramref name="on"/>.</summary>
    public static SceneGameComponent Write(GameComponent gameComponent, UInt128 on)
    {
        var entry = new SceneGameComponent { On = on, Type = gameComponent.GetType().FullName };
        ComponentFields.Write(gameComponent, typeof(GameComponent), entry.Values, SceneGameComponent.OwnAttributes);
        return entry;
    }

    /// <summary>Keeps <paramref name="entry"/> on <paramref name="entity"/> as it was read, among its
    /// <see cref="UnloadedGameComponents"/>.</summary>
    public static void Keep(Entity entity, SceneGameComponent entry)
    {
        var unloaded = entity.GetComponent<UnloadedGameComponents>();
        var entries = new List<SceneGameComponent>();
        if (unloaded != null)
        {
            entries.AddRange(unloaded.Entries);
            entity.RemoveComponent(unloaded);
        }

        entries.Add(entry);
        entity.AddComponent(new UnloadedGameComponents(entries));
    }

    /// <summary>Takes <paramref name="entry"/> out of those <paramref name="entity"/> keeps.</summary>
    public static void Drop(Entity entity, SceneGameComponent entry)
    {
        if (entity.GetComponent<UnloadedGameComponents>() is not { } unloaded)
        {
            return;
        }

        var kept = new List<SceneGameComponent>(unloaded.Entries.Count);
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
            entity.AddComponent(new UnloadedGameComponents(kept));
        }
    }

    /// <summary>Whether <paramref name="entity"/> keeps <paramref name="entry"/>.</summary>
    public static bool Keeps(Entity entity, SceneGameComponent entry)
    {
        if (entity.GetComponent<UnloadedGameComponents>() is not { } unloaded)
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
}
