using System.Numerics;

namespace Adamantium.Mathematics.Typed;

/// <summary>
/// Closest points, distances, intersections and containment between rays, planes, triangles, boxes and spheres. A
/// ray's distances are in units of its direction; a hit behind the ray's start is no hit, and a ray that starts inside a
/// volume hits it at distance zero. Lines are taken as parallel, and triangles as flat, when the sine of their angle is
/// below about 1e-6 - the same for every number type, as it is a matter of geometry, not of precision.
/// </summary>
public static class Collision
{
    /// <summary>The distance between <paramref name="ray"/> and the segment from <paramref name="lineStart"/> to
    /// <paramref name="lineEnd"/> where they come closest, and the segment's point there.</summary>
    public static T RayIntersectsLineSegment<T>(Ray<T> ray, Vector3<T> lineStart, Vector3<T> lineEnd, out Vector3<T> point)
        where T : unmanaged, IFloatingPointIeee754<T>
    {
        var u = ray.Direction;
        var v = lineEnd - lineStart;
        var w = ray.Position - lineStart;
        var a = Vector3<T>.Dot(u, u);
        var b = Vector3<T>.Dot(u, v);
        var c = Vector3<T>.Dot(v, v);
        var d = Vector3<T>.Dot(u, w);
        var e = Vector3<T>.Dot(v, w);
        T s;
        T t;
        if (c <= T.Epsilon)
        {
            t = T.Zero;
            s = T.Max(-d / a, T.Zero);
        }
        else
        {
            var denominator = a * c - b * b;
            s = denominator > SegmentsParallel<T>() * a * c ? T.Max((b * e - c * d) / denominator, T.Zero) : T.Zero;
            t = (b * s + e) / c;
            if (t < T.Zero)
            {
                t = T.Zero;
                s = T.Max(-d / a, T.Zero);
            }
            else if (t > T.One)
            {
                t = T.One;
                s = T.Max((b - d) / a, T.Zero);
            }
        }

        point = lineStart + v * t;
        return (ray.Position + u * s - point).Length();
    }

    /// <summary>The point of the triangle <paramref name="vertex1"/>, <paramref name="vertex2"/>,
    /// <paramref name="vertex3"/> nearest <paramref name="point"/>.</summary>
    public static Vector3<T> ClosestPointPointTriangle<T>(Vector3<T> point, Vector3<T> vertex1, Vector3<T> vertex2, Vector3<T> vertex3)
        where T : unmanaged, IFloatingPointIeee754<T>
    {
        var ab = vertex2 - vertex1;
        var ac = vertex3 - vertex1;
        var ap = point - vertex1;
        var d1 = Vector3<T>.Dot(ab, ap);
        var d2 = Vector3<T>.Dot(ac, ap);
        if (d1 <= T.Zero && d2 <= T.Zero)
        {
            return vertex1;
        }

        var bp = point - vertex2;
        var d3 = Vector3<T>.Dot(ab, bp);
        var d4 = Vector3<T>.Dot(ac, bp);
        if (d3 >= T.Zero && d4 <= d3)
        {
            return vertex2;
        }

        var vc = d1 * d4 - d3 * d2;
        if (vc <= T.Zero && d1 >= T.Zero && d3 <= T.Zero)
        {
            return vertex1 + d1 / (d1 - d3) * ab;
        }

        var cp = point - vertex3;
        var d5 = Vector3<T>.Dot(ab, cp);
        var d6 = Vector3<T>.Dot(ac, cp);
        if (d6 >= T.Zero && d5 <= d6)
        {
            return vertex3;
        }

        var vb = d5 * d2 - d1 * d6;
        if (vb <= T.Zero && d2 >= T.Zero && d6 <= T.Zero)
        {
            return vertex1 + d2 / (d2 - d6) * ac;
        }

        var va = d3 * d6 - d5 * d4;
        if (va <= T.Zero && d4 - d3 >= T.Zero && d5 - d6 >= T.Zero)
        {
            return vertex2 + (d4 - d3) / (d4 - d3 + (d5 - d6)) * (vertex3 - vertex2);
        }

        var denominator = T.One / (va + vb + vc);
        return vertex1 + ab * (vb * denominator) + ac * (vc * denominator);
    }

