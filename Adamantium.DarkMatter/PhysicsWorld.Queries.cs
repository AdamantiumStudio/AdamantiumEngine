using System;
using System.Collections.Generic;
using Adamantium.DarkMatter.Shapes;

namespace Adamantium.DarkMatter;

public sealed partial class PhysicsWorld
{
    private readonly List<(int Item, float Entry)> _rayCandidates = [];
    private readonly List<int> _overlapped = [];
    private readonly HashSet<int> _overlappedBodies = [];

    /// <inheritdoc/>
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

            ref var part = ref _parts[candidate.Item];
            var pose = PlacePart(_bodies[part.Body], part);
            var localRay = new Ray<float>(pose.InverseTransformPoint(ray.Position), pose.InverseTransformDirection(direction));
            if (!part.Shape.CastRay(localRay, nearest, out var distance, out var normal))
                continue;

            nearest = distance;
            found = true;
            hit = new RayHit(GetHandle(part.Body), ray.Position + direction * distance, pose.TransformDirection(normal),
                distance);
        }

        return found;
    }

    /// <inheritdoc/>
    public void Overlap(CollisionShape shape, in Pose pose, ICollection<BodyHandle> into)
    {
        ArgumentNullException.ThrowIfNull(shape);
        ArgumentNullException.ThrowIfNull(into);
        if (SurfaceCollisionShape.HoldsSurface(shape))
            throw new ArgumentException("A mesh or a height field holds no volume to look for overlaps with.",
                nameof(shape));

        var placedPose = new Pose(pose.Position, NormalizeRotation(pose.Rotation, nameof(pose)));
        _overlapped.Clear();
        _overlappedBodies.Clear();
        if (shape is CompoundCollisionShape compound)
        {
            foreach (var part in compound.Parts)
            {
                OverlapPart(part.Shape, placedPose.Transform(part.Pose));
            }
        }
        else
        {
            OverlapPart(shape, placedPose);
        }

        foreach (var body in _overlapped)
        {
            into.Add(GetHandle(body));
        }
    }

    private void OverlapPart(CollisionShape shape, in Pose pose)
    {
        _found.Clear();
        _tree.Query(shape.ComputeBounds(pose), _found);
        var placed = new PlacedShape(shape, pose);
        foreach (var index in _found)
        {
            ref var part = ref _parts[index];
            if (_overlappedBodies.Contains(part.Body))
                continue;

            var partPose = PlacePart(_bodies[part.Body], part);
            var meets = part.Shape is SurfaceCollisionShape surface
                ? MeetsSurface(shape, pose, surface, partPose)
                : Gjk.Intersects(placed, new PlacedShape(part.Shape, partPose));
            if (meets)
            {
                _overlappedBodies.Add(part.Body);
                _overlapped.Add(part.Body);
            }
        }
    }

    // A shape meets a surface where it meets one of the triangles near it.
    private bool MeetsSurface(CollisionShape shape, in Pose pose, SurfaceCollisionShape surface, in Pose surfacePose)
    {
        var placed = new PlacedShape(shape, pose);
        _triangles.Clear();
        surface.FindTriangles(shape.ComputeBounds(surfacePose.InverseTransform(pose)), _triangles);
        foreach (var triangle in _triangles)
        {
            if (!surface.TryGetTriangle(triangle, out var first, out var second, out var third))
                continue;

            _triangle.Set(first, second, third, surface.GetTriangleNormal(triangle, first, second, third));
            if (Gjk.Intersects(placed, new PlacedShape(_triangle, surfacePose)))
                return true;
        }

        return false;
    }

    private static int CompareEntries((int Item, float Entry) first, (int Item, float Entry) second) =>
        first.Entry.CompareTo(second.Entry);
}
