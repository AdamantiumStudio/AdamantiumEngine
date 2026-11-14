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
        if (behavior == null)
        {
            Keep(entity, entry);
            return;
        }

        entity.AddComponent(behavior);
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
        if (unloaded == null)
        {
            unloaded = new UnloadedBehaviors();
            entity.AddComponent(unloaded);
        }

        unloaded.Entries.Add(entry);
    }
}