    /// <summary>The point of <paramref name="plane"/> nearest <paramref name="point"/>; the plane's normal is taken to be
    /// of length one.</summary>
    public static Vector3<T> ClosestPointPlanePoint<T>(Plane<T> plane, Vector3<T> point) where T : unmanaged, IFloatingPointIeee754<T> =>
        point - DistancePlanePoint(plane, point) * plane.Normal;

    /// <summary>The point of <paramref name="box"/> nearest <paramref name="point"/>.</summary>
    public static Vector3<T> ClosestPointBoxPoint<T>(BoundingBox<T> box, Vector3<T> point) where T : unmanaged, IFloatingPointIeee754<T> =>
        Vector3<T>.Min(Vector3<T>.Max(point, box.Minimum), box.Maximum);

    /// <summary>The point of <paramref name="sphere"/>'s surface nearest <paramref name="point"/>.</summary>
    public static Vector3<T> ClosestPointSpherePoint<T>(BoundingSphere<T> sphere, Vector3<T> point)
        where T : unmanaged, IFloatingPointIeee754<T> =>
        Vector3<T>.Normalize(point - sphere.Center) * sphere.Radius + sphere.Center;

    /// <summary>The point of <paramref name="sphere1"/>'s surface nearest <paramref name="sphere2"/>'s center.</summary>
    public static Vector3<T> ClosestPointSphereSphere<T>(BoundingSphere<T> sphere1, BoundingSphere<T> sphere2)
        where T : unmanaged, IFloatingPointIeee754<T> =>
        ClosestPointSpherePoint(sphere1, sphere2.Center);

    /// <summary>The point of the segment from <paramref name="start"/> to <paramref name="end"/> nearest
    /// <paramref name="point"/>.</summary>
    public static Vector3<T> ClosestPointSegmentPoint<T>(Vector3<T> start, Vector3<T> end, Vector3<T> point)
        where T : unmanaged, IFloatingPointIeee754<T>
    {
        var segment = end - start;
        var length = Vector3<T>.Dot(segment, segment);
        return length <= T.Epsilon ? start : start + segment * T.Clamp(Vector3<T>.Dot(point - start, segment) / length, T.Zero, T.One);
    }

    /// <summary>How far <paramref name="point"/> lies in front of <paramref name="plane"/>, negative behind it; the
    /// plane's normal is taken to be of length one.</summary>
    public static T DistancePlanePoint<T>(Plane<T> plane, Vector3<T> point) where T : unmanaged, IFloatingPointIeee754<T> =>
        Vector3<T>.Dot(plane.Normal, point) + plane.D;

    /// <summary>The distance from <paramref name="point"/> to <paramref name="box"/>, zero inside it.</summary>
    public static T DistanceBoxPoint<T>(BoundingBox<T> box, Vector3<T> point) where T : unmanaged, IFloatingPointIeee754<T> =>
        Vector3<T>.Distance(point, ClosestPointBoxPoint(box, point));

    /// <summary>The distance between the two boxes, zero where they touch or overlap.</summary>
    public static T DistanceBoxBox<T>(BoundingBox<T> box1, BoundingBox<T> box2) where T : unmanaged, IFloatingPointIeee754<T>
    {
        var gap = Vector3<T>.Max(Vector3<T>.Max(box1.Minimum - box2.Maximum, box2.Minimum - box1.Maximum), Vector3<T>.Zero);
        return gap.Length();
    }

    /// <summary>The distance from <paramref name="point"/> to <paramref name="sphere"/>, zero inside it.</summary>
    public static T DistanceSpherePoint<T>(BoundingSphere<T> sphere, Vector3<T> point) where T : unmanaged, IFloatingPointIeee754<T> =>
        T.Max(Vector3<T>.Distance(sphere.Center, point) - sphere.Radius, T.Zero);

