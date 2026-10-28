using System;
using Adamantium.Mathematics;

namespace Adamantium.MathTests.Golden;

/// <summary>The recorded operations of the frustum, answered by <see cref="BoundingFrustum{T}"/>. A frustum was
/// recorded as its view-projection matrix and whether it checks its far plane; an instance method answers what it
/// returns, then each argument by reference, then the frustum.</summary>
public static class FrustumGolden
{
    private const string F = "BoundingFrustum";

    /// <summary>Adds the operations of BoundingFrustum to <paramref name="map"/>.</summary>
    public static void Register(GoldenMap map)
    {
        Instance(map, $"{F}.get_ViewProjection(this )", (x, _) => [x.ViewProjection]);
        Instance(map, $"{F}.get_EnableFarPlaneCheck(this )", (x, _) => [x.EnableFarPlaneCheck]);
        Instance(map, $"{F}.get_Near(this )", (x, _) => [x.Near]);
        Instance(map, $"{F}.get_Far(this )", (x, _) => [x.Far]);
        Instance(map, $"{F}.get_Left(this )", (x, _) => [x.Left]);
        Instance(map, $"{F}.get_Right(this )", (x, _) => [x.Right]);
        Instance(map, $"{F}.get_Top(this )", (x, _) => [x.Top]);
        Instance(map, $"{F}.get_Bottom(this )", (x, _) => [x.Bottom]);
        Instance(map, $"{F}.Support(this Vector3F)", (x, i) => [x.Support(i.Vector3<float>())]);
        Instance(map, $"{F}.Contains(this Vector3F&)", (x, i) =>
        {
            var point = i.Vector3<float>();
            return [x.Contains(point), point];
        });
        Instance(map, $"{F}.Contains(this BoundingBox)", (x, i) => [x.Contains(i.BoundingBox<float>())]);
        Instance(map, $"{F}.Contains(this BoundingBox&,ContainmentType&)", (x, i) =>
        {
            var box = i.BoundingBox<float>();
            return [box, x.Contains(box)];
        });
        Instance(map, $"{F}.Intersects(this BoundingBox)", (x, i) => [x.Intersects(i.BoundingBox<float>())]);
        Instance(map, $"{F}.Intersects(this BoundingBox&,Boolean&)", (x, i) =>
        {
            var box = i.BoundingBox<float>();
            return [box, x.Intersects(box)];
        });
        Instance(map, $"{F}.Contains(this BoundingSphere)", (x, i) => [x.Contains(i.BoundingSphere<float>())]);
        Instance(map, $"{F}.Contains(this BoundingSphere&,ContainmentType&)", (x, i) =>
        {
            var sphere = i.BoundingSphere<float>();
            return [sphere, x.Contains(sphere)];
        });
        Instance(map, $"{F}.Intersects(this BoundingSphere)", (x, i) => [x.Intersects(i.BoundingSphere<float>())]);
        Instance(map, $"{F}.Intersects(this BoundingSphere&,Boolean&)", (x, i) =>
        {
            var sphere = i.BoundingSphere<float>();
            return [sphere, x.Intersects(sphere)];
        });
        Instance(map, $"{F}.Contains(this OrientedBoundingBox&)", (x, i) =>
        {
            var box = OrientedBoxGolden.Read(i);
            return [x.Contains(box.Box), .. box.Echo];
        });
        Instance(map, $"{F}.Intersects(this OrientedBoundingBox&)", (x, i) =>
        {
            var box = OrientedBoxGolden.Read(i);
            return [x.Intersects(box.Box), .. box.Echo];
        });
        Instance(map, $"{F}.Contains(this BoundingFrustum)", (x, i) => [x.Contains(i.BoundingFrustum<float>())]);
        Instance(map, $"{F}.Intersects(this BoundingFrustum)", (x, i) => [x.Intersects(i.BoundingFrustum<float>())]);
        Instance(map, $"{F}.Intersects(this Plane)", (x, i) => [x.Intersects(i.Plane<float>())]);
        Instance(map, $"{F}.Intersects(this Plane&,PlaneIntersectionType&)", (x, i) =>
        {
            var plane = i.Plane<float>();
            return [plane, x.Intersects(plane)];
        });
        Instance(map, $"{F}.Equals(this BoundingFrustum)", (x, i) => [x.Equals(i.BoundingFrustum<float>())]);
        map.Returns($"{F}.op_Equality({F},{F})", i => i.BoundingFrustum<float>() == i.BoundingFrustum<float>());
        map.Returns($"{F}.op_Inequality({F},{F})", i => i.BoundingFrustum<float>() != i.BoundingFrustum<float>());
        map.ExactWhereOldWasNear(F, (a, b) => Matrix4x4<float>.NearEqual(new GoldenInputs(a).Matrix4x4<float>(), new GoldenInputs(b).Matrix4x4<float>()));
        map.ChangedWhere(IsFrustum, inputs => Array.Exists(inputs, double.IsNaN),
            "old bug: a NaN counted as inside or crossing, or a NaN corner as the farthest; now it is outside");
        map.ChangedWhere(IsFrustum, inputs => HasPlaneWithoutNormal(inputs, 0) || HasPlaneWithoutNormal(inputs, 17),
            "old bug: the zero matrix's planes were NaN, so points were outside it while boxes were inside and spheres " +
            "crossed it; now it sees nothing, its planes without a normal or a distance");
        map.ChangedWhere(key => IsFrustum(key) && !key.StartsWith($"{F}.Intersects(this Plane"),
            inputs => Array.TrueForAll(inputs, double.IsFinite) && (Large(inputs, 0) || Large(inputs, 17)),
            "old bug: a plane of 4e30 coefficients overflowed float into NaN as it was scaled to a normal of length one; " +
            "the new scales the matrix first - TypedFrustumTests checks such frustums against double");
        map.ChangedWhere(key => key.StartsWith($"{F}.Contains(this BoundingSphere") || key.StartsWith($"{F}.Intersects(this BoundingSphere"),
            TouchesFromInside,
            "a sphere touching a plane from inside - every point of it in the frustum - crossed the surface; now it is " +
            "inside, as its points are");
    }

