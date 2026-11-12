using Adamantium.ECS.Components;

namespace Adamantium.Engine.Scenes;

/// <summary>Stands on an entity for a component of the game's or a mod's code whose type is not loaded - the code not
/// built, the mod off: it keeps the scene's entry as it was read, so writing the scene back does not lose it.</summary>
public sealed class UnloadedBehaviour : Component
{
    public UnloadedBehaviour(SceneBehaviour entry)
    {
        Entry = entry;
    }

    /// <summary>The entry as the scene file has it.</summary>
    public SceneBehaviour Entry { get; }
}