    /// <summary>The distance between the two spheres, zero where they touch or overlap.</summary>
    public static T DistanceSphereSphere<T>(BoundingSphere<T> sphere1, BoundingSphere<T> sphere2)
        where T : unmanaged, IFloatingPointIeee754<T> =>
        T.Max(Vector3<T>.Distance(sphere1.Center, sphere2.Center) - (sphere1.Radius + sphere2.Radius), T.Zero);

    /// <summary>The square of the distance between the segments from <paramref name="start1"/> to
    /// <paramref name="end1"/> and from <paramref name="start2"/> to <paramref name="end2"/> where they come
    /// closest.</summary>
    public static T DistanceSquaredSegmentSegment<T>(Vector3<T> start1, Vector3<T> end1, Vector3<T> start2, Vector3<T> end2)
        where T : unmanaged, IFloatingPointIeee754<T>
    {
        var d1 = end1 - start1;
        var d2 = end2 - start2;
        var r = start1 - start2;
        var a = Vector3<T>.Dot(d1, d1);
        var e = Vector3<T>.Dot(d2, d2);
        var f = Vector3<T>.Dot(d2, r);
        if (a <= T.Epsilon && e <= T.Epsilon)
        {
            return Vector3<T>.Dot(r, r);
        }

        T s;
        T t;
        if (a <= T.Epsilon)
        {
            s = T.Zero;
            t = T.Clamp(f / e, T.Zero, T.One);
        }
        else
        {
            var c = Vector3<T>.Dot(d1, r);
            if (e <= T.Epsilon)
            {
                t = T.Zero;
                s = T.Clamp(-c / a, T.Zero, T.One);
            }
            else
            {
                var b = Vector3<T>.Dot(d1, d2);
                var denominator = a * e - b * b;
                s = denominator > SegmentsParallel<T>() * a * e ? T.Clamp((b * f - c * e) / denominator, T.Zero, T.One) : T.Zero;
                t = (b * s + f) / e;
                if (t < T.Zero)
                {
                    t = T.Zero;
                    s = T.Clamp(-c / a, T.Zero, T.One);
                }
                else if (t > T.One)
                {
                    t = T.One;
                    s = T.Clamp((b - c) / a, T.Zero, T.One);
                }
            }
        }

        var between = start1 + d1 * s - (start2 + d2 * t);
        return Vector3<T>.Dot(between, between);
    }

    /// <summary>Whether <paramref name="ray"/> passes within <see cref="MathHelper.Scalar.ZeroTolerance{T}"/> of
    /// <paramref name="point"/> - the square root of it, the tolerance standing for a squared distance.</summary>
    public static bool RayIntersectsPoint<T>(Ray<T> ray, Vector3<T> point) where T : unmanaged, IFloatingPointIeee754<T> =>
        RayIntersectsSphereSquared(ray, point, MathHelper.Scalar.ZeroTolerance<T>(), out _);

    /// <summary>Whether the two rays meet, and the point where they do; for rays along one line, the start of the one
    /// the other runs into.</summary>
    public static bool RayIntersectsRay<T>(Ray<T> ray1, Ray<T> ray2, out Vector3<T> point) where T : unmanaged, IFloatingPointIeee754<T>
    {
        point = Vector3<T>.Zero;
        var cross = Vector3<T>.Cross(ray1.Direction, ray2.Direction);
        var denominator = cross.LengthSquared();
        var offset = ray2.Position - ray1.Position;
        var parallel = Parallel<T>() * Parallel<T>();
        if (denominator <= parallel * ray1.Direction.LengthSquared() * ray2.Direction.LengthSquared())
        {
            if (Vector3<T>.Cross(offset, ray1.Direction).LengthSquared() > parallel * offset.LengthSquared() * ray1.Direction.LengthSquared())
            {
                return false;
            }

            if (Vector3<T>.Dot(offset, ray1.Direction) >= T.Zero)
            {
                point = ray2.Position;
                return true;
            }

            if (Vector3<T>.Dot(-offset, ray2.Direction) >= T.Zero)
            {
                point = ray1.Position;
                return true;
            }

            return false;
        }

        var s = Vector3<T>.Dot(Vector3<T>.Cross(offset, ray2.Direction), cross) / denominator;
        var t = Vector3<T>.Dot(Vector3<T>.Cross(offset, ray1.Direction), cross) / denominator;
        if (s < T.Zero || t < T.Zero)
        {
            return false;
        }

        var point1 = ray1.Position + s * ray1.Direction;
        if (!Vector3<T>.NearEqual(ray2.Position + t * ray2.Direction, point1))
        {
            return false;
        }

        point = point1;
        return true;
    }

