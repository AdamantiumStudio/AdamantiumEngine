using Adamantium.ECS.Components;

namespace Adamantium.Engine.Scenes;

/// <summary>Stands on an entity for a procedural shape whose type is not loaded - a mod's shape with the mod off: it
/// keeps the scene's entry as it was read, so writing the scene back does not lose it.</summary>
public sealed class UnloadedShape : Component
{
    public UnloadedShape(SceneShape entry)
    {
        Entry = entry;
    }

    /// <summary>The entry as the scene file has it.</summary>
    public SceneShape Entry { get; }
}
