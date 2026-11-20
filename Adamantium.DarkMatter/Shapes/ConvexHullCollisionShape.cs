using System;
using System.Collections.Generic;
using Adamantium.DarkMatter.CollisionDetection;
using Adamantium.DarkMatter.Dynamics;

namespace Adamantium.DarkMatter.Shapes;

/// <summary>The smallest convex volume holding a set of points: a rock, a cab. At most <see cref="MaxCorners"/>
/// corners, its edges rounded by <see cref="ConvexRadius"/> within it.</summary>
public sealed class ConvexHullCollisionShape : CollisionShape
{
    /// <summary>The most corners a hull keeps: each contact and query walks them.</summary>
    public const int MaxCorners = 256;

    // The rounding takes at most this share of the distance from the hull's middle to its nearest face.
    private const float MaxShareOfInnerRadius = 0.25f;

    // Faces contacts meet whose corners lie within this share of the hull's size of one plane are one: a body lying on
    // two faces nearly in one plane bears on both, not on whichever is turned a hair nearer.
    private const double ContactFlatness = 3e-4;

    // The hull as given, weighed and met by rays; and its core, the hull drawn in by the radius, that contacts meet.
    private readonly HullFaces _hull;
    private readonly HullFaces _core;
    private readonly Vector3F _boundsCenter;
    private readonly Vector3F _boundsHalf;
    private readonly float _boundingRadius;
    private readonly float _convexRadius;

    /// <summary>The hull of <paramref name="points"/>, not all in one plane, its edges rounded by
    /// <paramref name="convexRadius"/>.</summary>
    public ConvexHullCollisionShape(IReadOnlyList<Vector3F> points, float convexRadius = DefaultConvexRadius)
        : this(HullOf(points), convexRadius, nameof(points))
    {
    }

    /// <summary>The shape of a hull built already; its corners are not hulled anew unless more than
    /// <see cref="MaxCorners"/>.</summary>
    public ConvexHullCollisionShape(ConvexHull hull, float convexRadius = DefaultConvexRadius)
        : this(RequireSolid(hull), convexRadius, nameof(hull))
    {
    }

    private ConvexHullCollisionShape(ConvexHull hull, float convexRadius, string parameterName)
    {
        if (!float.IsFinite(convexRadius) || convexRadius < 0)
            throw new ArgumentOutOfRangeException(nameof(convexRadius), convexRadius,
                "A convex radius must be finite and not below zero.");

        var minimum = new Vector3F(float.MaxValue);
        var maximum = new Vector3F(float.MinValue);
        foreach (var vertex in hull.Vertices)
        {
            minimum = Vector3F.Min(minimum, vertex);
            maximum = Vector3F.Max(maximum, vertex);
            _boundingRadius = MathF.Max(_boundingRadius, vertex.Length());
        }

        _boundsCenter = (minimum + maximum) * 0.5f;
        _boundsHalf = (maximum - minimum) * 0.5f;
        if (Vector3F.Min(_boundsHalf) < MinimumSize / 2)
            throw new ArgumentException("A hull must be at least a micrometer across.", parameterName);

        RequireFiniteReach(parameterName);
        var size = 2 * Vector3F.Max(_boundsHalf);
        _hull = HullFaces.Build(hull, size);
        if (convexRadius > 0 && TryDrawIn(_hull, convexRadius, size, out var core, out var radius))
        {
            _core = core;
            _convexRadius = radius;
        }
        else
        {
            _core = HullFaces.Build(hull, size, ContactFlatness);
        }
    }

    /// <summary>The corners of the hull.</summary>
    public IReadOnlyList<Vector3F> Vertices => _hull.Vertices;

    /// <summary>How round the edges are, in meters: as asked, less on a small or uneven hull, none where a corner is
    /// too sharp to draw in.</summary>
    public float ConvexRadius => _convexRadius;

    /// <summary>How many flat faces the hull has.</summary>
    public int FaceCount => _hull.Count;

    /// <inheritdoc/>
    public override CollisionShapeKind Kind => CollisionShapeKind.ConvexHull;

    /// <inheritdoc/>
    public override float BoundingRadius => _boundingRadius;

