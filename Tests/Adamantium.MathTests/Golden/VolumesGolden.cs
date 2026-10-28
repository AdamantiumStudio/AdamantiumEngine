using System;
using Adamantium.Mathematics.Typed;
using Matrix3x3 = Adamantium.Mathematics.Typed.Matrix3x3;
using Matrix4x4 = Adamantium.Mathematics.Typed.Matrix4x4;
using BoundingBox = Adamantium.Mathematics.Typed.BoundingBox;
using BoundingSphere = Adamantium.Mathematics.Typed.BoundingSphere;
using Collision = Adamantium.Mathematics.Typed.Collision;
using Ray = Adamantium.Mathematics.Typed.Ray;

namespace Adamantium.MathTests.Golden;

/// <summary>The recorded operations of the ray, the plane, the box, the sphere and their tests, answered by
/// <see cref="Ray{T}"/>, <see cref="Plane{T}"/>, <see cref="BoundingBox{T}"/>, <see cref="BoundingSphere{T}"/> and
/// <see cref="Collision"/>. An operation with arguments by reference answers what it returns, then each such argument,
/// then the instance.</summary>
public static class VolumesGolden
{
    /// <summary>Adds the operations of Ray, Plane, BoundingBox, BoundingSphere and Collision to <paramref name="map"/>.</summary>
    public static void Register(GoldenMap map)
    {
        RegisterRay(map);
        RegisterPlane(map);
        RegisterBox(map);
        RegisterSphere(map);
        RegisterCollision(map);
        map.NotMapped(key => key.Contains("OrientedBoundingBox") || key.Contains("BoundingCapsule"),
            "takes an oriented box or a capsule: checked with those types");
        map.Tolerance(key => key.StartsWith("Ray.GetPickRay("), 1e-3,
            "the direction is the difference of two points unprojected through a random, badly conditioned matrix: old " +
            "and new each miss a double reference in float, by up to 3.5e-4 on the recorded cases and wholly on nearly " +
            "singular matrices, neither nearer on average");
        map.ChangedWhere(key => key.StartsWith("Ray.GetPickRay(") || key.StartsWith("Plane.Transform(Plane"),
            inputs => Array.TrueForAll(inputs, double.IsFinite) && Array.Exists(inputs, value => Math.Abs(value) > 1e15),
            "old bug: the inverse inside overflowed float into NaN on 4e30 elements; the new inverse is scaled first");
        map.NotMapped(RaySphereOrPoint,
            "old bug: the ray's direction was taken to be of length one, so a longer one - every recorded ray - hit " +
            "spheres and points it passes by and gave wrong distances; TypedVolumeTests checks the new against exact " +
            "arithmetic");
        map.ChangedWhere(RayOrPlaneHit, inputs => Array.Exists(inputs, double.IsNaN),
            "old bug: a NaN anywhere made a hit at distance NaN, or planes that meet; now it is no hit");
        map.ChangedWhere(key => (RayOrPlaneHit(key) && !key.Contains("BoundingBox")) || key.StartsWith("Collision.ClosestPointSegmentPoint("),
            inputs => Array.TrueForAll(inputs, double.IsFinite) && Array.Exists(inputs, value => Math.Abs(value) > 1e15),
            "old bug: products of 4e30 overflowed float into NaN, which counted as a hit at distance NaN or gave a NaN " +
            "point; the new scales its vectors first - every such recorded case checked against double arithmetic");
        map.ChangedWhere(key => key.StartsWith("Collision.RayIntersectsRay(") || key.StartsWith("Ray.Intersects(this Ray"),
            inputs => IsZero(inputs, 3) || IsZero(inputs, 9),
            "old bug: a ray without a direction met every ray its start lay behind, one way round and not the other; now " +
            "it meets nothing");
        map.ChangedWhere(key => key.StartsWith("Collision.RayIntersectsLineSegment(") || key.StartsWith("Ray.Intersects(this Vector3F&,Vector3F&,Single"),
            inputs => IsZero(inputs, 3),
            "old bug: a ray without a direction gave a NaN distance, so no hit within any limit; now its start's distance");
        map.ChangedWhere(key => key.StartsWith("BoundingSphere.Transform(this Matrix4x4"), inputs => Sheared(inputs, 4),
            "old bug: the radius grew by the longest axis, too little for a matrix that shears - the carried sphere " +
            "stuck out; it grows by the most the matrix stretches any direction now - every recorded matrix shears, so " +
            "TypedVolumeTests checks the new against matrices of known singular values");
        map.ChangedWhere(key => key.StartsWith("Collision.ClosestPointSphere"),
            inputs => Array.Exists(inputs, value => Math.Abs(value) > 1e15) || (inputs[0] == inputs[4] && inputs[1] == inputs[5] && inputs[2] == inputs[6]),
            "old bug: for the center itself it gave the center, not a point of the surface, and far from the origin the " +
            "offset's square overflowed into NaN; now a point of the surface, the offset scaled first");
        map.ChangedWhere(key => key.StartsWith("Collision.BoxContainsSphere(") || key.StartsWith("BoundingBox.Contains(this BoundingSphere"),
            inputs => inputs[9] <= 0
                      && (inputs[3] - inputs[0] <= inputs[9] || inputs[4] - inputs[1] <= inputs[9] || inputs[5] - inputs[2] <= inputs[9]),
            "old bug: a sphere held whole had also to be wider than its radius each way, so a point - radius 0 - in a " +
            "flat box was only intersecting; now one held whole is contained");
        map.ChangedWhere(key => key.StartsWith("Collision.BoxContainsSphere(") || key.StartsWith("BoundingBox.Contains(this BoundingSphere"),
            inputs => Array.Exists(inputs, double.IsNaN),
            "old bug: with a NaN, BoxIntersectsSphere said apart while BoxContainsSphere said intersecting; now both say " +
            "apart");
        map.ChangedWhere(key => key.StartsWith("Collision.DistanceBoxPoint("), inputs => Array.Exists(inputs, double.IsNaN),
            "old bug: every comparison with a NaN failed, so a NaN point counted as inside, at distance 0; now NaN in " +
            "gives NaN out");
        map.Calls("Ray.Equals(this Ray)", i => Self(i.Ray<float>(), i.Ray<float>(), (x, o) => x.Equals(o)));
        map.Calls("Ray.Equals(this Ray&)", i =>
        {
            var (x, o) = (i.Ray<float>(), i.Ray<float>());
            return [x.Equals(o), o, x];
        });
        map.Returns("Ray.op_Equality(Ray,Ray)", i => i.Ray<float>() == i.Ray<float>());
        map.Returns("Ray.op_Inequality(Ray,Ray)", i => i.Ray<float>() != i.Ray<float>());
        map.ExactWhereOldWasNear("Ray", (a, b) => Ray<float>.NearEqual(new GoldenInputs(a).Ray<float>(), new GoldenInputs(b).Ray<float>()));
    }

