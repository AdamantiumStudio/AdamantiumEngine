using Adamantium.ECS.Components;

namespace Adamantium.ECS;

/// <summary>A service of the game's or a mod's logic: what the engine works with when a game plays. One of each kind is
/// made with the code that brings it - from a public constructor taking the world - asleep until a game plays; see
/// <see cref="GameService"/> for a ready base.</summary>
public interface IGameService : IEntityService
{
    /// <summary>The scene played; none while no game plays.</summary>
    Scene Scene { get; }

    /// <summary>The game starts on <paramref name="scene"/>, before its first frame: the service takes the scene and
    /// begins its state anew. It is woken after.</summary>
    void Start(Scene scene);

    /// <summary>The game stops: the service, already asleep, lets the scene go.</summary>
    void Stop();

    /// <summary>A component of the scene played was changed from outside the game - by the editor - awake or paused: the
    /// service brings up to date what it makes of it, so the change shows at once.</summary>
    void ComponentEdited(GameComponent component)
    {
    }

    /// <summary>The game is paused: the service, put to sleep after, holds what runs of it on its own.</summary>
    void Pause()
    {
    }

    /// <summary>The game goes on: the service, woken after, lets what it held run again.</summary>
    void Resume()
    {
    }
}
