using System;
using System.Collections.Generic;
using Adamantium.ECS.Components;
using Serilog;

namespace Adamantium.ECS;

/// <summary>The game playing a scene: starts the world's game services (<see cref="IGameService"/>) on it and wakes
/// them, puts them to sleep on pause, and stops them when it stops. The world runs them as any service of its own; one
/// that throws - or fails to start - sleeps for the rest of the game, and is still stopped with it. One game at a time
/// plays in a world.</summary>
public sealed class GameRun
{
    private readonly List<IGameService> _services = [];
    private readonly HashSet<IGameService> _failed = [];
    private bool _paused;

    private GameRun(Scene scene)
    {
        Scene = scene;
    }

    /// <summary>The scene played.</summary>
    public Scene Scene { get; }

    /// <summary>The game services playing it, those that failed to start among them.</summary>
    public IReadOnlyList<IGameService> Services => _services;

    /// <summary>Whether the game is paused: its services sleep. One asleep already when the game pauses - put to sleep
    /// by the world for throwing, or by itself - is not woken when it goes on. On the world's thread.</summary>
    public bool IsPaused
    {
        get => _paused;
        set => SetPaused(value);
    }

    /// <summary>Starts the game on <paramref name="scene"/>: starts each game service of its world on the scene and wakes
    /// it; the world runs them from the next frame. On the world's thread.</summary>
    /// <exception cref="InvalidOperationException">A game plays in the world already.</exception>
    public static GameRun Start(Scene scene)
    {
        ArgumentNullException.ThrowIfNull(scene);
        var services = scene.World.GetServices<IGameService>();
        foreach (var service in services)
        {
            if (service.Scene != null)
                throw new InvalidOperationException($"A game plays {service.Scene.Name} in this world already.");
        }

        var run = new GameRun(scene);
        foreach (var service in services)
        {
            run._services.Add(service);
            try
            {
                service.Start(scene);
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

    /// <summary>Stops the game: puts its services to sleep and stops each. On the world's thread.</summary>
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
        }

        _services.Clear();
        _failed.Clear();
    }

    /// <summary>Tells the services that <paramref name="component"/> of the scene played was edited, playing or paused;
    /// one that throws sleeps for the rest of the game. On the world's thread.</summary>
    public void TellComponentEdited(GameComponent component)
    {
        ArgumentNullException.ThrowIfNull(component);
        // A game stopped has no services left.
        if (_services.Count == 0 || component.Owner?.Scene != Scene)
            return;

        foreach (var service in _services)
        {
            // Asleep for throwing: put among the failed on pause, or before it found asleep while playing.
            if (_failed.Contains(service) || !_paused && !service.IsActive)
                continue;

            try
            {
                service.ComponentEdited(component);
            }
            catch (Exception exception)
            {
                Log.Logger.Error(exception, "The game service {Service} failed on an edit and sleeps for the game",
                    service.GetType().FullName);
                _failed.Add(service);
                service.IsActive = false;
            }
        }
    }

    private void SetPaused(bool paused)
    {
        if (_paused == paused)
            return;

        _paused = paused;
        if (paused)
            PutServicesToSleep();
        else
            WakeServices();
    }

    private void PutServicesToSleep()
    {
        foreach (var service in _services)
        {
            if (!service.IsActive)
                _failed.Add(service);

            service.IsActive = false;
        }
    }

    private void WakeServices()
    {
        foreach (var service in _services)
        {
            if (!_failed.Contains(service))
                service.IsActive = true;
        }
    }
}
