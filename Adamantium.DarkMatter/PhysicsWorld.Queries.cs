using System;
using System.Collections.Generic;
using Adamantium.DarkMatter.CollisionDetection;
using Adamantium.DarkMatter.Shapes;

namespace Adamantium.DarkMatter;

// Queries are worked out in float in the frame of where they start, as collisions are in a pair's: the bodies they meet
// stand near that point wherever it is in the world.
public sealed partial class PhysicsWorld
{
    private readonly List<(int Item, double Entry)> _rayCandidates = [];
    private readonly List<int> _overlapped = [];
    private readonly HashSet<int> _overlappedBodies = [];
    private readonly ManifoldBuffer _castBuffer = new();
    private readonly List<CastTriangle> _castTriangles = [];

    /// <inheritdoc/>
    public bool RayCast(Vector3<double> origin, Vector3F direction, float maxDistance, out RayHit hit)
    {
        RequireFinite(origin, nameof(origin));
        hit = default;
        var length = direction.Length();
        if (length <= 0 || maxDistance < 0)
            return false;

        var way = direction / length;
        _rayCandidates.Clear();
        _tree.RayCast(new Ray<double>(origin, way.As<double>()), _rayCandidates, maxDistance);
        _rayCandidates.Sort(CompareEntries);

        var nearest = maxDistance;
        var found = false;
        foreach (var candidate in _rayCandidates)
        {
            if (candidate.Entry > nearest)
                break;

            ref var part = ref _parts[candidate.Item];
            var pose = PlacePart(_bodies[part.Body], part, origin);
            var localRay = new Ray<float>(pose.InverseTransformPoint(Vector3F.Zero), pose.InverseTransformDirection(way));
            if (!part.Shape.CastRay(localRay, nearest, out var distance, out var normal))
                continue;

            nearest = distance;
            found = true;
            hit = new RayHit(GetHandle(part.Body), origin + (way * distance).As<double>(),
                pose.TransformDirection(normal), distance);
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

        RequireFinite(pose.Position, nameof(pose));
        var origin = pose.Position;
        var placedPose = new LocalPose(Vector3F.Zero, NormalizeRotation(pose.Rotation, nameof(pose)));
        _overlapped.Clear();
        _overlappedBodies.Clear();
        if (shape is CompoundCollisionShape compound)
        {
            var parts = compound.Parts;
            for (var index = 0; index < parts.Count; index++)
            {
                OverlapPart(parts[index].Shape, placedPose.Transform(parts[index].LocalPose), origin);
            }
        }
        else
        {
            OverlapPart(shape, placedPose, origin);
        }

        foreach (var body in _overlapped)
        {
            into.Add(GetHandle(body));
        }
    }

    private void OverlapPart(CollisionShape shape, in LocalPose pose, Vector3<double> origin)
    {
        _found.Clear();
        _tree.Query(Place(shape.ComputeBounds(pose), origin), _found);
        var placed = new PlacedShape(shape, pose);
        foreach (var index in _found)
        {
            ref var part = ref _parts[index];
            if (_overlappedBodies.Contains(part.Body))
                continue;

            var partPose = PlacePart(_bodies[part.Body], part, origin);
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
    private bool MeetsSurface(CollisionShape shape, in LocalPose pose, SurfaceCollisionShape surface,
        in LocalPose surfacePose)
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

    /// <inheritdoc/>
    public bool CastShape(CollisionShape shape, in Pose pose, Vector3F translation, out ShapeHit hit)
    {
        ArgumentNullException.ThrowIfNull(shape);
        if (SurfaceCollisionShape.HoldsSurface(shape))
            throw new ArgumentException("A mesh or a height field holds no volume to cast.", nameof(shape));

        if (!float.IsFinite(translation.X) || !float.IsFinite(translation.Y) || !float.IsFinite(translation.Z))
            throw new ArgumentOutOfRangeException(nameof(translation), translation, "A translation must be finite.");

        RequireFinite(pose.Position, nameof(pose));
        var placedPose = new LocalPose(Vector3F.Zero, NormalizeRotation(pose.Rotation, nameof(pose)));
        var cast = new ShapeCast { Origin = pose.Position, Translation = translation, Nearest = 1 };
        if (shape is CompoundCollisionShape compound)
        {
            var parts = compound.Parts;
            for (var index = 0; index < parts.Count; index++)
            {
                CastPart(parts[index].Shape, placedPose.Transform(parts[index].LocalPose), ref cast);
            }
        }
        else
        {
            CastPart(shape, placedPose, ref cast);
        }

        hit = cast.Hit;
        return cast.Found;
    }

    // Each body part in the box the shape sweeps up to the nearest meeting yet.
    private void CastPart(CollisionShape shape, in LocalPose pose, ref ShapeCast cast)
    {
        var end = new LocalPose(pose.Position + cast.Translation * cast.Nearest, pose.Rotation);
        _found.Clear();
        _tree.Query(Place(SweptBounds(shape, pose, end), cast.Origin), _found);
        foreach (var index in _found)
        {
            ref var part = ref _parts[index];
            var partPose = PlacePart(_bodies[part.Body], part, cast.Origin);
            if (part.Shape is SurfaceCollisionShape surface)
            {
                CastAtSurface(shape, pose, surface, partPose, part.Body, ref cast);
                continue;
            }

            if (ShapeSweep.TryCast(shape, pose, cast.Translation, part.Shape, partPose, cast.Nearest, _castBuffer,
                    out var meeting))
                Keep(ref cast, part.Body, meeting);
        }
    }

    // The triangles up to where the shape's foremost point runs into the surface, swept nearest first by a lower bound
    // of where each could be met, until one could only be met past the nearest meeting yet.
    private void CastAtSurface(CollisionShape shape, in LocalPose pose, SurfaceCollisionShape surface,
        in LocalPose surfacePose, int body, ref ShapeCast cast)
    {
        var start = surfacePose.InverseTransform(pose);
        var placed = new PlacedShape(shape, start);
        var translation = surfacePose.InverseTransformDirection(cast.Translation);
        var length = translation.Length();
        var way = length > 0 ? translation / length : Vector3F.Zero;
        var limit = cast.Nearest;
        var foremost = length > 0 ? new Ray<float>(placed.Support(way), way) : default;
        if (length > 0 && surface.CastRay(foremost, limit * length, out var ahead, out _))
            limit = MathF.Min(limit, (ahead + ShapeSweep.MeetingReach) / length);

        var end = new LocalPose(start.Position + translation * limit, start.Rotation);
        _triangles.Clear();
        surface.FindTriangles(SweptBounds(shape, start, end), _triangles);
        _castTriangles.Clear();
        var entered = -1;
        var enteredAt = limit * length;
        foreach (var triangle in _triangles)
        {
            if (!surface.TryGetTriangle(triangle, out var first, out var second, out var third))
                continue;

            var triangleNormal = surface.GetTriangleNormal(triangle, first, second, third);
            var reach = ReachTriangle(placed, translation, way, first, second, third, triangleNormal, limit);
            if (reach > limit)
                continue;

            _triangle.Set(first, second, third, triangleNormal);
            if (length > 0 && _triangle.CastRay(foremost, enteredAt, out var along, out _))
            {
                entered = _castTriangles.Count;
                enteredAt = along;
            }

            _castTriangles.Add(new CastTriangle
            {
                First = first, Second = second, Third = third, Normal = triangleNormal, Reach = reach
            });
        }

        // The one the foremost point runs into goes first, so the rest are mostly passed by.
        if (entered >= 0)
            _castTriangles[entered] = _castTriangles[entered] with { Reach = float.NegativeInfinity };

        // Within a hair of this surface's own nearest meeting - all of a flat field - a triangle changes nothing.
        var tolerance = length > 0 ? ShapeSweep.MeetTolerance / length : 0;
        _castTriangles.Sort(CompareReaches);
        foreach (var candidate in _castTriangles)
        {
            if (cast.Found && candidate.Reach >= cast.Nearest - (cast.Body == body ? tolerance : 0))
                break;

            _triangle.Set(candidate.First, candidate.Second, candidate.Third, candidate.Normal);
            if (ShapeSweep.TryCast(shape, pose, cast.Translation, _triangle, surfacePose,
                    MathF.Min(limit, cast.Nearest), _castBuffer, out var meeting))
                Keep(ref cast, body, meeting);
        }
    }

    private static int CompareReaches(CastTriangle first, CastTriangle second) => first.Reach.CompareTo(second.Reach);

    // A lower bound of where the shape could meet the triangle, by separating axes with motion; cheapest axes first.
    private static float ReachTriangle(in PlacedShape placed, Vector3F translation, Vector3F way, Vector3F first,
        Vector3F second, Vector3F third, Vector3F normal, float limit)
    {
        var reach = way != Vector3F.Zero ? ReachAlong(placed, translation, -way, first, second, third) : 0;
        if (reach > limit)
            return reach;

        reach = MathF.Max(reach, MathF.Max(ReachAlong(placed, translation, normal, first, second, third),
            ReachAlong(placed, translation, -normal, first, second, third)));
        if (reach > limit)
            return reach;

        reach = MathF.Max(reach, ReachAcrossEdge(placed, translation, normal, first, second, third));
        reach = MathF.Max(reach, ReachAcrossEdge(placed, translation, normal, second, third, first));
        return MathF.Max(reach, ReachAcrossEdge(placed, translation, normal, third, first, second));
    }

    // The box the shape fills moving from start to end, out by the gap it stops at.
    private static BoundingBox<float> SweptBounds(CollisionShape shape, in LocalPose start, in LocalPose end)
    {
        var bounds = BoundingBox.CreateMerged(shape.ComputeBounds(start), shape.ComputeBounds(end));
        var margin = new Vector3F(ShapeSweep.StoppingGap);
        return new BoundingBox<float>(bounds.Minimum - margin, bounds.Maximum + margin);
    }

    // Along the in-plane axis square to the edge from start to end, pointing away from the corner across.
    private static float ReachAcrossEdge(in PlacedShape placed, Vector3F translation, Vector3F normal, Vector3F start,
        Vector3F end, Vector3F across)
    {
        var axis = Vector3F.Cross(end - start, normal);
        var length = axis.Length();
        if (!(length > 0))
            return 0;

        axis /= length;
        return ReachAlong(placed, translation, Vector3F.Dot(axis, across - start) > 0 ? -axis : axis, start, end, across);
    }

    // The gap past the meeting one along the axis over the closing speed; endless when the whole way cannot close it.
    private static float ReachAlong(in PlacedShape placed, Vector3F translation, Vector3F axis, Vector3F first,
        Vector3F second, Vector3F third)
    {
        var triangleReach = MathF.Max(Vector3F.Dot(axis, first), MathF.Max(Vector3F.Dot(axis, second), Vector3F.Dot(axis, third)));
        var apart = Vector3F.Dot(axis, placed.Support(-axis)) - triangleReach - ShapeSweep.MeetingReach;
        var closing = MathF.Max(-Vector3F.Dot(translation, axis), 0);
        if (apart > closing)
            return float.PositiveInfinity;

        return closing > 0 ? MathF.Max(apart, 0) / closing : 0;
    }

    // The nearest meeting becomes the hit, at its stop; one met no sooner than the hit's leaves it.
    private void Keep(ref ShapeCast cast, int body, in ShapeMeeting meeting)
    {
        if (cast.Found && meeting.Meet >= cast.Nearest)
            return;

        cast.Found = true;
        cast.Nearest = meeting.Meet;
        cast.Body = body;
        cast.Hit = new ShapeHit(GetHandle(body), cast.Origin + meeting.Point.As<double>(), meeting.Normal,
            meeting.Stop * cast.Translation.Length());
    }

    private static int CompareEntries((int Item, double Entry) first, (int Item, double Entry) second) =>
        first.Entry.CompareTo(second.Entry);
}