    private static bool IsFrustum(string key) => key.StartsWith($"{F}.");

    private static bool Large(double[] inputs, int start) =>
        inputs.Length >= start + 17 && Array.Exists(inputs[start..(start + 16)], value => Math.Abs(value) > 1e15);

    private static bool HasPlaneWithoutNormal(double[] inputs, int start)
    {
        if (inputs.Length < start + 17)
        {
            return false;
        }

        var frustum = new GoldenInputs(inputs[start..]).BoundingFrustum<float>();
        return Array.Exists([frustum.Near, frustum.Far, frustum.Left, frustum.Right, frustum.Top, frustum.Bottom],
            plane => plane.Normal == Vector3<float>.Zero);
    }

    private static bool TouchesFromInside(double[] inputs)
    {
        var read = new GoldenInputs(inputs);
        var (frustum, sphere) = (read.BoundingFrustum<float>(), read.BoundingSphere<float>());
        Plane<float>[] checkedPlanes = frustum.EnableFarPlaneCheck
            ? [frustum.Near, frustum.Far, frustum.Left, frustum.Right, frustum.Top, frustum.Bottom]
            : [frustum.Near, frustum.Left, frustum.Right, frustum.Top, frustum.Bottom];
        return frustum.Contains(sphere) == Mathematics.ContainmentType.Contains
               && Array.Exists(checkedPlanes, plane => Plane<float>.DotCoordinate(plane, sphere.Center) == sphere.Radius);
    }

    private static void Instance(GoldenMap map, string key, Func<BoundingFrustum<float>, GoldenInputs, object[]> answer) =>
        map.Calls(key, i =>
        {
            var self = i.BoundingFrustum<float>();
            return [.. answer(self, i), self.ViewProjection, self.EnableFarPlaneCheck];
        });
}
