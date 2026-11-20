using System;

namespace Adamantium.DarkMatter.Shapes;

/// <summary>One triangle of a surface, met with a body as any convex shape is: flat, its two sides its faces. A world
/// keeps one and sets its corners for each triangle it meets.</summary>
internal sealed class TriangleCollisionShape : CollisionShape
{
    private Vector3F _first;
    private Vector3F _second;
    private Vector3F _third;
    private Vector3F _normal;

    public override CollisionShapeKind Kind => CollisionShapeKind.Triangle;

    // Asked for seldom - contacts never read it - so worked out only then.
    public override float BoundingRadius =>
        MathF.Sqrt(MathF.Max(_first.LengthSquared(), MathF.Max(_second.LengthSquared(), _third.LengthSquared())));

    public Vector3F First => _first;

    public Vector3F Second => _second;

    public Vector3F Third => _third;

    /// <summary>The normal of the side the corners wind counterclockwise round, of length one.</summary>
    public Vector3F Normal => _normal;

    /// <summary>Makes it the triangle <paramref name="first"/>, <paramref name="second"/>, <paramref name="third"/>, of
    /// normal <paramref name="normal"/>.</summary>
    public void Set(Vector3F first, Vector3F second, Vector3F third, Vector3F normal)
    {
        _first = first;
        _second = second;
        _third = third;
        _normal = normal;
    }

    public override MassProperties ComputeMass(float density) =>
        throw new InvalidOperationException("A triangle has no volume to weigh.");

    internal override BoundingBox<float> ComputeBounds(in LocalPose pose)
    {
        var first = pose.TransformPoint(_first);
        var second = pose.TransformPoint(_second);
        var third = pose.TransformPoint(_third);
        return new BoundingBox<float>(Vector3F.Min(first, Vector3F.Min(second, third)),
            Vector3F.Max(first, Vector3F.Max(second, third)));
    }

    public override Vector3F GetSupport(Vector3F direction)
    {
        var first = Vector3F.Dot(_first, direction);
        var second = Vector3F.Dot(_second, direction);
        var third = Vector3F.Dot(_third, direction);
        return first >= second && first >= third ? _first : second >= third ? _second : _third;
    }

    // Flat, its face toward any side is the whole triangle; a face clipped edge on keeps only what reaches the other.
    internal override int GetSupportingFace(Vector3F direction, Span<Vector3F> into)
    {
        into[0] = _first;
        into[1] = _second;
        into[2] = _third;
        return 3;
    }

    internal override int FlatFaceCount => 2;

    internal override Vector3F GetFlatFaceNormal(int face) => face == 0 ? _normal : -_normal;

    internal override float GetFlatFaceOffset(int face) => Vector3F.Dot(GetFlatFaceNormal(face), _first);

    internal override int FindFlatFace(Vector3F direction) => Vector3F.Dot(direction, _normal) >= 0 ? 0 : 1;

    internal override int GetFlatFace(int face, Span<Vector3F> into)
    {
        into[0] = _first;
        into[1] = face == 0 ? _second : _third;
        into[2] = face == 0 ? _third : _second;
        return 3;
    }

    public override bool CastRay(in Ray<float> ray, float maxDistance, out float distance, out Vector3F normal)
    {
        normal = default;
        if (!Collision.RayIntersectsTriangle(ray, _first, _second, _third, out distance) || distance > maxDistance)
            return false;

        normal = Vector3F.Dot(ray.Direction, _normal) <= 0 ? _normal : -_normal;
        return true;
    }
}
