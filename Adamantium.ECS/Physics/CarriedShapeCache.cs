using System;
using System.Collections.Generic;
using Adamantium.DarkMatter.Shapes;
using Adamantium.Graphics.Core.Models;
using Adamantium.Mathematics;

namespace Adamantium.ECS.Physics;

/// <summary>The shapes of a game's bodies made from hulls and meshes, one for each hull or mesh and carry they share,
/// kept while a body uses it.</summary>
internal sealed class CarriedShapeCache
{
    private readonly Dictionary<CarriedShapeKey, CarriedShapeUse> _uses = new();

    /// <summary>The shape of <paramref name="hull"/> carried by <paramref name="carry"/>, made on first use; its key goes
    /// in <paramref name="takenBy"/>, to give back with <see cref="GiveBack"/>.</summary>
    public ConvexHullCollisionShape Take(ConvexHull hull, in Matrix4x4F carry, float convexRadius,
        List<CarriedShapeKey> takenBy)
    {
        var key = new CarriedShapeKey(hull, 0, default, KeepSixDigits(carry), convexRadius);
        if (!_uses.TryGetValue(key, out var use))
        {
            // A hull carried is still a hull: carried as it is, not hulled anew from its corners.
            use = new CarriedShapeUse(new ConvexHullCollisionShape(hull.Transform(carry), convexRadius));
            _uses[key] = use;
        }

        Use(key, use, takenBy);
        return (ConvexHullCollisionShape)use.Shape;
    }

    /// <summary>The surface of <paramref name="mesh"/>'s triangles carried by <paramref name="carry"/>, made on first
    /// use and again once the mesh changes; its key goes in <paramref name="takenBy"/>.</summary>
    public TriangleMeshCollisionShape Take(Mesh mesh, in Matrix4x4F carry, List<CarriedShapeKey> takenBy)
    {
        var key = new CarriedShapeKey(mesh, mesh.Version, mesh.MeshTopology, KeepSixDigits(carry), 0);
        if (!_uses.TryGetValue(key, out var use))
        {
            var points = new Vector3F[mesh.Points.Length];
            for (var index = 0; index < points.Length; index++)
            {
                points[index] = Vector3F.TransformCoordinate(mesh.Points[index].As<float>(), carry);
            }

            // A mirroring carry turns every triangle over; wound back, the surface still tells its convex edges.
            var triangles = mesh.GatherTriangles();
            if (carry.Determinant() < 0)
            {
                for (var index = 0; index < triangles.Length; index += 3)
                {
                    var second = triangles[index + 1];
                    triangles[index + 1] = triangles[index + 2];
                    triangles[index + 2] = second;
                }
            }

            use = new CarriedShapeUse(new TriangleMeshCollisionShape(points, triangles));
            _uses[key] = use;
        }

        Use(key, use, takenBy);
        return (TriangleMeshCollisionShape)use.Shape;
    }

    /// <summary>Gives back the shapes <paramref name="taken"/> names; one no body uses any more is let go.</summary>
    public void GiveBack(List<CarriedShapeKey> taken)
    {
        foreach (var key in taken)
        {
            if (_uses.TryGetValue(key, out var use) && --use.Users == 0)
                _uses.Remove(key);
        }

        taken.Clear();
    }

    public void Clear() => _uses.Clear();

    private static void Use(CarriedShapeKey key, CarriedShapeUse use, List<CarriedShapeKey> takenBy)
    {
        use.Users++;
        takenBy.Add(key);
    }

    // A carry read back from a turned entity's matrix differs in its last digits: rounded for the key, copies share the
    // shape the first of them made.
    private static Matrix4x4F KeepSixDigits(Matrix4x4F carry)
    {
        for (var index = 0; index < 16; index++)
        {
            carry[index] = KeepSixDigits(carry[index]);
        }

        return carry;
    }

    private static float KeepSixDigits(float value)
    {
        if (value == 0 || !float.IsFinite(value))
            return value;

        var unit = Math.Pow(10, Math.Floor(Math.Log10(Math.Abs(value))) - 5);
        return (float)(Math.Round(value / unit) * unit);
    }
}