    private static void RegisterRay(GoldenMap map)
    {
        Instance(map, "Ray.Intersects(this Ray&)", i => i.Ray<float>(), (x, i) =>
        {
            var ray = i.Ray<float>();
            return [x.Intersects(ray, out Vector3<float> _), ray];
        });
        Instance(map, "Ray.Intersects(this Ray&,Vector3F&)", i => i.Ray<float>(), (x, i) =>
        {
            var ray = i.Ray<float>();
            i.Vector3<float>();
            return [x.Intersects(ray, out Vector3<float> point), ray, point];
        });
        Instance(map, "Ray.Intersects(this Plane&)", i => i.Ray<float>(), (x, i) =>
        {
            var plane = i.Plane<float>();
            return [x.Intersects(plane, out float _), plane];
        });
        Instance(map, "Ray.Intersects(this Plane&,Single&)", i => i.Ray<float>(), (x, i) =>
        {
            var plane = i.Plane<float>();
            i.Number<float>();
            return [x.Intersects(plane, out float distance), plane, distance];
        });
        Instance(map, "Ray.Intersects(this Plane&,Vector3F&)", i => i.Ray<float>(), (x, i) =>
        {
            var plane = i.Plane<float>();
            i.Vector3<float>();
            return [x.Intersects(plane, out Vector3<float> point), plane, point];
        });
        Instance(map, "Ray.Intersects(this Vector3F&,Vector3F&,Vector3F&)", i => i.Ray<float>(), (x, i) =>
        {
            var (a, b, c) = (i.Vector3<float>(), i.Vector3<float>(), i.Vector3<float>());
            return [x.Intersects(a, b, c, out float _), a, b, c];
        });
        Instance(map, "Ray.Intersects(this Vector3F&,Vector3F&,Single,Single&,Vector3F&)", i => i.Ray<float>(), (x, i) =>
        {
            var (start, end, limit) = (i.Vector3<float>(), i.Vector3<float>(), i.Number<float>());
            i.Number<float>();
            i.Vector3<float>();
            return [x.Intersects(start, end, limit, out var distance, out var point), start, end, distance, point];
        });
        Instance(map, "Ray.Intersects(this Vector3F&,Vector3F&,Vector3F&,Single&)", i => i.Ray<float>(), (x, i) =>
        {
            var (a, b, c) = (i.Vector3<float>(), i.Vector3<float>(), i.Vector3<float>());
            i.Number<float>();
            return [x.Intersects(a, b, c, out float distance), a, b, c, distance];
        });
        Instance(map, "Ray.Intersects(this Vector3F&,Vector3F&,Vector3F&,Vector3F&)", i => i.Ray<float>(), (x, i) =>
        {
            var (a, b, c) = (i.Vector3<float>(), i.Vector3<float>(), i.Vector3<float>());
            i.Vector3<float>();
            return [x.Intersects(a, b, c, out Vector3<float> point), a, b, c, point];
        });
        Instance(map, "Ray.Intersects(this BoundingBox&)", i => i.Ray<float>(), (x, i) =>
        {
            var box = i.BoundingBox<float>();
            return [x.Intersects(box), box];
        });
        Instance(map, "Ray.Intersects(this BoundingBox)", i => i.Ray<float>(), (x, i) => [x.Intersects(i.BoundingBox<float>())]);
        Instance(map, "Ray.Intersects(this BoundingBox&,Single&)", i => i.Ray<float>(), (x, i) =>
        {
            var box = i.BoundingBox<float>();
            i.Number<float>();
            return [x.Intersects(box, out float distance), box, distance];
        });
        Instance(map, "Ray.Intersects(this BoundingBox&,Vector3F&)", i => i.Ray<float>(), (x, i) =>
        {
            var box = i.BoundingBox<float>();
            i.Vector3<float>();
            return [x.Intersects(box, out Vector3<float> point), box, point];
        });
        map.Returns("Ray.GetPickRay(Int32,Int32,ViewportF,Matrix4x4F)",
            i => Ray.CreateFromViewport(i.Number<float>(), i.Number<float>(), i.Viewport<float>(), i.Matrix4x4<float>()));
    }

