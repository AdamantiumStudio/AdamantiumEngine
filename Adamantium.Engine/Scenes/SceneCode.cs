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

        var entries = new List<SceneBehavior>(unloaded.Entries);
        unloaded.Entries.Clear();
        foreach (var entry in entries)
        {
            SceneBehaviors.Attach(entity, entry, code, problems);
        }

        if (unloaded.Entries.Count == 0)
            entity.RemoveComponent(unloaded);
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