    /// <summary>The mass of the hull as given, its rounding not taken off: a few percent more on a small hull.</summary>
    public override MassProperties ComputeMass(float density)
    {
        // Tetrahedra from a point inside to each face's triangles: their volumes, middles and second moments
        // (Blow and Binstock, How to find the inertia tensor).
        var vertices = _hull.Vertices;
        var origin = Vector3<double>.Zero;
        foreach (var vertex in vertices)
        {
            origin += vertex.As<double>();
        }

        origin /= vertices.Length;
        var volume = 0.0;
        var moment = Vector3<double>.Zero;
        var covariance = default(Matrix3x3<double>);
        for (var face = 0; face < _hull.Count; face++)
        {
            var first = vertices[_hull.Corners[_hull.Starts[face]]].As<double>() - origin;
            for (var corner = _hull.Starts[face] + 1; corner < _hull.Starts[face + 1] - 1; corner++)
            {
                var second = vertices[_hull.Corners[corner]].As<double>() - origin;
                var third = vertices[_hull.Corners[corner + 1]].As<double>() - origin;
                var determinant = Vector3<double>.Dot(first, Vector3<double>.Cross(second, third));
                volume += determinant / 6;
                var sum = first + second + third;
                moment += sum * (determinant / 24);
                covariance += (Outer(first) + Outer(second) + Outer(third) + Outer(sum)) * (determinant / 120);
            }
        }

        var mass = density * volume;
        if (!(volume > 0))
            return new MassProperties(0, Vector3F.Zero);

        var center = moment / volume;
        var centered = covariance * density - Outer(center) * mass;
        var trace = centered.M11 + centered.M22 + centered.M33;
        var tensor = Matrix3x3<double>.Identity * trace - centered;
        InertiaMath.Diagonalize(tensor, out var moments, out var axes);
        return new MassProperties((float)mass, moments, (origin + center).As<float>(), axes);
    }

    /// <inheritdoc/>
    internal override BoundingBox<float> ComputeBounds(in LocalPose pose)
    {
        var axisX = Vector3F.Abs(pose.TransformDirection(Vector3F.UnitX));
        var axisY = Vector3F.Abs(pose.TransformDirection(Vector3F.UnitY));
        var axisZ = Vector3F.Abs(pose.TransformDirection(Vector3F.UnitZ));
        var reach = axisX * _boundsHalf.X + axisY * _boundsHalf.Y + axisZ * _boundsHalf.Z;
        var center = pose.TransformPoint(_boundsCenter);
        return new BoundingBox<float>(center - reach, center + reach);
    }

    /// <summary>The core's farthest point, out by the convex radius.</summary>
    public override Vector3F GetSupport(Vector3F direction)
    {
        var core = _core.GetSupport(direction);
        var length = direction.Length();
        return _convexRadius > 0 && length > 0 ? core + direction * (_convexRadius / length) : core;
    }

    internal override float CoreRadius => _convexRadius;

    internal override Vector3F GetCoreSupport(Vector3F direction) => _core.GetSupport(direction);

    internal override int FlatFaceCount => _core.Count;

    internal override Vector3F GetFlatFaceNormal(int face) => _core.Normals[face];

    internal override float GetFlatFaceOffset(int face) => _core.Offsets[face];

    internal override int GetFlatFace(int face, Span<Vector3F> into) => _core.GetFace(face, into);

    internal override int FindFlatFace(Vector3F direction) => _core.FindFace(direction);

    internal override int GetSupportingFace(Vector3F direction, Span<Vector3F> into) =>
        _core.GetFace(_core.FindSupportingFace(direction), into);

