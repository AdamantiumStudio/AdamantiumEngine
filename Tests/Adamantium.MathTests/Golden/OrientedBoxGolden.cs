using System;
using Adamantium.Mathematics;

namespace Adamantium.MathTests.Golden;

/// <summary>The recorded operations of the oriented box, and those of the other volumes taking one, answered by
/// <see cref="OrientedBoundingBox{T}"/>. The old box was recorded as its center, half extent, orientation, then a
/// minimum and a maximum it kept beside them - its center less and plus its half extent, along the world's axes - which
/// the new box has not: they are read past, echoed where the old echoed them, and made from the new box where it gave
/// one.</summary>
public static class OrientedBoxGolden
{
    private const string O = "OrientedBoundingBox";

    /// <summary>Adds the operations of OrientedBoundingBox, and those of the other volumes taking one, to
    /// <paramref name="map"/>.</summary>
    public static void Register(GoldenMap map)
    {
        Instance(map, $"{O}.get_Size(this )", (x, _) => [x.Size]);
        Instance(map, $"{O}.Support(this Vector3F)", (x, i) => [x.Support(i.Vector3<float>())]);
        Instance(map, $"{O}.GetBoundingBox(this )", (x, _) => [x.GetBoundingBox()]);
        Instance(map, $"{O}.ConvertToFrustum(this )", (x, _) =>
        {
            var frustum = x.ToFrustum();
            return [frustum.ViewProjection, frustum.EnableFarPlaneCheck];
        });
        Instance(map, $"{O}.Contains(this Vector3F&)", (x, i) =>
        {
            var point = i.Vector3<float>();
            return [x.Contains(point) == Mathematics.ContainmentType.Contains, point];
        });
        Instance(map, $"{O}.ContainsLine(this Vector3F&,Vector3F&)", (x, i) =>
        {
            var (start, end) = (i.Vector3<float>(), i.Vector3<float>());
            return [x.Contains(start, end), start, end];
        });
        Instance(map, $"{O}.Contains(this BoundingBox&)", (x, i) =>
        {
            var box = i.BoundingBox<float>();
            return [x.Contains(box), box];
        });
        Instance(map, $"{O}.Intersects(this BoundingBox&)", (x, i) =>
        {
            var box = i.BoundingBox<float>();
            return [x.Intersects(box), box];
        });
        Instance(map, $"{O}.Contains(this {O}&)", (x, i) =>
        {
            var other = Read(i);
            return [x.Contains(other.Box), .. other.Echo];
        });
        Instance(map, $"{O}.Intersects(this {O}&)", (x, i) =>
        {
            var other = Read(i);
            return [x.Intersects(other.Box), .. other.Echo];
        });
        Instance(map, $"{O}.Contains(this BoundingSphere&)", (x, i) =>
        {
            var sphere = i.BoundingSphere<float>();
            return [x.Contains(sphere), sphere];
        });
        Instance(map, $"{O}.Intersects(this BoundingSphere&)", (x, i) =>
        {
            var sphere = i.BoundingSphere<float>();
            return [x.Intersects(sphere), sphere];
        });
        Instance(map, $"{O}.Contains(this BoundingFrustum)", (x, i) => [x.Contains(i.BoundingFrustum<float>())]);
        Instance(map, $"{O}.Intersects(this BoundingFrustum)", (x, i) => [x.Intersects(i.BoundingFrustum<float>())]);
        Instance(map, $"{O}.Intersects(this Plane&)", (x, i) =>
        {
            var plane = i.Plane<float>();
            return [x.Intersects(plane), plane];
        });
        Instance(map, $"{O}.Intersects(this Ray&,Single&)", (x, i) =>
        {
            var ray = i.Ray<float>();
            var hit = x.Intersects(ray, out float distance);
            return [hit, ray, distance];
        });
        Instance(map, $"{O}.Intersects(this Ray&,Vector3F&)", (x, i) =>
        {
            var ray = i.Ray<float>();
            var hit = x.Intersects(ray, out Vector3<float> point);
            return [hit, ray, point];
        });
        Instance(map, $"{O}.Equals(this {O})", (x, i) => [x.Equals(Read(i).Box)]);
        map.Returns($"{O}.op_Equality({O},{O})", i => Read(i).Box == Read(i).Box);
        map.Returns($"{O}.op_Inequality({O},{O})", i => Read(i).Box != Read(i).Box);
        map.Calls($"{O}.FromBoundingBox(BoundingBox)", i => Old(OrientedBoundingBox.CreateFromBox(i.BoundingBox<float>())));
        map.Calls($"{O}.Merge({O}&,{O}&)", i =>
        {
            var (first, second) = (Read(i), Read(i));
            return [.. Old(OrientedBoundingBox.CreateMerged(first.Box, second.Box)), .. first.Echo, .. second.Echo];
        });
        map.Calls($"{O}.Contains(BoundingBox&,{O}&)", i =>
        {
            var (box, oriented) = (i.BoundingBox<float>(), Read(i));
            return [box.Contains(oriented.Box), box, .. oriented.Echo];
        });
        map.Calls($"{O}.Contains(BoundingSphere&,{O}&)", i =>
        {
            var (sphere, oriented) = (i.BoundingSphere<float>(), Read(i));
            return [sphere.Contains(oriented.Box), sphere, .. oriented.Echo];
        });
        map.Calls($"{O}.Contains(BoundingFrustum,{O}&)", i =>
        {
            var (frustum, oriented) = (i.BoundingFrustum<float>(), Read(i));
            return [frustum.Contains(oriented.Box), .. oriented.Echo];
        });
        map.NotMapped($"{O}.ContainsRelativeBox(Vector3F&,Vector3F&,Matrix4x4F&)",
            "the old box's helper for its separating axes, public by mistake; private in the new");
        RegisterOthers(map);
        RegisterChanges(map);
    }