    private static void RegisterPlane(GoldenMap map)
    {
        Instance(map, "Plane.Normalize(this )", i => i.Plane<float>(), (x, _) =>
        {
            x.Normalize();
            return [x];
        }, false);
        Instance(map, "Plane.Intersects(this Vector3F&)", i => i.Plane<float>(), (x, i) =>
        {
            var point = i.Vector3<float>();
            return [x.Intersects(point), point];
        });
        Instance(map, "Plane.Intersects(this Ray&)", i => i.Plane<float>(), (x, i) =>
        {
            var ray = i.Ray<float>();
            return [x.Intersects(ray, out float _), ray];
        });
        Instance(map, "Plane.Intersects(this Ray&,Single&)", i => i.Plane<float>(), (x, i) =>
        {
            var ray = i.Ray<float>();
            i.Number<float>();
            return [x.Intersects(ray, out float distance), ray, distance];
        });
        Instance(map, "Plane.Intersects(this Ray&,Vector3F&)", i => i.Plane<float>(), (x, i) =>
        {
            var ray = i.Ray<float>();
            i.Vector3<float>();
            return [x.Intersects(ray, out Vector3<float> point), ray, point];
        });
        Instance(map, "Plane.Intersects(this Plane&)", i => i.Plane<float>(), (x, i) =>
        {
            var plane = i.Plane<float>();
            return [x.Intersects(plane), plane];
        });
        Instance(map, "Plane.Intersects(this Plane&,Ray&)", i => i.Plane<float>(), (x, i) =>
        {
            var plane = i.Plane<float>();
            i.Ray<float>();
            return [x.Intersects(plane, out Ray<float> line), plane, line];
        });
        Instance(map, "Plane.Intersects(this Vector3F&,Vector3F&,Vector3F&)", i => i.Plane<float>(), (x, i) =>
        {
            var (a, b, c) = (i.Vector3<float>(), i.Vector3<float>(), i.Vector3<float>());
            return [x.Intersects(a, b, c), a, b, c];
        });
        Instance(map, "Plane.Intersects(this BoundingBox&)", i => i.Plane<float>(), (x, i) =>
        {
            var box = i.BoundingBox<float>();
            return [x.Intersects(box), box];
        });
        Instance(map, "Plane.Intersects(this BoundingSphere&)", i => i.Plane<float>(), (x, i) =>
        {
            var sphere = i.BoundingSphere<float>();
            return [x.Intersects(sphere), sphere];
        });
        Instance(map, "Plane.Reflection(this Matrix4x4F&)", i => i.Plane<float>(), (x, i) =>
        {
            i.Matrix4x4<float>();
            return [Matrix4x4.CreateReflection(x)];
        });
        Instance(map, "Plane.Reflection(this )", i => i.Plane<float>(), (x, _) => [Matrix4x4.CreateReflection(x)]);
        Instance(map, "Plane.Shadow(this Vector4F&,Matrix4x4F&)", i => i.Plane<float>(), (x, i) =>
        {
            var light = i.Vector4<float>();
            i.Matrix4x4<float>();
            return [light, Matrix4x4.CreateShadow(light, x)];
        });
        Instance(map, "Plane.Shadow(this Vector4F)", i => i.Plane<float>(), (x, i) => [Matrix4x4.CreateShadow(i.Vector4<float>(), x)]);
        Instance(map, "Plane.Reflection(this Matrix3x3F&)", i => i.Plane<float>(), (x, i) =>
        {
            i.Matrix3x3<float>();
            return [Matrix3x3.CreateReflection(x)];
        });
        Instance(map, "Plane.Reflection3x3(this )", i => i.Plane<float>(), (x, _) => [Matrix3x3.CreateReflection(x)]);
        map.Returns("Plane.Shadow(Vector4F,Plane)", i =>
        {
            var light = i.Vector4<float>();
            return Matrix3x3.CreateShadow(light, i.Plane<float>());
        });
        map.Returns("Plane.Multiply(Plane,Single)", i => Plane<float>.Multiply(i.Plane<float>(), i.Number<float>()));
        map.Returns("Plane.Dot(Plane,Vector4F)", i => Plane<float>.Dot(i.Plane<float>(), i.Vector4<float>()));
        map.Returns("Plane.DotCoordinate(Plane,Vector3F)", i => Plane<float>.DotCoordinate(i.Plane<float>(), i.Vector3<float>()));
        map.Returns("Plane.DotNormal(Plane,Vector3F)", i => Plane<float>.DotNormal(i.Plane<float>(), i.Vector3<float>()));
        map.Returns("Plane.Normalize(Plane)", i => Plane<float>.Normalize(i.Plane<float>()));
        map.Returns("Plane.Transform(Plane,QuaternionF)", i => Plane<float>.Transform(i.Plane<float>(), i.Quaternion<float>()));
        map.Returns("Plane.Transform(Plane,Matrix4x4F)", i => Plane<float>.Transform(i.Plane<float>(), i.Matrix4x4<float>()));
        map.Returns("Plane.op_Multiply(Single,Plane)", i => i.Number<float>() * i.Plane<float>());
        map.Returns("Plane.op_Multiply(Plane,Single)", i => i.Plane<float>() * i.Number<float>());
        map.Calls("Plane.Equals(this Plane)", i => Self(i.Plane<float>(), i.Plane<float>(), (x, o) => x.Equals(o)));
        map.Calls("Plane.Equals(this Plane&)", i =>
        {
            var (x, o) = (i.Plane<float>(), i.Plane<float>());
            return [x.Equals(o), o, x];
        });
        map.Returns("Plane.op_Equality(Plane,Plane)", i => i.Plane<float>() == i.Plane<float>());
        map.Returns("Plane.op_Inequality(Plane,Plane)", i => i.Plane<float>() != i.Plane<float>());
        map.ExactWhereOldWasNear("Plane", (a, b) => OldPlaneEquals(new GoldenInputs(a).Plane<float>(), new GoldenInputs(b).Plane<float>()));
    }

