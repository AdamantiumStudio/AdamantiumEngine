using System;
using System.Collections.Generic;
using Adamantium.DarkMatter.CollisionDetection;
using Adamantium.DarkMatter.Dynamics;
using Adamantium.DarkMatter.Shapes;

namespace Adamantium.DarkMatter;

// Queries are worked out in float in the frame of where they start, as collisions are in a pair's: the bodies they meet
// stand near that point wherever it is in the world.
internal sealed partial class PhysicsWorld
{
    private readonly List<(int Item, double Entry)> _rayCandidates = [];
    private readonly List<int> _overlapped = [];
    private readonly HashSet<int> _overlappedBodies = [];
    private readonly ManifoldBuffer _castBuffer = new();
    private readonly List<CastTriangle> _castTriangles = [];

    /// <inheritdoc/>
    public bool RayCast(WorldPosition origin, Vector3F direction, float maxDistance, out WorldRayHit hit,
        BodyHandle except = default, ulong layers = ulong.MaxValue, bool meetsTriggers = false)
    {
        hit = default;
        if (!TryStartRay(origin, direction, maxDistance, except, out var way, out var skipped))
            return false;

        var filter = new PartFilter(layers, meetsTriggers);
        var nearest = maxDistance;
        var found = false;
        foreach (var candidate in _rayCandidates)
        {
            if (candidate.Entry > nearest)
                break;

            if (!TryMeetPart(candidate.Item, skipped, filter, origin, way, nearest, out var met))
                continue;

            nearest = met.Distance;
            found = true;
            hit = met;
        }

        if (found)
            hit = WithPartIndex(hit);

        return found;
    }

    /// <inheritdoc/>
    public int RayCast(WorldPosition origin, Vector3F direction, float maxDistance, Span<WorldRayHit> hits,
        BodyHandle except = default, ulong layers = ulong.MaxValue, bool meetsTriggers = false)
    {
        if (hits.IsEmpty || !TryStartRay(origin, direction, maxDistance, except, out var way, out var skipped))
            return 0;

        var filter = new PartFilter(layers, meetsTriggers);
        var count = 0;
        var farthest = maxDistance;
        foreach (var candidate in _rayCandidates)
        {
            // Full, a box entered past the farthest hit kept holds no nearer one.
            if (count == hits.Length && candidate.Entry > farthest)
                break;

            if (!TryMeetPart(candidate.Item, skipped, filter, origin, way, maxDistance, out var met))
                continue;

            count = KeepNearestPerBody(hits, count, met);
            if (count == hits.Length)
                farthest = hits[count - 1].Distance;
        }

        for (var index = 0; index < count; index++)
        {
            hits[index] = WithPartIndex(hits[index]);
        }

        return count;
    }

    // The ray made of length one and its candidates found, nearest box first; false for no ray to cast.
    private bool TryStartRay(WorldPosition origin, Vector3F direction, float maxDistance, BodyHandle except,
        out Vector3F way, out int skipped)
    {
        RequireFinite(origin, nameof(origin));
        skipped = IndexOfExcept(except);
        way = default;
        var length = direction.Length();
        if (length <= 0 || maxDistance < 0)
            return false;

        way = direction / length;
        _rayCandidates.Clear();
        _tree.RayCast(new Ray<double>(origin.ToVector(), way.As<double>()), _rayCandidates, maxDistance);
        _rayCandidates.Sort(CompareEntries);
        return true;
    }

    // Where the ray meets the part within reach, unless its body is the one passed by or the filter passes it by; the
    // hit tells the part by its slot, made its index once kept.
    private bool TryMeetPart(int slot, int skipped, in PartFilter filter, WorldPosition origin, Vector3F way,
        float reach, out WorldRayHit hit)
    {
        hit = default;
        ref var part = ref _parts[slot];
        if (part.Body == skipped || !filter.Meets(part))
            return false;

        var pose = PlacePart(_bodies[part.Body], part, origin);
        var localRay = new Ray<float>(pose.InverseTransformPoint(Vector3F.Zero), pose.InverseTransformDirection(way));
        if (!part.Shape.CastRay(localRay, reach, out var distance, out var normal))
            return false;

        hit = new WorldRayHit(GetHandle(part.Body), slot, origin + way * distance, pose.TransformDirection(normal),
            distance);
        return true;
    }

    private WorldRayHit WithPartIndex(in WorldRayHit hit) =>
        new(hit.Body, _parts[hit.Part].Index, hit.Position, hit.Normal, hit.Distance);

    // The hits kept nearest first, one for each body - its nearest part - as many as fit; gives how many there are.
    private static int KeepNearestPerBody(Span<WorldRayHit> hits, int count, in WorldRayHit hit)
    {
        for (var index = 0; index < count; index++)
        {
            if (hits[index].Body != hit.Body)
                continue;

            if (hits[index].Distance <= hit.Distance)
                return count;

            hits.Slice(index + 1, count - index - 1).CopyTo(hits[index..]);
            count--;
            break;
        }

        var at = count;
        while (at > 0 && hits[at - 1].Distance > hit.Distance)
        {
            at--;
        }

        if (at == hits.Length)
            return count;

        var kept = Math.Min(count, hits.Length - 1);
        hits.Slice(at, kept - at).CopyTo(hits[(at + 1)..]);
        hits[at] = hit;
        return kept + 1;
    }

    // The world's own index of the body a query passes by; none for a default handle.
    private int IndexOfExcept(BodyHandle except) => except.IsValid ? Resolve(except) : NoBody;