    /// <summary>Whether <paramref name="ray"/> meets <paramref name="plane"/> ahead of its start, and how far along it;
    /// a ray along the plane, within <see cref="MathHelper.Scalar.ZeroTolerance{T}"/>, does not.</summary>
    public static bool RayIntersectsPlane<T>(Ray<T> ray, Plane<T> plane, out T distance) where T : unmanaged, IFloatingPointIeee754<T>
    {
        distance = T.Zero;
        var direction = Vector3<T>.Dot(plane.Normal, ray.Direction);
        if (MathHelper.Scalar.IsZero(direction))
        {
            return false;
        }

        var along = (-plane.D - Vector3<T>.Dot(plane.Normal, ray.Position)) / direction;
        if (along < T.Zero)
        {
            return false;
        }

        distance = along;
        return true;
    }

    /// <summary>Whether <paramref name="ray"/> meets <paramref name="plane"/> ahead of its start, and where.</summary>
    public static bool RayIntersectsPlane<T>(Ray<T> ray, Plane<T> plane, out Vector3<T> point) where T : unmanaged, IFloatingPointIeee754<T> =>
        PointAt(ray, RayIntersectsPlane(ray, plane, out T distance), distance, out point);

    /// <summary>Whether <paramref name="ray"/> meets the triangle <paramref name="vertex1"/>, <paramref name="vertex2"/>,
    /// <paramref name="vertex3"/> from either side, and how far along it; a ray along the triangle's plane does not.</summary>
    public static bool RayIntersectsTriangle<T>(Ray<T> ray, Vector3<T> vertex1, Vector3<T> vertex2, Vector3<T> vertex3, out T distance)
        where T : unmanaged, IFloatingPointIeee754<T>
    {
        distance = T.Zero;
        var edge1 = vertex2 - vertex1;
        var edge2 = vertex3 - vertex1;
        var directionCrossEdge2 = Vector3<T>.Cross(ray.Direction, edge2);
        var determinant = Vector3<T>.Dot(edge1, directionCrossEdge2);
        var scale = edge1.LengthSquared() * edge2.LengthSquared() * ray.Direction.LengthSquared();
        if (determinant * determinant <= T.CreateTruncating(1e-14) * scale)
        {
            return false;
        }

        var inverse = T.One / determinant;
        var fromVertex = ray.Position - vertex1;
        var u = Vector3<T>.Dot(fromVertex, directionCrossEdge2) * inverse;
        if (u < T.Zero || u > T.One)
        {
            return false;
        }

        var fromVertexCrossEdge1 = Vector3<T>.Cross(fromVertex, edge1);
        var v = Vector3<T>.Dot(ray.Direction, fromVertexCrossEdge1) * inverse;
        if (v < T.Zero || u + v > T.One)
        {
            return false;
        }

        var along = Vector3<T>.Dot(edge2, fromVertexCrossEdge1) * inverse;
        if (along < T.Zero)
        {
            return false;
        }

        distance = along;
        return true;
    }