    private static void RegisterBox(GoldenMap map)
    {
        Instance(map, "BoundingBox.Support(this Vector3F)", i => i.BoundingBox<float>(), (x, i) => [x.Support(i.Vector3<float>())]);
        Instance(map, "BoundingBox.Intersects(this Ray&)", i => i.BoundingBox<float>(), (x, i) =>
        {
            var ray = i.Ray<float>();
            return [x.Intersects(ray), ray];
        });
        Instance(map, "BoundingBox.Intersects(this Ray&,Single&)", i => i.BoundingBox<float>(), (x, i) =>
        {
            var ray = i.Ray<float>();
            i.Number<float>();
            return [x.Intersects(ray, out float distance), ray, distance];
        });
        Instance(map, "BoundingBox.Intersects(this Ray&,Vector3F&)", i => i.BoundingBox<float>(), (x, i) =>
        {
            var ray = i.Ray<float>();
            i.Vector3<float>();
            return [x.Intersects(ray, out Vector3<float> point), ray, point];
        });
        Instance(map, "BoundingBox.Intersects(this Plane&)", i => i.BoundingBox<float>(), (x, i) =>
        {
            var plane = i.Plane<float>();
            return [x.Intersects(plane), plane];
        });
        BoxWith(map, "Intersects", "BoundingBox", i => i.BoundingBox<float>(), (x, o) => x.Intersects(o));
        BoxWith(map, "Intersects", "BoundingSphere", i => i.BoundingSphere<float>(), (x, o) => x.Intersects(o));
        BoxWith(map, "Contains", "Vector3F", i => i.Vector3<float>(), (x, o) => x.Contains(o));
        BoxWith(map, "Contains", "BoundingBox", i => i.BoundingBox<float>(), (x, o) => x.Contains(o));
        BoxWith(map, "Contains", "BoundingSphere", i => i.BoundingSphere<float>(), (x, o) => x.Contains(o));
        map.Returns("BoundingBox.FromSphere(BoundingSphere)", i => BoundingBox.CreateFromSphere(i.BoundingSphere<float>()));
        map.Returns("BoundingBox.Merge(BoundingBox,BoundingBox)",
            i => BoundingBox.CreateMerged(i.BoundingBox<float>(), i.BoundingBox<float>()));
        map.Calls("BoundingBox.Equals(this BoundingBox)",
            i => Self(i.BoundingBox<float>(), i.BoundingBox<float>(), (x, o) => x.Equals(o)));
        map.Calls("BoundingBox.Equals(this BoundingBox&)", i =>
        {
            var (x, o) = (i.BoundingBox<float>(), i.BoundingBox<float>());
            return [x.Equals(o), o, x];
        });
        map.Returns("BoundingBox.op_Equality(BoundingBox,BoundingBox)", i => i.BoundingBox<float>() == i.BoundingBox<float>());
        map.Returns("BoundingBox.op_Inequality(BoundingBox,BoundingBox)", i => i.BoundingBox<float>() != i.BoundingBox<float>());
        map.ExactWhereOldWasNear("BoundingBox",
            (a, b) => BoundingBox<float>.NearEqual(new GoldenInputs(a).BoundingBox<float>(), new GoldenInputs(b).BoundingBox<float>()));
    }

