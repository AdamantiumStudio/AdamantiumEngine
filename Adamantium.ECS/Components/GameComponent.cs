namespace Adamantium.ECS.Components;

/// <summary>A component of the game's or a mod's code: data its services act on. Its public properties with a getter
/// and a setter, declared on its own type, are what a scene keeps of it; each sets through <c>SetProperty</c>, so the
/// editor follows its changes.</summary>
public abstract class GameComponent : Component
{
}