    /// <summary>Whether <paramref name="ray"/> meets the triangle, and where.</summary>
    public static bool RayIntersectsTriangle<T>(Ray<T> ray, Vector3<T> vertex1, Vector3<T> vertex2, Vector3<T> vertex3, out Vector3<T> point)
        where T : unmanaged, IFloatingPointIeee754<T> =>
        PointAt(ray, RayIntersectsTriangle(ray, vertex1, vertex2, vertex3, out T distance), distance, out point);

    /// <summary>Whether <paramref name="ray"/> meets <paramref name="box"/>, and how far along it - zero from inside. A
    /// direction component within <see cref="MathHelper.Scalar.ZeroTolerance{T}"/> of zero counts as none.</summary>
    public static bool RayIntersectsBox<T>(Ray<T> ray, BoundingBox<T> box, out T distance) where T : unmanaged, IFloatingPointIeee754<T>
    {
        distance = T.Zero;
        var nearest = T.Zero;
        var farthest = T.PositiveInfinity;
        for (var axis = 0; axis < 3; axis++)
        {
            var (position, direction) = (ray.Position[axis], ray.Direction[axis]);
            var (minimum, maximum) = (box.Minimum[axis], box.Maximum[axis]);
            if (MathHelper.Scalar.IsZero(direction))
            {
                if (position < minimum || position > maximum)
                {
                    return false;
                }

                continue;
            }

            var inverse = T.One / direction;
            var (t1, t2) = ((minimum - position) * inverse, (maximum - position) * inverse);
            if (t1 > t2)
            {
                (t1, t2) = (t2, t1);
            }

            nearest = T.Max(t1, nearest);
            farthest = T.Min(t2, farthest);
            if (nearest > farthest)
            {
                return false;
            }
        }

        distance = nearest;
        return true;
    }

    /// <summary>Whether <paramref name="ray"/> meets <paramref name="box"/>, and where.</summary>
    public static bool RayIntersectsBox<T>(Ray<T> ray, BoundingBox<T> box, out Vector3<T> point) where T : unmanaged, IFloatingPointIeee754<T> =>
        PointAt(ray, RayIntersectsBox(ray, box, out T distance), distance, out point);

    /// <summary>Whether <paramref name="ray"/> meets <paramref name="sphere"/>, and how far along it - zero from
    /// inside.</summary>
    public static bool RayIntersectsSphere<T>(Ray<T> ray, BoundingSphere<T> sphere, out T distance)
        where T : unmanaged, IFloatingPointIeee754<T> =>
        RayIntersectsSphereSquared(ray, sphere.Center, sphere.Radius * sphere.Radius, out distance);

    /// <summary>Whether <paramref name="ray"/> meets <paramref name="sphere"/>, and where.</summary>
    public static bool RayIntersectsSphere<T>(Ray<T> ray, BoundingSphere<T> sphere, out Vector3<T> point)
        where T : unmanaged, IFloatingPointIeee754<T> =>
        PointAt(ray, RayIntersectsSphere(ray, sphere, out T distance), distance, out point);

    /// <summary>Which side of <paramref name="plane"/> <paramref name="point"/> is on, or whether it lies on it.</summary>
    public static PlaneIntersectionType PlaneIntersectsPoint<T>(Plane<T> plane, Vector3<T> point) where T : unmanaged, IFloatingPointIeee754<T>
    {
        var distance = DistancePlanePoint(plane, point);
        return distance > T.Zero ? PlaneIntersectionType.Front
            : distance < T.Zero ? PlaneIntersectionType.Back : PlaneIntersectionType.Intersecting;
    }

    /// <summary>Whether the two planes meet: whether their normals are not parallel, within
    /// <see cref="MathHelper.Scalar.ZeroTolerance{T}"/>.</summary>
    public static bool PlaneIntersectsPlane<T>(Plane<T> plane1, Plane<T> plane2) where T : unmanaged, IFloatingPointIeee754<T> =>
        !MathHelper.Scalar.IsZero(Vector3<T>.Cross(plane1.Normal, plane2.Normal).LengthSquared());