    private static void RegisterSphere(GoldenMap map)
    {
        Instance(map, "BoundingSphere.get_Diameter(this )", i => i.BoundingSphere<float>(), (x, _) => [x.Diameter]);
        Instance(map, "BoundingSphere.Transform(this Single,Vector3F)", i => i.BoundingSphere<float>(),
            (x, i) => [x.Transform(i.Number<float>(), i.Vector3<float>())]);
        Instance(map, "BoundingSphere.Transform(this Single&,Vector3F&)", i => i.BoundingSphere<float>(), (x, i) =>
        {
            var (scale, translation) = (i.Number<float>(), i.Vector3<float>());
            return [x.Transform(scale, translation), scale, translation];
        });
        Instance(map, "BoundingSphere.Transform(this Matrix4x4F)", i => i.BoundingSphere<float>(), (x, i) => [x.Transform(i.Matrix4x4<float>())]);
        Instance(map, "BoundingSphere.Support(this Vector3F)", i => i.BoundingSphere<float>(), (x, i) => [x.Support(i.Vector3<float>())]);
        Instance(map, "BoundingSphere.Intersects(this Plane&)", i => i.BoundingSphere<float>(), (x, i) =>
        {
            var plane = i.Plane<float>();
            return [x.Intersects(plane), plane];
        });
        Instance(map, "BoundingSphere.Intersects(this Vector3F&,Vector3F&,Vector3F&)", i => i.BoundingSphere<float>(), (x, i) =>
        {
            var (a, b, c) = (i.Vector3<float>(), i.Vector3<float>(), i.Vector3<float>());
            return [x.Intersects(a, b, c), a, b, c];
        });
        SphereWith(map, "Intersects", "BoundingBox", i => i.BoundingBox<float>(), (x, o) => x.Intersects(o));
        SphereWith(map, "Intersects", "BoundingSphere", i => i.BoundingSphere<float>(), (x, o) => x.Intersects(o));
        Instance(map, "BoundingSphere.Contains(this Vector3F&)", i => i.BoundingSphere<float>(), (x, i) =>
        {
            var point = i.Vector3<float>();
            return [x.Contains(point), point];
        });
        Instance(map, "BoundingSphere.Contains(this Vector3F&,Vector3F&,Vector3F&)", i => i.BoundingSphere<float>(), (x, i) =>
        {
            var (a, b, c) = (i.Vector3<float>(), i.Vector3<float>(), i.Vector3<float>());
            return [x.Contains(a, b, c), a, b, c];
        });
        Instance(map, "BoundingSphere.Contains(this BoundingBox&)", i => i.BoundingSphere<float>(), (x, i) =>
        {
            var box = i.BoundingBox<float>();
            return [x.Contains(box), box];
        });
        Instance(map, "BoundingSphere.Contains(this BoundingSphere&)", i => i.BoundingSphere<float>(), (x, i) =>
        {
            var sphere = i.BoundingSphere<float>();
            return [x.Contains(sphere), sphere];
        });
        map.Returns("BoundingSphere.FromBox(BoundingBox)", i => BoundingSphere.CreateFromBox(i.BoundingBox<float>()));
        map.Returns("BoundingSphere.Merge(BoundingSphere,BoundingSphere)",
            i => BoundingSphere.CreateMerged(i.BoundingSphere<float>(), i.BoundingSphere<float>()));
        map.Calls("BoundingSphere.Equals(this BoundingSphere)",
            i => Self(i.BoundingSphere<float>(), i.BoundingSphere<float>(), (x, o) => x.Equals(o)));
        map.Calls("BoundingSphere.Equals(this BoundingSphere&)", i =>
        {
            var (x, o) = (i.BoundingSphere<float>(), i.BoundingSphere<float>());
            return [x.Equals(o), o, x];
        });
        map.Returns("BoundingSphere.op_Equality(BoundingSphere,BoundingSphere)",
            i => i.BoundingSphere<float>() == i.BoundingSphere<float>());
        map.Returns("BoundingSphere.op_Inequality(BoundingSphere,BoundingSphere)",
            i => i.BoundingSphere<float>() != i.BoundingSphere<float>());
        map.ExactWhereOldWasNear("BoundingSphere",
            (a, b) => OldSphereEquals(new GoldenInputs(a).BoundingSphere<float>(), new GoldenInputs(b).BoundingSphere<float>()));
    }

