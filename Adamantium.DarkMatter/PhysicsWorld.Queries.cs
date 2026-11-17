using System;
using System.Collections.Generic;
using Adamantium.DarkMatter.Shapes;

namespace Adamantium.DarkMatter;

public sealed partial class PhysicsWorld
{
    private readonly List<(int Item, float Entry)> _rayCandidates = [];

    /// <summary>Where <paramref name="ray"/> first meets a body within <paramref name="maxDistance"/> meters; a ray
    /// starting inside a body meets it at once.</summary>
    public bool RayCast(in Ray<float> ray, float maxDistance, out RayHit hit)
    {
        hit = default;
        var length = ray.Direction.Length();
        if (length <= 0 || maxDistance < 0)
            return false;

        var direction = ray.Direction / length;
        var worldRay = new Ray<float>(ray.Position, direction);
        _rayCandidates.Clear();
        _tree.RayCast(worldRay, _rayCandidates, maxDistance);
        _rayCandidates.Sort(CompareEntries);

        var nearest = maxDistance;
        var found = false;
        foreach (var candidate in _rayCandidates)
        {
            if (candidate.Entry > nearest)
                break;

            ref var body = ref _bodies[candidate.Item];
            var pose = body.Pose;
            var localRay = new Ray<float>(pose.InverseTransformPoint(ray.Position), pose.InverseTransformDirection(direction));
            if (!body.Shape.CastRay(localRay, nearest, out var distance, out var normal))
                continue;

            nearest = distance;
            found = true;
            hit = new RayHit(GetHandle(candidate.Item), ray.Position + direction * distance,
                pose.TransformDirection(normal), distance);
        }

        return found;
    }

    /// <summary>Adds to <paramref name="into"/> every body that shares a point with <paramref name="shape"/> standing
    /// at <paramref name="pose"/>.</summary>
    public void Overlap(Shape shape, in Pose pose, List<RigidBody> into)
    {
        ArgumentNullException.ThrowIfNull(shape);
        ArgumentNullException.ThrowIfNull(into);
        var placedPose = new Pose(pose.Position, NormalizeRotation(pose.Rotation, nameof(pose)));
        _found.Clear();
        _tree.Query(shape.ComputeBounds(placedPose), _found);
        var placed = new PlacedShape(shape, placedPose);
        foreach (var index in _found)
        {
            ref var body = ref _bodies[index];
            if (Gjk.Intersects(placed, new PlacedShape(body.Shape, body.Pose)))
                into.Add(GetHandle(index));
        }
    }

    private static int CompareEntries((int Item, float Entry) first, (int Item, float Entry) second) =>
        first.Entry.CompareTo(second.Entry);
}
