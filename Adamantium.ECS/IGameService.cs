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
}