    private static void RegisterChanges(GoldenMap map)
    {
        map.NotMapped($"{O}.Transform(this QuaternionF,Vector3F)",
            "old bug: the turns were composed the wrong way round - the box's own turn after the new one - so the box " +
            "went elsewhere than its carried corners; TypedOrientedBoxTests checks the corners");
        map.NotMapped($"{O}.Transform(this Single,QuaternionF,Vector3F)",
            "old bug: the turns composed the wrong way round, and a negative scale gave negative half extents; " +
            "TypedOrientedBoxTests checks the corners");
        map.NotMapped($"{O}.Transform(this Vector3F,QuaternionF,Vector3F)",
            "old bug: the scale along the world's axes was laid along the box's own, and the turns composed the wrong " +
            "way round; now the smallest box along the carried axes holding the carried one - TypedOrientedBoxTests " +
            "checks it holds every carried corner");
        map.NotMapped($"{O}.Transform(this Matrix4x4F)",
            "old bug: axes a shear left askew were taken as square, so the box no longer held the carried one - every " +
            "recorded matrix shears; TypedOrientedBoxTests checks the new is exact without a shear and holds every " +
            "carried corner with one");
        map.ExactWhereOldWasNear(O, (a, b) => OrientedBoundingBox<float>.NearEqual(Read(new GoldenInputs(a)).Box, Read(new GoldenInputs(b)).Box));
        map.ChangedWhere(TakesABox, inputs => Array.Exists(inputs, double.IsNaN),
            "old bug: a NaN counted as a hit, or as inside or crossing; now it is outside");
        map.ChangedWhere(key => TakesABox(key) && key.Contains("Ray"),
            inputs => Array.TrueForAll(inputs, double.IsFinite) && Array.Exists(inputs, value => Math.Abs(value) > 1e15),
            "old bug: products of 4e30 overflowed float into NaN, a hit at distance NaN; the new answers as double does " +
            "- TypedOrientedBoxTests checks rays far away against double");
        map.ChangedWhere(key => key is "BoundingBox.Intersects(this OrientedBoundingBox&)", inputs => Inverted(inputs, 0),
            "an axis-aligned box turned inside out holds nothing now; the old took it as a box of the absolute size");
        map.ChangedWhere(key => key.StartsWith($"{O}.Intersects(this BoundingBox") || key.StartsWith($"{O}.Contains(this BoundingBox"),
            inputs => Inverted(inputs, 16), "an axis-aligned box turned inside out holds nothing now");
        map.ChangedWhere(key => key.StartsWith($"{O}.Intersects(this BoundingSphere"), Touching,
            "a sphere touching the box meets it now, as everywhere else; the old took touching as apart");
        map.ChangedWhere(key => key.StartsWith($"{O}.ContainsLine("), EndOnTheSurface,
            "old bug: an end on the surface was inside for Contains of one point but outside for the segment");
        map.ChangedWhere(key => key is $"BoundingFrustum.Contains(this {O}&)" or $"{O}.Contains(BoundingFrustum,{O}&)",
            inputs => FrustumTouchesFromInside(inputs) || Array.TrueForAll(inputs[..16], value => value == 0),
            "a box touching a plane from inside is inside now, as a sphere is; the zero matrix sees nothing now");
        map.ChangedWhere(key => key is $"{O}.Contains(this BoundingFrustum)" or $"{O}.Intersects(this BoundingFrustum)",
            inputs =>
            {
                var read = new GoldenInputs(inputs);
                var box = Read(read).Box;
                var frustum = read.BoundingFrustum<float>();
                return frustum.Contains(box) == Mathematics.ContainmentType.Disjoint
                       && box.Contains(frustum) == Mathematics.ContainmentType.Disjoint
                       && box.ToFrustum().Contains(frustum) != Mathematics.ContainmentType.Disjoint;
            },
            "old bug: only the box's faces were tried as separating, so a frustum whose own plane leaves the box wholly " +
            "behind it counted as crossing; the new tries the frustum's planes too");
        map.ChangedWhere(key => key is $"{O}.Contains(this BoundingFrustum)" or $"{O}.Intersects(this BoundingFrustum)",
            inputs => Array.TrueForAll(inputs[16..32], value => value == 0), "the zero matrix sees nothing now");
        map.ChangedWhere(key => key == $"{O}.ConvertToFrustum(this )", inputs => inputs[3] < 0 || inputs[4] < 0 || inputs[5] < 0,
            "a box of a half extent below zero holds nothing, so its frustum sees nothing; the old gave infinities and NaN");
    }

