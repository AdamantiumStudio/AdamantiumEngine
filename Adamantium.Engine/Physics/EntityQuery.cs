using System;
using Adamantium.DarkMatter;
using Adamantium.DarkMatter.Shapes;
using Adamantium.ECS;
using Adamantium.Mathematics;

namespace Adamantium.Engine.Physics;

/// <summary>A query of the game's physics told link by link on the caller's stack: its <see cref="EntityQueryFilter"/>
/// gathered, then answered by the service's own ray, cast or overlap.</summary>
public readonly ref struct EntityQuery
{
    private readonly IPhysicsService _physics;
    private readonly EntityQueryFilter _filter;

    internal EntityQuery(IPhysicsService physics, EntityQueryFilter filter)
    {
        _physics = physics;
        _filter = filter;
    }

    /// <summary>Passes <paramref name="entity"/>'s body by: one entity; told again, the new one.</summary>
    public EntityQuery Except(Entity entity) => new(_physics, _filter with { Except = entity });

    /// <summary>Meets triggers too.</summary>
    public EntityQuery MeetingTriggers() => new(_physics, _filter with { MeetsTriggers = true });

    public bool RayCast(WorldPosition origin, Vector3F direction, float maxDistance, out EntityRayHit hit) =>
        _physics.RayCast(origin, direction, maxDistance, out hit, _filter);

    public int RayCast(WorldPosition origin, Vector3F direction, float maxDistance, Span<EntityRayHit> hits) =>
        _physics.RayCast(origin, direction, maxDistance, hits, _filter);

    public bool CastShape(CollisionShape shape, in Pose pose, Vector3F translation, out EntityShapeHit hit) =>
        _physics.CastShape(shape, pose, translation, out hit, _filter);

    public int Overlap(CollisionShape shape, in Pose pose, Span<Entity> entities) =>
        _physics.Overlap(shape, pose, entities, _filter);
}
