using System;
using Adamantium.DarkMatter.Shapes;

namespace Adamantium.DarkMatter;

/// <summary>A query told link by link on the caller's stack: its <see cref="QueryFilter"/> gathered, then answered by
/// the simulation's own ray, cast or overlap.</summary>
public readonly ref struct QueryBuilder
{
    private readonly PhysicsSimulation _simulation;
    private readonly QueryFilter _filter;

    internal QueryBuilder(PhysicsSimulation simulation, QueryFilter filter)
    {
        _simulation = simulation;
        _filter = filter;
    }

    /// <summary>Meets only bodies on <paramref name="layers"/>; told again, on both.</summary>
    public QueryBuilder OnLayers(LayerMask layers) =>
        new(_simulation, _filter with { Layers = new LayerMask(_filter.Layers.Bits & layers.Bits) });

    /// <summary>Passes <paramref name="body"/> by: one body; told again, the new one.</summary>
    public QueryBuilder Except(BodyId body) => new(_simulation, _filter with { Except = body });

    /// <summary>Meets triggers too.</summary>
    public QueryBuilder MeetingTriggers() => new(_simulation, _filter with { MeetsTriggers = true });

    public bool RayCast(WorldPosition origin, Vector3F direction, float maxDistance, out RayHit hit) =>
        _simulation.RayCast(origin, direction, maxDistance, out hit, _filter);

    public int RayCast(WorldPosition origin, Vector3F direction, float maxDistance, Span<RayHit> hits) =>
        _simulation.RayCast(origin, direction, maxDistance, hits, _filter);

    public bool CastShape(CollisionShape shape, in Pose pose, Vector3F translation, out ShapeHit hit) =>
        _simulation.CastShape(shape, pose, translation, out hit, _filter);

    public int Overlap(CollisionShape shape, in Pose pose, Span<BodyId> bodies) =>
        _simulation.Overlap(shape, pose, bodies, _filter);
}
