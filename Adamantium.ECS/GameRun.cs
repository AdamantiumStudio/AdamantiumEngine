using System;
using System.Collections.Generic;
using Adamantium.Core;
using Serilog;

namespace Adamantium.ECS;

/// <summary>The game playing a scene: one service of each kind the world's code brings (<see cref="GameService"/>), made
/// for the scene when the game starts and let go when it stops. A service of the world, run every frame while it plays;
/// paused, it is not, and the time it was paused is not played. A game service that throws is told, and taken out of the
/// run so as not to throw again every frame.</summary>
public sealed class GameRun : EntityService
{
    private readonly List<GameService> _services = [];
    private TimeSpan _playTime;

    private GameRun(EntityWorld world, Scene scene) : base(world)
    {
        Scene = scene;
        Name = $"Game of {scene.Name}";
    }

    /// <summary>The scene played.</summary>
    public Scene Scene { get; }

    /// <summary>The game services playing it.</summary>
    public IReadOnlyList<GameService> Services => _services;

    /// <summary>Whether the game is paused: its services wait, and its time stands still.</summary>
    public bool IsPaused
    {
        get => !IsActive;
        set => IsActive = !value;
    }

    /// <summary>How long the game has played, its pauses left out.</summary>
    public TimeSpan PlayTime => _playTime;

    public override bool IsUpdateService => true;

    public override bool IsRenderingService => false;

    public override EntityServiceType ServiceType => EntityServiceType.Update;

    /// <summary>Starts the game on <paramref name="scene"/>: makes one service of each kind the world's code brings, starts
    /// each, and puts the run among the world's services, which run it from the next frame. On the world's thread.
    /// </summary>
    public static GameRun Start(Scene scene)
    {
        ArgumentNullException.ThrowIfNull(scene);
        var run = new GameRun(scene.World, scene);
        foreach (var type in scene.World.Code.GetGivenTypes<GameService>())
        {
            try
            {
                var service = (GameService)Activator.CreateInstance(type);
                service.Scene = scene;
                run._services.Add(service);
            }
            catch (Exception exception)
            {
                Log.Logger.Error(exception, "The game service {Service} could not be made", type.FullName);
            }
        }

        for (var i = 0; i < run._services.Count; i++)
        {
            if (!run.Call(run._services[i], "start", static started => started.Start()))
                i--;
        }

        scene.World.AddService(run);
        return run;
    }

    public override void Update(AppTime appTime)
    {
        _playTime += TimeSpan.FromSeconds(appTime.FrameTime);
        for (var i = 0; i < _services.Count; i++)
        {
            var service = _services[i];
            service.PlayTime = _playTime;
            service.FrameTime = appTime.FrameTime;
            if (!Call(service, "update", static updated => updated.Update()))
                i--;
        }
    }

    /// <summary>Stops the game: tells each service, lets them go, and takes the run out of the world's services. On the
    /// world's thread.</summary>
    public void Stop()
    {
        foreach (var service in _services)
        {
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
        EntityWorld.RemoveService(this);
    }

    // False when the service threw, and was taken out.
    private bool Call(GameService service, string what, Action<GameService> call)
    {
        try
        {
            call(service);
            return true;
        }
        catch (Exception exception)
        {
            Log.Logger.Error(exception, "The game service {Service} failed to {What} and is taken out of the game",
                service.GetType().FullName, what);
            _services.Remove(service);
            return false;
        }
    }
}