    private static void RegisterCollision(GoldenMap map)
    {
        map.Calls("Collision.RayIntersectsLineSegment(Ray&,Vector3F,Vector3F,Vector3F&)", i =>
        {
            var (ray, start, end) = (i.Ray<float>(), i.Vector3<float>(), i.Vector3<float>());
            return [Collision.RayIntersectsLineSegment(ray, start, end, out var point), ray, point];
        });
        map.Returns("Collision.ClosestPointPointTriangle(Vector3F,Vector3F,Vector3F,Vector3F)",
            i => Collision.ClosestPointPointTriangle(i.Vector3<float>(), i.Vector3<float>(), i.Vector3<float>(), i.Vector3<float>()));
        map.Returns("Collision.ClosestPointPlanePoint(Plane,Vector3F)", i => Collision.ClosestPointPlanePoint(i.Plane<float>(), i.Vector3<float>()));
        map.Returns("Collision.ClosestPointBoxPoint(BoundingBox,Vector3F)",
            i => Collision.ClosestPointBoxPoint(i.BoundingBox<float>(), i.Vector3<float>()));
        map.Returns("Collision.ClosestPointSpherePoint(BoundingSphere,Vector3F)",
            i => Collision.ClosestPointSpherePoint(i.BoundingSphere<float>(), i.Vector3<float>()));
        map.Returns("Collision.ClosestPointSphereSphere(BoundingSphere,BoundingSphere)",
            i => Collision.ClosestPointSphereSphere(i.BoundingSphere<float>(), i.BoundingSphere<float>()));
        map.Returns("Collision.DistancePlanePoint(Plane,Vector3F)", i => Collision.DistancePlanePoint(i.Plane<float>(), i.Vector3<float>()));
        map.Returns("Collision.DistanceBoxPoint(BoundingBox,Vector3F)", i => Collision.DistanceBoxPoint(i.BoundingBox<float>(), i.Vector3<float>()));
        map.Returns("Collision.DistanceBoxBox(BoundingBox,BoundingBox)",
            i => Collision.DistanceBoxBox(i.BoundingBox<float>(), i.BoundingBox<float>()));
        map.Returns("Collision.DistanceSpherePoint(BoundingSphere,Vector3F)",
            i => Collision.DistanceSpherePoint(i.BoundingSphere<float>(), i.Vector3<float>()));
        map.Returns("Collision.DistanceSphereSphere(BoundingSphere,BoundingSphere)",
            i => Collision.DistanceSphereSphere(i.BoundingSphere<float>(), i.BoundingSphere<float>()));
        RayTest(map, "RayIntersectsRay(Ray&,Ray&,Vector3F&)", i => i.Ray<float>(), 3, (ray, other) =>
            (Collision.RayIntersectsRay(ray, other, out var point), point));
        RayTest(map, "RayIntersectsPlane(Ray&,Plane&,Single&)", i => i.Plane<float>(), 1, (ray, plane) =>
            (Collision.RayIntersectsPlane(ray, plane, out float distance), distance));
        RayTest(map, "RayIntersectsPlane(Ray&,Plane&,Vector3F&)", i => i.Plane<float>(), 3, (ray, plane) =>
            (Collision.RayIntersectsPlane(ray, plane, out Vector3<float> point), point));
        RayTest(map, "RayIntersectsBox(Ray&,BoundingBox&,Single&)", i => i.BoundingBox<float>(), 1, (ray, box) =>
            (Collision.RayIntersectsBox(ray, box, out float distance), distance));
        RayTest(map, "RayIntersectsBox(Ray&,BoundingBox&,Vector3F&)", i => i.BoundingBox<float>(), 3, (ray, box) =>
            (Collision.RayIntersectsBox(ray, box, out Vector3<float> point), point));
        map.Calls("Collision.RayIntersectsTriangle(Ray&,Vector3F&,Vector3F&,Vector3F&,Single&)", i =>
        {
            var (ray, a, b, c) = (i.Ray<float>(), i.Vector3<float>(), i.Vector3<float>(), i.Vector3<float>());
            return [Collision.RayIntersectsTriangle(ray, a, b, c, out float distance), ray, a, b, c, distance];
        });
        map.Calls("Collision.RayIntersectsTriangle(Ray&,Vector3F&,Vector3F&,Vector3F&,Vector3F&)", i =>
        {
            var (ray, a, b, c) = (i.Ray<float>(), i.Vector3<float>(), i.Vector3<float>(), i.Vector3<float>());
            return [Collision.RayIntersectsTriangle(ray, a, b, c, out Vector3<float> point), ray, a, b, c, point];
        });
        map.Returns("Collision.PlaneIntersectsPoint(Plane,Vector3F)", i => Collision.PlaneIntersectsPoint(i.Plane<float>(), i.Vector3<float>()));
        map.Returns("Collision.PlaneIntersectsPlane(Plane,Plane)", i => Collision.PlaneIntersectsPlane(i.Plane<float>(), i.Plane<float>()));
        map.Calls("Collision.PlaneIntersectsPlane(Plane&,Plane&,Ray&)", i =>
        {
            var (plane1, plane2) = (i.Plane<float>(), i.Plane<float>());
            return [Collision.PlaneIntersectsPlane(plane1, plane2, out Ray<float> line), plane1, plane2, line];
        });
        map.Returns("Collision.PlaneIntersectsTriangle(Plane,Vector3F,Vector3F,Vector3F)",
            i => Collision.PlaneIntersectsTriangle(i.Plane<float>(), i.Vector3<float>(), i.Vector3<float>(), i.Vector3<float>()));
        map.Returns("Collision.PlaneIntersectsBox(Plane,BoundingBox)", i => Collision.PlaneIntersectsBox(i.Plane<float>(), i.BoundingBox<float>()));
        map.Returns("Collision.PlaneIntersectsSphere(Plane,BoundingSphere)",
            i => Collision.PlaneIntersectsSphere(i.Plane<float>(), i.BoundingSphere<float>()));
        map.Returns("Collision.BoxIntersectsBox(BoundingBox,BoundingBox)", i => Collision.BoxIntersectsBox(i.BoundingBox<float>(), i.BoundingBox<float>()));
        map.Returns("Collision.BoxIntersectsSphere(BoundingBox,BoundingSphere)",
            i => Collision.BoxIntersectsSphere(i.BoundingBox<float>(), i.BoundingSphere<float>()));
        map.Returns("Collision.SphereIntersectsTriangle(BoundingSphere,Vector3F,Vector3F,Vector3F)",
            i => Collision.SphereIntersectsTriangle(i.BoundingSphere<float>(), i.Vector3<float>(), i.Vector3<float>(), i.Vector3<float>()));
        map.Returns("Collision.SphereIntersectsSphere(BoundingSphere,BoundingSphere)",
            i => Collision.SphereIntersectsSphere(i.BoundingSphere<float>(), i.BoundingSphere<float>()));
        map.Returns("Collision.BoxContainsPoint(BoundingBox,Vector3F)", i => Collision.BoxContainsPoint(i.BoundingBox<float>(), i.Vector3<float>()));
        map.Returns("Collision.BoxContainsBox(BoundingBox,BoundingBox)", i => Collision.BoxContainsBox(i.BoundingBox<float>(), i.BoundingBox<float>()));
        map.Returns("Collision.BoxContainsSphere(BoundingBox,BoundingSphere)",
            i => Collision.BoxContainsSphere(i.BoundingBox<float>(), i.BoundingSphere<float>()));
        map.Returns("Collision.SphereContainsPoint(BoundingSphere,Vector3F)",
            i => Collision.SphereContainsPoint(i.BoundingSphere<float>(), i.Vector3<float>()));
        map.Returns("Collision.SphereContainsTriangle(BoundingSphere,Vector3F,Vector3F,Vector3F)",
            i => Collision.SphereContainsTriangle(i.BoundingSphere<float>(), i.Vector3<float>(), i.Vector3<float>(), i.Vector3<float>()));
        map.Returns("Collision.SphereContainsBox(BoundingSphere,BoundingBox)",
            i => Collision.SphereContainsBox(i.BoundingSphere<float>(), i.BoundingBox<float>()));
        map.Returns("Collision.SphereContainsSphere(BoundingSphere,BoundingSphere)",
            i => Collision.SphereContainsSphere(i.BoundingSphere<float>(), i.BoundingSphere<float>()));
        map.Returns("Collision.ClosestPointSegmentPoint(Vector3F,Vector3F,Vector3F)",
            i => Collision.ClosestPointSegmentPoint(i.Vector3<float>(), i.Vector3<float>(), i.Vector3<float>()));
        map.Returns("Collision.DistanceSquaredSegmentSegment(Vector3F,Vector3F,Vector3F,Vector3F)",
            i => Collision.DistanceSquaredSegmentSegment(i.Vector3<float>(), i.Vector3<float>(), i.Vector3<float>(), i.Vector3<float>()));
    }

