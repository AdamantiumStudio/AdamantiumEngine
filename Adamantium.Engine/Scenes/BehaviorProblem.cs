using System;
using Adamantium.ECS;

namespace Adamantium.Engine.Scenes;

/// <summary>A component of the game's code that could not be made on <paramref name="Entity"/>: the full name of its
/// <paramref name="Type"/> and what its code threw. Its scene entry is kept as it was.</summary>
public sealed record BehaviorProblem(Entity Entity, string Type, Exception Exception);
