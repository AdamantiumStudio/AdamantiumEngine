using System;
using System.Collections.Generic;
using Serilog;

namespace Adamantium.ECS;

/// <summary>The game playing a scene: wakes the world's game services (<see cref="GameService"/>) for it, puts them to
/// sleep on pause, and when it stops. The world runs them as any service of its own; one that throws - or fails to start
/// - sleeps for the rest of the game. One game at a time plays in a world.</summary>
public sealed class GameRun
{
    private readonly List<GameService> _services = [];
    private readonly HashSet<GameService> _failed = [];
    private bool _paused;

    private GameRun(Scene scene)
    {
        Scene = scene;
    }

    /// <summary>The scene played.</summary>
    public Scene Scene { get; }

    /// <summary>The game services given the scene, those that failed to start among them.</summary>
    public IReadOnlyList<GameService> Services => _services;

    /// <summary>Whether the game is paused: its services sleep. One asleep already when the game pauses - put to sleep
    /// by the world for throwing, or by itself - is not woken when it goes on. On the world's thread.</summary>
    public bool IsPaused
    {
        get => _paused;
        set
        {
            if (_paused == value)
                return;

            _paused = value;
            foreach (var service in _services)
            {
                if (value)
                {
                    // The world put to sleep a service that threw: it is out of the game, not woken on going on.
                    if (!service.IsActive)
                        _failed.Add(service);

                    service.IsActive = false;
                }
                else if (!_failed.Contains(service))
                {
                    service.IsActive = true;
                }
            }
        }
    }

    /// <summary>Starts the game on <paramref name="scene"/>: gives each game service of its world the scene, starts it
    /// and wakes it; the world runs them from the next frame. On the world's thread.</summary>
    /// <exception cref="InvalidOperationException">A game plays in the world already.</exception>
    public static GameRun Start(Scene scene)
    {
        ArgumentNullException.ThrowIfNull(scene);
        var services = scene.World.GetServices<GameService>();
        foreach (var service in services)
        {
            if (service.Scene != null)
                throw new InvalidOperationException($"A game plays {service.Scene.Name} in this world already.");
        }

        var run = new GameRun(scene);
        foreach (var service in services)
        {
            // In the game even when it fails to start - asleep, and told when the game stops - so the scene is taken
            // from it then, and no other game starts in the world meanwhile.
            service.Scene = scene;
            run._services.Add(service);
            try
            {
                service.Start();
            }
            catch (Exception exception)
            {
                Log.Logger.Error(exception, "The game service {Service} failed to start and sleeps for the game",
                    service.GetType().FullName);
                run._failed.Add(service);
                continue;
            }

            service.IsActive = true;
        }

        return run;
    }

    /// <summary>Stops the game: puts its services to sleep, tells each, and takes the scene from them. On the world's
    /// thread.</summary>
    public void Stop()
    {
        foreach (var service in _services)
        {
            service.IsActive = false;
            try
            {
                service.Stop();
            }
            catch (Exception exception)
            {
                Log.Logger.Error(exception, "The game service {Service} failed to stop", service.GetType().FullName);
            }

            service.Scene = null;
        }

        _services.Clear();
        _failed.Clear();
    }
}