    private static void RayTest<TOther>(GoldenMap map, string key, Func<GoldenInputs, TOther> read, int written,
        Func<Ray<float>, TOther, (bool Hit, object Answer)> test) =>
        map.Calls("Collision." + key, i =>
        {
            var (ray, other) = (i.Ray<float>(), read(i));
            var (hit, answer) = test(ray, other);
            return [hit, ray, other, answer];
        });

    private static void BoxWith<TOther>(GoldenMap map, string method, string other, Func<GoldenInputs, TOther> read,
        Func<BoundingBox<float>, TOther, object> test)
    {
        Instance(map, $"BoundingBox.{method}(this {other}&)", i => i.BoundingBox<float>(), (x, i) =>
        {
            var value = read(i);
            return [test(x, value), value];
        });
        Instance(map, $"BoundingBox.{method}(this {other})", i => i.BoundingBox<float>(), (x, i) => [test(x, read(i))]);
    }

    private static void SphereWith<TOther>(GoldenMap map, string method, string other, Func<GoldenInputs, TOther> read,
        Func<BoundingSphere<float>, TOther, object> test)
    {
        Instance(map, $"BoundingSphere.{method}(this {other}&)", i => i.BoundingSphere<float>(), (x, i) =>
        {
            var value = read(i);
            return [test(x, value), value];
        });
        Instance(map, $"BoundingSphere.{method}(this {other})", i => i.BoundingSphere<float>(), (x, i) => [test(x, read(i))]);
    }

