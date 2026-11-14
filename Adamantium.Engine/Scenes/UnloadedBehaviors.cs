using System.Collections.Generic;
using Adamantium.ECS.Components;

namespace Adamantium.Engine.Scenes;

/// <summary>Stands on an entity for the components of the game's or a mod's code whose types are not loaded - the code
/// not built, the mod off: it keeps their scene entries as they were read, so writing the scene back does not lose
/// them. One on an entity, however many such components it has.</summary>
public sealed class UnloadedBehaviors : Component
{
    /// <summary>The entries as the scene file has them.</summary>
    public List<SceneBehavior> Entries { get; } = [];
}
