using System.Collections.Generic;
using Adamantium.ECS.Components;

namespace Adamantium.Engine.Scenes;

/// <summary>Stands on an entity for the components of the game's or a mod's code whose types are not loaded - the code
/// not built, the mod off: it keeps their scene entries as they were read, so writing the scene back does not lose
/// them. One on an entity, however many such components it has. Its entries never change once it is made: another one
/// takes its place instead, so a thread reading it while the world's changes it reads one whole set.</summary>
public sealed class UnloadedGameComponents : Component
{
    public UnloadedGameComponents(IReadOnlyList<SceneGameComponent> entries)
    {
        Entries = [.. entries];
    }

    /// <summary>The entries as the scene file has them.</summary>
    public IReadOnlyList<SceneGameComponent> Entries { get; }

    /// <summary>A copy keeping the same entries: a copied entity keeps its components not loaded too - a scene writes each
    /// entry on the entity that keeps it.</summary>
    public override IComponent Clone() => new UnloadedGameComponents(Entries);
}