    private static void Instance<TSelf>(GoldenMap map, string key, Func<GoldenInputs, TSelf> read, Func<TSelf, GoldenInputs, object[]> answer,
        bool selfAfter = true) =>
        map.Calls(key, i =>
        {
            var self = read(i);
            var answers = answer(self, i);
            return selfAfter ? [.. answers, self] : answers;
        });

    private static bool RaySphereOrPoint(string key) =>
        key.StartsWith("Collision.RayIntersectsSphere(") || key.StartsWith("Collision.RayIntersectsPoint(")
        || key.StartsWith("Ray.Intersects(this BoundingSphere") || key == "Ray.Intersects(this Vector3F&)"
        || key.StartsWith("BoundingSphere.Intersects(this Ray");

    private static bool RayOrPlaneHit(string key) =>
        key.StartsWith("Collision.RayIntersects") || key.StartsWith("Collision.PlaneIntersectsPlane(")
        || key.StartsWith("Ray.Intersects(") || key.StartsWith("Plane.Intersects(this Ray")
        || key.StartsWith("Plane.Intersects(this Plane") || key.StartsWith("BoundingBox.Intersects(this Ray");

    private static bool IsZero(double[] inputs, int at) => inputs.Length > at + 2 && inputs[at] == 0 && inputs[at + 1] == 0 && inputs[at + 2] == 0;

    private static bool Sheared(double[] inputs, int at)
    {
        double Dot(int row1, int row2) =>
            inputs[at + row1 * 4] * inputs[at + row2 * 4] + inputs[at + row1 * 4 + 1] * inputs[at + row2 * 4 + 1]
            + inputs[at + row1 * 4 + 2] * inputs[at + row2 * 4 + 2];

        var largest = Math.Max(Dot(0, 0), Math.Max(Dot(1, 1), Dot(2, 2)));
        return Math.Max(Math.Abs(Dot(0, 1)), Math.Max(Math.Abs(Dot(0, 2)), Math.Abs(Dot(1, 2)))) > 1e-5 * largest;
    }

    private static bool OldPlaneEquals(Plane<float> left, Plane<float> right) =>
        Vector3<float>.NearEqual(left.Normal, right.Normal) && left.D == right.D;

    private static bool OldSphereEquals(BoundingSphere<float> left, BoundingSphere<float> right) =>
        Vector3<float>.NearEqual(left.Center, right.Center) && left.Radius == right.Radius;

    private static object[] Self<TSelf>(TSelf self, TSelf other, Func<TSelf, TSelf, object> answer) => [answer(self, other), self];
}
