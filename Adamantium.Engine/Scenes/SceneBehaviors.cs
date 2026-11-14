using System;
using System.Collections.Generic;
using System.Reflection;
using Adamantium.ECS;
using Adamantium.ECS.Components;
using Serilog;

namespace Adamantium.Engine.Scenes;

/// <summary>Moves the game's components between an entity and their scene entries.</summary>
internal static class SceneBehaviors
{
    /// <summary>Puts the component <paramref name="entry"/> describes on <paramref name="entity"/>, its type found in
    /// <paramref name="code"/>. The entry is kept on the entity instead when the type is not loaded, the entity has one
    /// of that type already, or its code throws - then the throw is added to <paramref name="problems"/>.</summary>
    public static void Attach(Entity entity, SceneBehavior entry, WorldCode code, List<BehaviorProblem> problems)
    {
        var type = code.FindType<Behavior>(entry.Type);
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

        var behavior = Make(entity, entry, type, problems);
        if (behavior == null || !TryAdd(entity, behavior, entry.Type, problems))
        {
            Keep(entity, entry);
        }
    }

    /// <summary>Puts <paramref name="behavior"/> on <paramref name="entity"/>; false when its code throws as it is
    /// added - its <c>Initialize</c>, say - then it is taken off again and the throw added to
    /// <paramref name="problems"/>.</summary>
    public static bool TryAdd(Entity entity, Behavior behavior, string type, List<BehaviorProblem> problems)
    {
        try
        {
            entity.AddComponent(behavior);
            return true;
        }
        catch (Exception exception)
        {
            // The game's own code: whatever it throws is its fault, and must cost the scene no more than this component.
            Log.Logger.Error(exception, "Scene: {Type} on {Name} threw as it was added; its saved values are kept unloaded",
                type, entity.Name);
            problems.Add(new BehaviorProblem(entity, type, exception));
            entity.RemoveComponent(behavior);
            return false;
        }
    }

    /// <summary>A component of <paramref name="type"/> with the fields <paramref name="entry"/> keeps, by name; null when
    /// its code throws - then the throw is added to <paramref name="problems"/>.</summary>
    public static Behavior Make(Entity entity, SceneBehavior entry, Type type, List<BehaviorProblem> problems)
    {
        try
        {
            var behavior = (Behavior)Activator.CreateInstance(type);
            ComponentFields.Read(entry.Values, behavior, typeof(Behavior));
            return behavior;
        }
        catch (Exception exception) when (exception is TargetInvocationException or TypeInitializationException)
        {
            var thrown = exception.InnerException ?? exception;
            Log.Logger.Error(thrown, "Scene: could not create {Type} on {Name}; its saved values are kept unloaded",
                type.FullName, entity.Name);
            problems.Add(new BehaviorProblem(entity, entry.Type, thrown));
            return null;
        }
    }

    /// <summary>The entry the scene writes for <paramref name="behavior"/> on the entity <paramref name="on"/>.</summary>
    public static SceneBehavior Write(Behavior behavior, UInt128 on)
    {
        var entry = new SceneBehavior { On = on, Type = behavior.GetType().FullName };
        ComponentFields.Write(behavior, typeof(Behavior), entry.Values, SceneBehavior.OwnAttributes);
        return entry;
    }

    /// <summary>Keeps <paramref name="entry"/> on <paramref name="entity"/> as it was read, among its
    /// <see cref="UnloadedBehaviors"/>.</summary>
    public static void Keep(Entity entity, SceneBehavior entry)
    {
        var unloaded = entity.GetComponent<UnloadedBehaviors>();
        var entries = new List<SceneBehavior>();
        if (unloaded != null)
        {
            entries.AddRange(unloaded.Entries);
            entity.RemoveComponent(unloaded);
        }

        entries.Add(entry);
        entity.AddComponent(new UnloadedBehaviors(entries));
    }

    /// <summary>Takes <paramref name="entry"/> out of those <paramref name="entity"/> keeps.</summary>
    public static void Drop(Entity entity, SceneBehavior entry)
    {
        if (entity.GetComponent<UnloadedBehaviors>() is not { } unloaded)
        {
            return;
        }

        var kept = new List<SceneBehavior>(unloaded.Entries.Count);
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
            entity.AddComponent(new UnloadedBehaviors(kept));
        }
    }

    /// <summary>Whether <paramref name="entity"/> keeps <paramref name="entry"/>.</summary>
    public static bool Keeps(Entity entity, SceneBehavior entry)
    {
        if (entity.GetComponent<UnloadedBehaviors>() is not { } unloaded)
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