    /// <summary>Where <paramref name="ray"/> first meets the hull's faces, the rounding not met; from inside, at once.
    /// </summary>
    public override bool CastRay(in Ray<float> ray, float maxDistance, out float distance, out Vector3F normal)
    {
        var enter = 0f;
        var leave = maxDistance;
        normal = -ray.Direction;
        var inside = true;
        for (var face = 0; face < _hull.Count; face++)
        {
            var facing = _hull.Normals[face];
            var height = Vector3F.Dot(facing, ray.Position) - _hull.Offsets[face];
            var closing = Vector3F.Dot(facing, ray.Direction);
            if (height > 0)
                inside = false;

            if (closing == 0)
            {
                if (height > 0)
                {
                    distance = 0;
                    return false;
                }

                continue;
            }

            // On or outside this face and heading away from it: the ray never comes in.
            if (height >= 0 && closing > 0)
            {
                distance = 0;
                return false;
            }

            var crossing = -height / closing;
            if (closing < 0)
            {
                if (crossing > enter)
                {
                    enter = crossing;
                    normal = facing;
                }
            }
            else if (crossing < leave)
            {
                leave = crossing;
            }

            if (enter > leave)
            {
                distance = 0;
                return false;
            }
        }

        if (inside)
        {
            distance = 0;
            normal = -ray.Direction;
            return true;
        }

        distance = enter;
        return true;
    }

    private static Matrix3x3<double> Outer(Vector3<double> vector) =>
        new(vector * vector.X, vector * vector.Y, vector * vector.Z);

    private static ConvexHull HullOf(IReadOnlyList<Vector3F> points)
    {
        ArgumentNullException.ThrowIfNull(points);
        RequireFinite(points, nameof(points));
        return RequireSolid(points.Count >= 4 ? ConvexHull.FromPoints(points) : null, nameof(points));
    }

    private static ConvexHull RequireSolid(ConvexHull hull)
    {
        ArgumentNullException.ThrowIfNull(hull);
        RequireFinite(hull.Vertices, nameof(hull));
        return RequireSolid(hull, nameof(hull));
    }

    // The hull, of at most MaxCorners corners; throws unless it holds a volume.
    private static ConvexHull RequireSolid(ConvexHull hull, string parameterName)
    {
        if (hull is { IsFlat: false, Vertices.Count: > MaxCorners })
            hull = ConvexHull.FromPoints(KeepFarthest(hull));

        if (hull == null || hull.IsFlat || hull.Triangles.Count < 12)
            throw new ArgumentException("A hull needs points that hold a volume, not all in one plane.", parameterName);

        return hull;
    }

    private static void RequireFinite(IReadOnlyList<Vector3F> points, string parameterName)
    {
        foreach (var point in points)
        {
            if (!float.IsFinite(point.X) || !float.IsFinite(point.Y) || !float.IsFinite(point.Z))
                throw new ArgumentException("A hull's points must be finite.", parameterName);
        }
    }

    // The core: every face moved in by the radius, so the rounded hull keeps the hull's faces where they are. The
    // radius is cut so no corner is rounded off by more than asked.
    private static bool TryDrawIn(HullFaces hull, float requested, float size, out HullFaces core, out float fitted)
    {
        core = null;
        fitted = 0;
        var middle = Vector3<double>.Zero;
        foreach (var vertex in hull.Vertices)
        {
            middle += vertex.As<double>();
        }

        middle /= hull.Vertices.Length;
        var radius = (double)requested;
        for (var face = 0; face < hull.Count; face++)
        {
            var inner = hull.Offsets[face] - Vector3<double>.Dot(hull.Normals[face].As<double>(), middle);
            radius = Math.Min(radius, MaxShareOfInnerRadius * inner);
        }

        // A sharp corner is cut deeper than the radius: the radius shrinks till the first core cutting none deeper.
        ConvexHull kept = null;
        for (var attempt = 0; attempt < 3 && radius > 0; attempt++)
        {
            var drawnIn = MoveFacesIn(hull, middle, radius);
            if (drawnIn == null)
                break;

            // Corners split where faces moved apart: kept to MaxCorners, as many as each query may walk.
            if (drawnIn.Vertices.Count > MaxCorners)
                drawnIn = ConvexHull.FromPoints(KeepFarthest(drawnIn));

            var deepestCut = 0.0;
            foreach (var vertex in hull.Vertices)
            {
                var outward = vertex.As<double>() - middle;
                var reach = outward.Length();
                var way = outward / reach;
                var coreReach = Vector3<double>.Dot(drawnIn.Support(way.As<float>()).As<double>() - middle, way);
                deepestCut = Math.Max(deepestCut, reach - coreReach - radius);
            }

            if (deepestCut <= requested * 1.001)
            {
                kept = drawnIn;
                fitted = (float)radius;
                break;
            }

            radius *= requested / deepestCut;
        }

        if (kept == null)
            return false;

        core = HullFaces.Build(kept, size, ContactFlatness);
        return true;
    }