    /// <inheritdoc/>
    public void Overlap(CollisionShape shape, in Pose pose, ICollection<BodyHandle> into, BodyHandle except = default,
        ulong layers = ulong.MaxValue, bool meetsTriggers = false)
    {
        ArgumentNullException.ThrowIfNull(shape);
        ArgumentNullException.ThrowIfNull(into);
        var filter = new PartFilter(layers, meetsTriggers);
        if (SurfaceCollisionShape.HoldsSurface(shape))
            throw new ArgumentException("A mesh or a height field holds no volume to look for overlaps with.",
                nameof(shape));

        RequireFinite(pose.Position, nameof(pose));
        var origin = pose.Position;
        var placedPose = new LocalPose(Vector3F.Zero, NormalizeRotation(pose.Rotation, nameof(pose)));
        _overlapped.Clear();
        _overlappedBodies.Clear();
        // Passed by as one already found.
        var skipped = IndexOfExcept(except);
        if (skipped != NoBody)
            _overlappedBodies.Add(skipped);
        if (shape is CompoundCollisionShape compound)
        {
            var parts = compound.Parts;
            for (var index = 0; index < parts.Count; index++)
            {
                OverlapPart(parts[index].Shape, placedPose.Transform(parts[index].Pose), origin, filter);
            }
        }
        else
        {
            OverlapPart(shape, placedPose, origin, filter);
        }

        foreach (var body in _overlapped)
        {
            into.Add(GetHandle(body));
        }
    }

    private void OverlapPart(CollisionShape shape, in LocalPose pose, WorldPosition origin, in PartFilter filter)
    {
        _found.Clear();
        _tree.Query(Place(shape.ComputeBounds(pose), origin), _found);
        var placed = new PlacedShape(shape, pose);
        foreach (var index in _found)
        {
            ref var part = ref _parts[index];
            if (!filter.Meets(part) || _overlappedBodies.Contains(part.Body))
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
    public bool CastShape(CollisionShape shape, in Pose pose, Vector3F translation, out WorldShapeHit hit,
        BodyHandle except = default, ulong layers = ulong.MaxValue, bool meetsTriggers = false)
    {
        ArgumentNullException.ThrowIfNull(shape);
        if (SurfaceCollisionShape.HoldsSurface(shape))
            throw new ArgumentException("A mesh or a height field holds no volume to cast.", nameof(shape));

        if (!float.IsFinite(translation.X) || !float.IsFinite(translation.Y) || !float.IsFinite(translation.Z))
            throw new ArgumentOutOfRangeException(nameof(translation), translation, "A translation must be finite.");

        RequireFinite(pose.Position, nameof(pose));
        var placedPose = new LocalPose(Vector3F.Zero, NormalizeRotation(pose.Rotation, nameof(pose)));
        var cast = new ShapeCast
        {
            Origin = pose.Position, Translation = translation, Nearest = 1, Except = IndexOfExcept(except),
            Parts = new PartFilter(layers, meetsTriggers)
        };
        if (shape is CompoundCollisionShape compound)
        {
            var parts = compound.Parts;
            for (var index = 0; index < parts.Count; index++)
            {
                CastPart(parts[index].Shape, placedPose.Transform(parts[index].Pose), ref cast);
            }
        }
        else
        {
            CastPart(shape, placedPose, ref cast);
        }

        hit = cast.Found ? WithPartIndex(cast.Hit) : default;
        return cast.Found;
    }

    private WorldShapeHit WithPartIndex(in WorldShapeHit hit) =>
        new(hit.Body, _parts[hit.Part].Index, hit.Position, hit.Normal, hit.Distance);

    /// <summary>The surface of the material of <paramref name="body"/>'s part at <paramref name="part"/> among its
    /// parts: its own, or the body's; null for none, or for a part the body does not have.</summary>
    internal string SurfaceOf(BodyHandle body, int part)
    {
        for (var slot = _bodies[Resolve(body)].FirstPart; slot != NoPart; slot = _parts[slot].Next)
        {
            if (_parts[slot].Index == part)
                return _parts[slot].Material.Surface;
        }

        return null;
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
            if (part.Body == cast.Except || !cast.Parts.Meets(part))
                continue;

            var partPose = PlacePart(_bodies[part.Body], part, cast.Origin);
            if (part.Shape is SurfaceCollisionShape surface)
            {
                CastAtSurface(shape, pose, surface, partPose, index, ref cast);
                continue;
            }

            if (ShapeSweep.TryCast(shape, pose, cast.Translation, part.Shape, partPose, cast.Nearest, _castBuffer,
                    out var meeting))
                Keep(ref cast, index, meeting);
        }
    }

    // The triangles up to where the shape's foremost point runs into the surface, swept nearest first by a lower bound
    // of where each could be met, until one could only be met past the nearest meeting yet.
    private void CastAtSurface(CollisionShape shape, in LocalPose pose, SurfaceCollisionShape surface,
        in LocalPose surfacePose, int slot, ref ShapeCast cast)
    {
        var body = _parts[slot].Body;
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
                Keep(ref cast, slot, meeting);
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
    private void Keep(ref ShapeCast cast, int slot, in ShapeMeeting meeting)
    {
        if (cast.Found && meeting.Meet >= cast.Nearest)
            return;

        var body = _parts[slot].Body;
        cast.Found = true;
        cast.Nearest = meeting.Meet;
        cast.Body = body;
        cast.Hit = new WorldShapeHit(GetHandle(body), slot, cast.Origin + meeting.Point, meeting.Normal,
            meeting.Stop * cast.Translation.Length());
    }

    private static int CompareEntries((int Item, double Entry) first, (int Item, double Entry) second) =>
        first.Entry.CompareTo(second.Entry);
}
