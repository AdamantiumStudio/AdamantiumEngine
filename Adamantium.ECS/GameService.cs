using System.Collections.Generic;
using Adamantium.ECS.Components;

namespace Adamantium.ECS;

/// <summary>The logic of the game's or a mod's code: a service of the world that acts on the game's components while
/// the game plays a scene. One of each kind is made with the code that brings it and let go with it; it sleeps
/// (<see cref="EntityService.IsActive"/> false) until a game plays (<see cref="GameRun"/>), and sleeps again on pause and
/// when the game stops. A kind needs a public constructor taking the world.</summary>
public abstract class GameService : EntityService
{
    protected GameService(EntityWorld world) : base(world)
    {
        IsActive = false;
    }

    /// <summary>The scene played; none while no game plays.</summary>
    public Scene Scene { get; internal set; }

    public sealed override bool IsUpdateService => true;

    public sealed override bool IsRenderingService => false;

    public sealed override EntityServiceType ServiceType => EntityServiceType.Update;

    /// <summary>The game's code failing does not stop the frame: the service sleeps for the rest of the game.</summary>
    public sealed override bool SleepsWhenUpdateFails => true;

    /// <summary>Called once when a game starts, before its first frame: the place to begin the service's state anew.
    /// </summary>
    public virtual void Start()
    {
    }

    /// <summary>Called once when the game stops.</summary>
    public virtual void Stop()
    {
    }

    /// <summary>Every component of kind <typeparamref name="T"/> in the scene played, over the whole tree of each of its
    /// roots. Each walk is its own: walks may go on inside one another.</summary>
    protected IEnumerable<T> Each<T>() where T : class, IComponent
    {
        if (Scene == null)
            yield break;

        var walk = new Stack<Entity>();
        for (var i = Scene.RootEntities.Count - 1; i >= 0; i--)
        {
            walk.Push(Scene.RootEntities[i]);
        }

        while (walk.Count > 0)
        {
            var entity = walk.Pop();
            if (entity.GetComponent<T>() is { } component)
                yield return component;

            var children = entity.Dependencies;
            for (var i = children.Count - 1; i >= 0; i--)
            {
                if (children[i] != null)
                    walk.Push(children[i]);
            }
        }
    }
}
