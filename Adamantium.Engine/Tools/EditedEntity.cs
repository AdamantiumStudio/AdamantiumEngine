using Adamantium.ECS;

namespace Adamantium.Engine.Tools;

/// <summary>One entity a gesture changed, with how it stood before the gesture and after it.</summary>
/// <param name="Entity">The entity changed.</param>
/// <param name="Before">How it stood when the gesture began.</param>
/// <param name="After">How it stood when the gesture ended.</param>
public sealed record EditedEntity(Entity Entity, EditedState Before, EditedState After);