    private static bool TakesABox(string key) => key.Contains(O);

    private static bool Inverted(double[] inputs, int start) =>
        inputs[start] > inputs[start + 3] || inputs[start + 1] > inputs[start + 4] || inputs[start + 2] > inputs[start + 5];

    private static bool Touching(double[] inputs)
    {
        var read = new GoldenInputs(inputs);
        var (box, sphere) = (Read(read).Box, read.BoundingSphere<float>());
        var local = Local(box, sphere.Center);
        var nearest = Vector3<float>.Clamp(local, -box.HalfExtent, box.HalfExtent);
        return Vector3<float>.Distance(nearest, local) == sphere.Radius;
    }

    private static bool EndOnTheSurface(double[] inputs)
    {
        var read = new GoldenInputs(inputs);
        var box = Read(read).Box;
        return OnTheSurface(box, read.Vector3<float>()) || OnTheSurface(box, read.Vector3<float>());
    }

    private static bool OnTheSurface(OrientedBoundingBox<float> box, Vector3<float> point)
    {
        var local = Vector3<float>.Abs(Local(box, point));
        return local.X <= box.HalfExtent.X && local.Y <= box.HalfExtent.Y && local.Z <= box.HalfExtent.Z
               && (local.X == box.HalfExtent.X || local.Y == box.HalfExtent.Y || local.Z == box.HalfExtent.Z);
    }

    private static Vector3<float> Local(OrientedBoundingBox<float> box, Vector3<float> point)
    {
        var (axes, offset) = (Matrix3x3.CreateFromQuaternion(box.Orientation), point - box.Center);
        return new Vector3<float>(Vector3<float>.Dot(offset, axes.Row1), Vector3<float>.Dot(offset, axes.Row2), Vector3<float>.Dot(offset, axes.Row3));
    }