    /// <summary>Whether the two planes meet, and the line where they do, its direction of length one.</summary>
    public static bool PlaneIntersectsPlane<T>(Plane<T> plane1, Plane<T> plane2, out Ray<T> line) where T : unmanaged, IFloatingPointIeee754<T>
    {
        var direction = Vector3<T>.Cross(plane1.Normal, plane2.Normal);
        var denominator = direction.LengthSquared();
        if (MathHelper.Scalar.IsZero(denominator))
        {
            line = default;
            return false;
        }

        var point = Vector3<T>.Cross(plane2.D * plane1.Normal - plane1.D * plane2.Normal, direction) / denominator;
        line = new Ray<T>(point, Vector3<T>.Normalize(direction));
        return true;
    }

    /// <summary>Which side of <paramref name="plane"/> the triangle is on, or whether it crosses it.</summary>
    public static PlaneIntersectionType PlaneIntersectsTriangle<T>(Plane<T> plane, Vector3<T> vertex1, Vector3<T> vertex2, Vector3<T> vertex3)
        where T : unmanaged, IFloatingPointIeee754<T>
    {
        var (side1, side2, side3) = (PlaneIntersectsPoint(plane, vertex1), PlaneIntersectsPoint(plane, vertex2),
            PlaneIntersectsPoint(plane, vertex3));
        return side1 == side2 && side2 == side3 && side1 != PlaneIntersectionType.Intersecting ? side1 : PlaneIntersectionType.Intersecting;
    }

    /// <summary>Which side of <paramref name="plane"/> <paramref name="box"/> is on, or whether it crosses it.</summary>
    public static PlaneIntersectionType PlaneIntersectsBox<T>(Plane<T> plane, BoundingBox<T> box) where T : unmanaged, IFloatingPointIeee754<T>
    {
        var normal = plane.Normal;
        var nearest = new Vector3<T>(normal.X >= T.Zero ? box.Minimum.X : box.Maximum.X,
            normal.Y >= T.Zero ? box.Minimum.Y : box.Maximum.Y, normal.Z >= T.Zero ? box.Minimum.Z : box.Maximum.Z);
        if (Vector3<T>.Dot(normal, nearest) + plane.D > T.Zero)
        {
            return PlaneIntersectionType.Front;
        }

        var farthest = new Vector3<T>(normal.X >= T.Zero ? box.Maximum.X : box.Minimum.X,
            normal.Y >= T.Zero ? box.Maximum.Y : box.Minimum.Y, normal.Z >= T.Zero ? box.Maximum.Z : box.Minimum.Z);
        return Vector3<T>.Dot(normal, farthest) + plane.D < T.Zero ? PlaneIntersectionType.Back : PlaneIntersectionType.Intersecting;
    }

    /// <summary>Which side of <paramref name="plane"/> <paramref name="sphere"/> is on, or whether it crosses it.</summary>
    public static PlaneIntersectionType PlaneIntersectsSphere<T>(Plane<T> plane, BoundingSphere<T> sphere)
        where T : unmanaged, IFloatingPointIeee754<T>
    {
        var distance = DistancePlanePoint(plane, sphere.Center);
        return distance > sphere.Radius ? PlaneIntersectionType.Front
            : distance < -sphere.Radius ? PlaneIntersectionType.Back : PlaneIntersectionType.Intersecting;
    }

    /// <summary>Whether the two boxes touch or overlap.</summary>
    public static bool BoxIntersectsBox<T>(BoundingBox<T> box1, BoundingBox<T> box2) where T : unmanaged, IFloatingPointIeee754<T> =>
        !(box1.Minimum.X > box2.Maximum.X || box2.Minimum.X > box1.Maximum.X || box1.Minimum.Y > box2.Maximum.Y
          || box2.Minimum.Y > box1.Maximum.Y || box1.Minimum.Z > box2.Maximum.Z || box2.Minimum.Z > box1.Maximum.Z);

