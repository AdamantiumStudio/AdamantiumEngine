using Adamantium.ECS.Components;

namespace Adamantium.ECS;

/// <summary>A ready base for an <see cref="IGameService"/>: a service of the world that acts on the game's components
/// while the game plays a scene. It sleeps (<see cref="EntityService.IsActive"/> false) until a game plays
/// (<see cref="GameRun"/>), and sleeps again on pause and when the game stops. A kind needs a public constructor taking
/// the world.</summary>
public abstract class GameService : EntityService, IGameService
{
    protected GameService(EntityWorld world) : base(world)
    {
        IsActive = false;
    }

    public Scene Scene { get; private set; }

    public sealed override bool IsUpdateService => true;

    public sealed override bool IsRenderingService => false;

    public sealed override EntityServiceType ServiceType => EntityServiceType.Update;

    /// <summary>The game's code failing does not stop the frame: the service sleeps for the rest of the game.</summary>
    public sealed override bool SleepsWhenUpdateFails => true;

    public void Start(Scene scene)
    {
        Scene = scene;
        OnStart();
    }

    public void Stop()
    {
        try
        {
            OnStop();
        }
        finally
        {
            Scene = null;
        }
    }

    /// <summary>Called once when a game starts, its <see cref="Scene"/> given, before its first frame: the place to begin
    /// the service's state anew.</summary>
    protected virtual void OnStart()
    {
    }

    /// <summary>Called once when the game stops, before the scene is let go.</summary>
    protected virtual void OnStop()
    {
    }

    /// <summary>Every component of type <typeparamref name="T"/> in the scene played, on entities enabled in their tree:
    /// a selection from the scene's index (<see cref="Scene.Components"/>), nothing allocated. Empty while no game plays.
    /// </summary>
    protected ComponentSelection<T> Each<T>() where T : class, IComponent => Scene?.Components.Of<T>() ?? default;
}
