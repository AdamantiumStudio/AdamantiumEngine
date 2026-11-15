namespace Adamantium.ECS;

internal sealed record ReturnedEntity(Entity Entity, Entity Owner, Scene Scene, int Index);
