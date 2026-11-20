using System;
using System.Collections.Generic;
using Adamantium.DarkMatter.Shapes;

namespace Adamantium.DarkMatter;

// Queries: answered at once, of the bodies where the last step left them.
public sealed partial class PhysicsSimulation
{
    private readonly List<BodyHandle> _overlapped = [];
    private WorldRayHit[] _rayHits = [];

    /// <summary>Where a ray from <paramref name="origin"/> along <paramref name="direction"/> first meets a body within
    /// <paramref name="maxDistance"/> meters; a ray starting inside a body meets it at once.</summary>
    public bool RayCast(WorldPosition origin, Vector3F direction, float maxDistance, out RayHit hit,
        in QueryFilter filter = default)
    {
        ThrowIfDisposed();
        hit = default;
        if (!_world.RayCast(origin, direction, maxDistance, out var met, ExceptOf(filter)))
            return false;

        hit = new RayHit(IdOf(met.Body), met.Part, met.Position, met.Normal, met.Distance);
        return true;
    }

    /// <summary>Writes into <paramref name="hits"/> every body the ray meets, nearest first - the nearest as many as fit
    /// - and gives how many it wrote.</summary>
    public int RayCast(WorldPosition origin, Vector3F direction, float maxDistance, Span<RayHit> hits,
        in QueryFilter filter = default)
    {
        ThrowIfDisposed();
        if (_rayHits.Length < hits.Length)
            _rayHits = new WorldRayHit[hits.Length];

        var met = _rayHits.AsSpan(0, hits.Length);
        var count = _world.RayCast(origin, direction, maxDistance, met, ExceptOf(filter));
        for (var index = 0; index < count; index++)
        {
            ref var hit = ref met[index];
            hits[index] = new RayHit(IdOf(hit.Body), hit.Part, hit.Position, hit.Normal, hit.Distance);
        }

        return count;
    }

    /// <summary>The first body <paramref name="shape"/> meets moved by <paramref name="translation"/> without turning,
    /// and where it stops, within a millimeter of it; meshes and fields cannot be cast.</summary>
    public bool CastShape(CollisionShape shape, in Pose pose, Vector3F translation, out ShapeHit hit,
        in QueryFilter filter = default)
    {
        ThrowIfDisposed();
        hit = default;
        if (!_world.CastShape(shape, pose, translation, out var met, ExceptOf(filter)))
            return false;

        hit = new ShapeHit(IdOf(met.Body), met.Part, met.Position, met.Normal, met.Distance);
        return true;
    }

    /// <summary>Writes into <paramref name="bodies"/> the bodies that share a point with <paramref name="shape"/>
    /// standing at <paramref name="pose"/> - as many as fit - and gives how many it wrote.</summary>
    public int Overlap(CollisionShape shape, in Pose pose, Span<BodyId> bodies, in QueryFilter filter = default)
    {
        ThrowIfDisposed();
        _overlapped.Clear();
        _world.Overlap(shape, pose, _overlapped, ExceptOf(filter));
        var count = Math.Min(bodies.Length, _overlapped.Count);
        for (var index = 0; index < count; index++)
        {
            bodies[index] = IdOf(_overlapped[index]);
        }

        return count;
    }

    // The core's handle of the body passed by; none for one not in the core.
    private BodyHandle ExceptOf(in QueryFilter filter) =>
        !filter.Except.IsNone && TryFind(filter.Except, out var body) ? body.Handle : default;

    private BodyId IdOf(BodyHandle handle)
    {
        var slot = _slotOfCoreBody[handle.Index];
        return new BodyId(slot, _bodySlots[slot].Generation);
    }
}