    private static bool FrustumTouchesFromInside(double[] inputs)
    {
        var read = new GoldenInputs(inputs);
        var (frustum, box) = (read.BoundingFrustum<float>(), Read(read).Box);
        var axes = Matrix3x3.CreateFromQuaternion(box.Orientation);
        Plane<float>[] planes = [frustum.Near, frustum.Far, frustum.Left, frustum.Right, frustum.Top, frustum.Bottom];
        return frustum.Contains(box) == Mathematics.ContainmentType.Contains && Array.Exists(planes, plane =>
            Plane<float>.DotCoordinate(plane, box.Center) == box.HalfExtent.X * MathF.Abs(Vector3<float>.Dot(plane.Normal, axes.Row1))
            + box.HalfExtent.Y * MathF.Abs(Vector3<float>.Dot(plane.Normal, axes.Row2)) + box.HalfExtent.Z * MathF.Abs(Vector3<float>.Dot(plane.Normal, axes.Row3)));
    }

    private static void RegisterOthers(GoldenMap map)
    {
        map.Calls($"Plane.Intersects(this {O}&)", i =>
        {
            var (plane, box) = (i.Plane<float>(), Read(i));
            return [plane.Intersects(box.Box), .. box.Echo, plane];
        });
        map.Calls($"Ray.Intersects(this {O}&,Single&)", i =>
        {
            var (ray, box) = (i.Ray<float>(), Read(i));
            var hit = ray.Intersects(box.Box, out float distance);
            return [hit, .. box.Echo, distance, ray];
        });
        map.Calls($"BoundingBox.Intersects(this {O}&)", i =>
        {
            var (self, box) = (i.BoundingBox<float>(), Read(i));
            return [self.Intersects(box.Box), .. box.Echo, self];
        });
        map.Calls($"BoundingSphere.Intersects(this {O}&)", i =>
        {
            var (sphere, box) = (i.BoundingSphere<float>(), Read(i));
            return [sphere.Intersects(box.Box), .. box.Echo, sphere];
        });
        map.Calls($"BoundingSphere.Intersects(this {O})", i =>
        {
            var (sphere, box) = (i.BoundingSphere<float>(), Read(i));
            return [sphere.Intersects(box.Box), sphere];
        });
        map.Calls($"BoundingSphere.Contains(this {O}&)", i =>
        {
            var (sphere, box) = (i.BoundingSphere<float>(), Read(i));
            return [sphere.Contains(box.Box), .. box.Echo, sphere];
        });
        map.Calls($"Collision.BoxIntersectsSphere({O}&,BoundingSphere&)", i =>
        {
            var (box, sphere) = (Read(i), i.BoundingSphere<float>());
            return [box.Box.Intersects(sphere), .. box.Echo, sphere];
        });
    }

    /// <summary>The next sixteen numbers as an old box: the new box, and the numbers to echo where the old echoed
    /// it.</summary>
    internal static (OrientedBoundingBox<float> Box, object[] Echo) Read(GoldenInputs inputs)
    {
        var box = new OrientedBoundingBox<float>(inputs.Vector3<float>(), inputs.Vector3<float>(), inputs.Quaternion<float>());
        var (minimum, maximum) = (inputs.Vector3<float>(), inputs.Vector3<float>());
        return (box, [box.Center, box.HalfExtent, box.Orientation, minimum, maximum]);
    }

    private static object[] Old(OrientedBoundingBox<float> box) =>
        [box.Center, box.HalfExtent, box.Orientation, box.Center - box.HalfExtent, box.Center + box.HalfExtent];

    private static void Instance(GoldenMap map, string key, Func<OrientedBoundingBox<float>, GoldenInputs, object[]> answer) =>
        map.Calls(key, i =>
        {
            var self = Read(i);
            return [.. answer(self.Box, i), .. self.Echo];
        });
}
