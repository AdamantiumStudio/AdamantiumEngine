using System;
using System.Collections.Generic;
using Adamantium.ECS.Components;

namespace Adamantium.ECS;

/// <summary>The logic of the game's or a mod's code: what acts on its components while the game plays a scene. One of
/// each kind the world's code brings is made for the scene played when the game starts (<see cref="GameRun"/>) and let go
/// when it stops; none runs while a scene is edited. A kind needs a public constructor taking nothing.</summary>
public abstract class GameService
{
    private readonly Stack<Entity> _walk = new();

    /// <summary>The scene played.</summary>
    public Scene Scene { get; internal set; }

    /// <summary>The world the scene is in.</summary>
    public EntityWorld World => Scene?.World;

    /// <summary>How long the game has played, its pauses left out.</summary>
    public TimeSpan PlayTime { get; internal set; }

    /// <summary>The seconds the frame played took.</summary>
    public double FrameTime { get; internal set; }

    /// <summary>Called once when the game starts, before its first frame.</summary>
    public virtual void Start()
    {
    }

    /// <summary>Called on every frame the game plays, on the world's thread.</summary>
    public abstract void Update();

    /// <summary>Called once when the game stops.</summary>
    public virtual void Stop()
    {
    }

    /// <summary>Every component of kind <typeparamref name="T"/> in the scene played, over the whole tree of each of its
    /// roots.</summary>
    protected IEnumerable<T> Each<T>() where T : class, IComponent
    {
        if (Scene == null)
            yield break;

        _walk.Clear();
        for (var i = Scene.RootEntities.Count - 1; i >= 0; i--)
        {
            _walk.Push(Scene.RootEntities[i]);
        }

        while (_walk.Count > 0)
        {
            var entity = _walk.Pop();
            if (entity.GetComponent<T>() is { } component)
                yield return component;

            var children = entity.Dependencies;
            for (var i = children.Count - 1; i >= 0; i--)
            {
                if (children[i] != null)
                    _walk.Push(children[i]);
            }
        }
    }
}