    /// <summary>Whether <paramref name="box"/> and <paramref name="sphere"/> touch or overlap.</summary>
    public static bool BoxIntersectsSphere<T>(BoundingBox<T> box, BoundingSphere<T> sphere) where T : unmanaged, IFloatingPointIeee754<T> =>
        Vector3<T>.DistanceSquared(sphere.Center, Vector3<T>.Clamp(sphere.Center, box.Minimum, box.Maximum)) <= sphere.Radius * sphere.Radius;

    /// <summary>Whether <paramref name="sphere"/> touches or overlaps the triangle.</summary>
    public static bool SphereIntersectsTriangle<T>(BoundingSphere<T> sphere, Vector3<T> vertex1, Vector3<T> vertex2, Vector3<T> vertex3)
        where T : unmanaged, IFloatingPointIeee754<T> =>
        Vector3<T>.DistanceSquared(ClosestPointPointTriangle(sphere.Center, vertex1, vertex2, vertex3), sphere.Center)
        <= sphere.Radius * sphere.Radius;

    /// <summary>Whether the two spheres touch or overlap.</summary>
    public static bool SphereIntersectsSphere<T>(BoundingSphere<T> sphere1, BoundingSphere<T> sphere2)
        where T : unmanaged, IFloatingPointIeee754<T>
    {
        var reach = sphere1.Radius + sphere2.Radius;
        return Vector3<T>.DistanceSquared(sphere1.Center, sphere2.Center) <= reach * reach;
    }

    /// <summary>Whether <paramref name="box"/> holds <paramref name="point"/>, its faces included.</summary>
    public static ContainmentType BoxContainsPoint<T>(BoundingBox<T> box, Vector3<T> point) where T : unmanaged, IFloatingPointIeee754<T> =>
        box.Minimum.X <= point.X && box.Maximum.X >= point.X && box.Minimum.Y <= point.Y && box.Maximum.Y >= point.Y
        && box.Minimum.Z <= point.Z && box.Maximum.Z >= point.Z
            ? ContainmentType.Contains
            : ContainmentType.Disjoint;

    /// <summary>Whether <paramref name="box1"/> holds <paramref name="box2"/> wholly, in part or not at all.</summary>
    public static ContainmentType BoxContainsBox<T>(BoundingBox<T> box1, BoundingBox<T> box2) where T : unmanaged, IFloatingPointIeee754<T>
    {
        if (!BoxIntersectsBox(box1, box2))
        {
            return ContainmentType.Disjoint;
        }

        return box1.Minimum.X <= box2.Minimum.X && box2.Maximum.X <= box1.Maximum.X && box1.Minimum.Y <= box2.Minimum.Y
               && box2.Maximum.Y <= box1.Maximum.Y && box1.Minimum.Z <= box2.Minimum.Z && box2.Maximum.Z <= box1.Maximum.Z
            ? ContainmentType.Contains
            : ContainmentType.Intersects;
    }

    /// <summary>Whether <paramref name="box"/> holds <paramref name="sphere"/> wholly, in part or not at all; a sphere
    /// held whole must also be narrower than the box each way.</summary>
    public static ContainmentType BoxContainsSphere<T>(BoundingBox<T> box, BoundingSphere<T> sphere) where T : unmanaged, IFloatingPointIeee754<T>
    {
        if (!BoxIntersectsSphere(box, sphere))
        {
            return ContainmentType.Disjoint;
        }

        var (center, radius) = (sphere.Center, sphere.Radius);
        for (var axis = 0; axis < 3; axis++)
        {
            var (minimum, maximum) = (box.Minimum[axis], box.Maximum[axis]);
            if (!(minimum + radius <= center[axis] && center[axis] <= maximum - radius && maximum - minimum > radius))
            {
                return ContainmentType.Intersects;
            }
        }

        return ContainmentType.Contains;
    }

    /// <summary>Whether <paramref name="sphere"/> holds <paramref name="point"/>, its surface included.</summary>
    public static ContainmentType SphereContainsPoint<T>(BoundingSphere<T> sphere, Vector3<T> point)
        where T : unmanaged, IFloatingPointIeee754<T> =>
        Vector3<T>.DistanceSquared(point, sphere.Center) <= sphere.Radius * sphere.Radius
            ? ContainmentType.Contains
            : ContainmentType.Disjoint;