    // The faces moved in by the radius met as half-spaces round the middle: each face's dual point, its normal over its
    // distance, and each face of their hull a corner where its three planes meet. Null when nothing is left inside.
    private static ConvexHull MoveFacesIn(HullFaces hull, Vector3<double> middle, double radius)
    {
        var duals = new Vector3F[hull.Count];
        for (var face = 0; face < hull.Count; face++)
        {
            var normal = hull.Normals[face].As<double>();
            var distance = hull.Offsets[face] - radius - Vector3<double>.Dot(normal, middle);
            if (!(distance > 0))
                return null;

            duals[face] = (normal / distance).As<float>();
        }

        var dualHull = ConvexHull.FromPoints(duals);
        if (dualHull.IsFlat || dualHull.Triangles.Count < 12)
            return null;

        var corners = new List<Vector3F>(dualHull.Triangles.Count / 3);
        for (var triangle = 0; triangle < dualHull.Triangles.Count; triangle += 3)
        {
            var first = dualHull.Vertices[dualHull.Triangles[triangle]].As<double>();
            var second = dualHull.Vertices[dualHull.Triangles[triangle + 1]].As<double>();
            var third = dualHull.Vertices[dualHull.Triangles[triangle + 2]].As<double>();
            var plane = Vector3<double>.Cross(second - first, third - first);
            var along = Vector3<double>.Dot(plane, first);
            if (along > 1e-12 * plane.Length())
                corners.Add((middle + plane / along).As<float>());
        }

        var drawnIn = corners.Count >= 4 ? ConvexHull.FromPoints(corners) : null;
        return drawnIn is { IsFlat: false, Triangles.Count: >= 12 } ? drawnIn : null;
    }

    // The corners farthest along the six axes, then along ever finer spreads of directions over the sphere, up to
    // MaxCorners: the kept ones reach every way the hull does.
    private static List<Vector3F> KeepFarthest(ConvexHull hull)
    {
        var kept = new List<Vector3F>(MaxCorners);
        var keptSet = new HashSet<Vector3F>();
        for (var axis = 0; axis < 3; axis++)
        {
            foreach (var sign in AxisMath.Signs)
            {
                var direction = axis switch { 0 => Vector3F.UnitX, 1 => Vector3F.UnitY, _ => Vector3F.UnitZ } * sign;
                var corner = hull.Support(direction);
                if (keptSet.Add(corner))
                    kept.Add(corner);
            }
        }

        var met = new List<Vector3F>();
        var metSet = new HashSet<Vector3F>();
        var golden = MathF.PI * (3 - MathF.Sqrt(5));
        for (var directions = MaxCorners; directions <= 16 * MaxCorners && kept.Count < MaxCorners; directions *= 2)
        {
            met.Clear();
            metSet.Clear();
            for (var index = 0; index < directions; index++)
            {
                var height = 1 - 2 * (index + 0.5f) / directions;
                var across = MathF.Sqrt(1 - height * height);
                var angle = golden * index;
                var corner = hull.Support(new Vector3F(across * MathF.Cos(angle), height, across * MathF.Sin(angle)));
                if (!keptSet.Contains(corner) && metSet.Add(corner))
                    met.Add(corner);
            }

            // The whole spread fits: kept, and a finer one tried; it does not: every so many of it fill what is left.
            if (kept.Count + met.Count <= MaxCorners)
            {
                foreach (var corner in met)
                {
                    keptSet.Add(corner);
                    kept.Add(corner);
                }

                continue;
            }

            var room = MaxCorners - kept.Count;
            for (var index = 0; index < room; index++)
            {
                kept.Add(met[(int)((long)index * met.Count / room)]);
            }
        }

        return kept;
    }
}