    /// <summary>Whether <paramref name="sphere"/> holds the triangle wholly, in part or not at all.</summary>
    public static ContainmentType SphereContainsTriangle<T>(BoundingSphere<T> sphere, Vector3<T> vertex1, Vector3<T> vertex2, Vector3<T> vertex3)
        where T : unmanaged, IFloatingPointIeee754<T>
    {
        if (SphereContainsPoint(sphere, vertex1) == ContainmentType.Contains && SphereContainsPoint(sphere, vertex2) == ContainmentType.Contains
            && SphereContainsPoint(sphere, vertex3) == ContainmentType.Contains)
        {
            return ContainmentType.Contains;
        }

        return SphereIntersectsTriangle(sphere, vertex1, vertex2, vertex3) ? ContainmentType.Intersects : ContainmentType.Disjoint;
    }

    /// <summary>Whether <paramref name="sphere"/> holds <paramref name="box"/> wholly - every corner - in part or not at
    /// all.</summary>
    public static ContainmentType SphereContainsBox<T>(BoundingSphere<T> sphere, BoundingBox<T> box) where T : unmanaged, IFloatingPointIeee754<T>
    {
        if (!BoxIntersectsSphere(box, sphere))
        {
            return ContainmentType.Disjoint;
        }

        var radiusSquared = sphere.Radius * sphere.Radius;
        for (var corner = 0; corner < 8; corner++)
        {
            var point = new Vector3<T>((corner & 1) == 0 ? box.Minimum.X : box.Maximum.X, (corner & 2) == 0 ? box.Minimum.Y : box.Maximum.Y,
                (corner & 4) == 0 ? box.Minimum.Z : box.Maximum.Z);
            if (Vector3<T>.DistanceSquared(sphere.Center, point) > radiusSquared)
            {
                return ContainmentType.Intersects;
            }
        }

        return ContainmentType.Contains;
    }

    /// <summary>Whether <paramref name="sphere1"/> holds <paramref name="sphere2"/> wholly, in part or not at all.</summary>
    public static ContainmentType SphereContainsSphere<T>(BoundingSphere<T> sphere1, BoundingSphere<T> sphere2)
        where T : unmanaged, IFloatingPointIeee754<T>
    {
        var distance = Vector3<T>.Distance(sphere1.Center, sphere2.Center);
        if (sphere1.Radius + sphere2.Radius < distance)
        {
            return ContainmentType.Disjoint;
        }

        return sphere1.Radius - sphere2.Radius < distance ? ContainmentType.Intersects : ContainmentType.Contains;
    }

    private static bool RayIntersectsSphereSquared<T>(Ray<T> ray, Vector3<T> center, T radiusSquared, out T distance)
        where T : unmanaged, IFloatingPointIeee754<T>
    {
        distance = T.Zero;
        var offset = ray.Position - center;
        var b = Vector3<T>.Dot(offset, ray.Direction);
        var c = Vector3<T>.Dot(offset, offset) - radiusSquared;
        if (c > T.Zero && b > T.Zero)
        {
            return false;
        }

        var discriminant = b * b - c;
        if (discriminant < T.Zero)
        {
            return false;
        }

        distance = T.Max(-b - T.Sqrt(discriminant), T.Zero);
        return true;
    }

    private static bool PointAt<T>(Ray<T> ray, bool hit, T distance, out Vector3<T> point) where T : unmanaged, IFloatingPointIeee754<T>
    {
        point = hit ? ray.Position + ray.Direction * distance : Vector3<T>.Zero;
        return hit;
    }

    private static T Parallel<T>() where T : unmanaged, IFloatingPointIeee754<T> => T.CreateTruncating(1e-6);

    private static T SegmentsParallel<T>() where T : unmanaged, IFloatingPointIeee754<T> => T.CreateTruncating(1e-7);
}
