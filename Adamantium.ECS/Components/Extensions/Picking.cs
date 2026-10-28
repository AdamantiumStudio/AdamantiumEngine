using System;
using System.Buffers;
using System.Collections.Generic;
using Adamantium.Graphics.Core;
using Adamantium.Graphics.Core.Models;
using Adamantium.Mathematics;

namespace Adamantium.ECS.Components.Extensions;

/// <summary>
/// Finds what a ray hits: the nearest hit along it, over an entity and everything under it. A hidden entity is skipped
/// with its subtree, and so is a disabled one unless asked for, as an editor picks what is switched off.
/// </summary>
public static class Picking
{
    private static readonly Comparer<TriangleCandidate> NearestEntryFirst =
        Comparer<TriangleCandidate>.Create((a, b) => a.Entry.CompareTo(b.Entry));

    /// <param name="aperture">How close a line has to pass, in pixels.</param>
    /// <param name="withDisabled">Whether a disabled entity and what is under it are picked too.</param>
    public static PickHit Pick(this Entity root, in PickRay ray, PickMode mode, float aperture = 0, bool withDisabled = false)
    {
        Validate(mode);
        var nearest = default(PickHit);
        var candidates = new Candidates();
        try
        {
            PickTree(root, in ray, mode, aperture, withDisabled, ref nearest, ref candidates);
            return PickTriangles(candidates.AsSpan(), in ray, nearest);
        }
        finally
        {
            candidates.Return();
        }
    }

    /// <param name="aperture">How close a line has to pass, in pixels.</param>
    /// <param name="withDisabled">Whether a disabled entity and what is under it are picked too.</param>
    public static PickHit Pick(IReadOnlyList<Entity> roots, in PickRay ray, PickMode mode, float aperture = 0, bool withDisabled = false)
    {
        Validate(mode);
        var nearest = default(PickHit);
        var candidates = new Candidates();
        try
        {
            for (int i = 0; i < roots.Count; i++)
            {
                PickTree(roots[i], in ray, mode, aperture, withDisabled, ref nearest, ref candidates);
            }

            return PickTriangles(candidates.AsSpan(), in ray, nearest);
        }
        finally
        {
            candidates.Return();
        }
    }

    /// <summary>
    /// The nearest triangle of a mesh <paramref name="ray"/> hits, asking <paramref name="index"/> for the meshes whose
    /// boxes it passes through instead of walking every entity. Skips what <see cref="Pick(Entity, in PickRay, PickMode,
    /// float, bool)"/> skips: a hidden entity or one under a hidden one, a disabled one unless asked for, one left out of
    /// collision detection.
    /// </summary>
    public static PickHit PickMeshes(SceneIndex index, in PickRay ray, bool withDisabled = false)
    {
        var candidates = new Candidates();
        try
        {
            var origin = ray.Ray.Position + (ray.Camera?.WorldPosition ?? Vector3<double>.Zero).As<float>();
            foreach (var (entity, _) in index.RayCast(new Ray<float>(origin, ray.Ray.Direction)))
            {
                if (!Pickable(entity, withDisabled))
                {
                    continue;
                }

                var transform = entity.Transform.GetMetadata(ray.Camera);
                var mesh = entity.GetComponent<MeshData>() is { IsEnabled: true } data ? data.Mesh : null;
                if (transform.Enabled && mesh is { HasPoints: true } && IsTriangles(mesh.MeshTopology))
                {
                    var world = transform.WorldMatrixF;
                    AddTriangles(entity, mesh, Skin.DrawnOn(entity), in world, in ray, ref candidates);
                }
            }

            return PickTriangles(candidates.AsSpan(), in ray, default);
        }
        finally
        {
            candidates.Return();
        }
    }

    private static bool Pickable(Entity entity, bool withDisabled)
    {
        if (entity.IgnoreInCollisionDetection)
        {
            return false;
        }

        for (var at = entity; at != null; at = at.Owner)
        {
            if (!at.Visible || (!at.IsEnabled && !withDisabled))
            {
                return false;
            }
        }

        return true;
    }

    /// <summary>
    /// Where a collider placed by <paramref name="world"/> meets the ray, for a shape that is not an entity of its own.
    /// </summary>
    public static bool PickCollider(Collider collider, in Matrix4x4F world, in Ray<float> ray, out Vector3F point, out float depth)
    {
        point = Vector3F.Zero;
        depth = 0;
        if (!ToLocal(in ray, in world, out var local))
        {
            return false;
        }

        local.Direction = Vector3F.Normalize(local.Direction);
        if (!collider.Intersects(ref local, out var localPoint))
        {
            return false;
        }

        point = Vector3F.TransformCoordinate(localPoint, world);
        depth = Vector3F.Dot(point - ray.Position, ray.Direction);
        return depth >= 0;
    }

    private static void Validate(PickMode mode)
    {
        if ((mode & PickMode.Colliders) != 0 && (mode & PickMode.MeshColliders) != 0)
        {
            throw new ArgumentException("Colliders already include the colliders of meshes: ask for one of the two.", nameof(mode));
        }
    }

    private static void PickTree(Entity entity, in PickRay ray, PickMode mode, float aperture, bool withDisabled,
        ref PickHit nearest, ref Candidates candidates)
    {
        if (!entity.Visible || (!entity.IsEnabled && !withDisabled))
        {
            return;
        }

        if (!entity.IgnoreInCollisionDetection)
        {
            PickEntity(entity, in ray, mode, aperture, ref nearest, ref candidates);
        }

        foreach (var child in entity.Dependencies.AsSpan())
        {
            if (child != null)
            {
                PickTree(child, in ray, mode, aperture, withDisabled, ref nearest, ref candidates);
            }
        }
    }

    private static void PickEntity(Entity entity, in PickRay ray, PickMode mode, float aperture, ref PickHit nearest,
        ref Candidates candidates)
    {
        var transform = entity.Transform.GetMetadata(ray.Camera);
        if (!transform.Enabled)
        {
            return;
        }

        var world = transform.WorldMatrixF;
        var meshData = entity.GetComponent<MeshData>();
        var mesh = meshData is { IsEnabled: true } ? meshData.Mesh : null;

        var collider = entity.GetComponent<Collider>();
        var testCollider = (mode & PickMode.Colliders) != 0 || ((mode & PickMode.MeshColliders) != 0 && mesh != null);
        if (collider is { IsEnabled: true } && testCollider
            && PickCollider(collider, in world, ray.Ray, out var point, out var depth))
        {
            nearest = PickHit.Nearest(nearest, new PickHit(entity, point, depth));
        }

        if (mesh == null || !mesh.HasPoints)
        {
            return;
        }

        if ((mode & PickMode.Triangles) != 0 && IsTriangles(mesh.MeshTopology))
        {
            AddTriangles(entity, mesh, Skin.DrawnOn(entity), in world, in ray, ref candidates);
        }

        if ((mode & PickMode.Lines) != 0 && IsLines(mesh.MeshTopology))
        {
            PickLines(entity, mesh, in world, in ray, aperture, ref nearest);
        }
    }

    private static bool IsTriangles(PrimitiveType topology)
    {
        return topology == PrimitiveType.TriangleList || topology == PrimitiveType.TriangleStrip;
    }

    private static bool IsLines(PrimitiveType topology)
    {
        return topology == PrimitiveType.LineList || topology == PrimitiveType.LineStrip;
    }

    private static void AddTriangles(Entity entity, Mesh mesh, Skin skin, in Matrix4x4F world, in PickRay pick,
        ref Candidates candidates)
    {
        var ray = pick.Ray;
        if (!ToLocal(in ray, in world, out var local))
        {
            return;
        }

        var bounds = skin != null ? skin.PosedBounds(mesh, pick.Camera) : mesh.Bounds.ToBoundingBox().As<float>();
        if (Collision.RayIntersectsBox(local, bounds, out float entry))
        {
            candidates.Add(new TriangleCandidate(entity, mesh, skin, local, entry));
        }
    }

    private static PickHit PickTriangles(Span<TriangleCandidate> candidates, in PickRay pick, PickHit nearest)
    {
        var ray = pick.Ray;
        candidates.Sort(NearestEntryFirst);
        foreach (var (entity, mesh, skin, local, entry) in candidates)
        {
            if (nearest.IsHit && entry > nearest.Depth)
            {
                break;
            }

            var hit = skin != null
                ? skin.Intersect(mesh, local, pick.Camera, out var distance)
                : mesh.GetTriangleTree().Intersect(local, out distance);
            if (hit)
            {
                nearest = PickHit.Nearest(nearest, new PickHit(entity, ray.Position + ray.Direction * distance, distance));
            }
        }

        return nearest;
    }

    private readonly record struct TriangleCandidate(Entity Entity, Mesh Mesh, Skin Skin, Ray<float> Local, float Entry);

    private struct Candidates
    {
        private TriangleCandidate[] items;
        private int count;

        public void Add(in TriangleCandidate candidate)
        {
            if (items == null || count == items.Length)
            {
                var larger = ArrayPool<TriangleCandidate>.Shared.Rent(Math.Max(16, count * 2));
                if (items != null)
                {
                    items.AsSpan(0, count).CopyTo(larger);
                    ArrayPool<TriangleCandidate>.Shared.Return(items, clearArray: true);
                }

                items = larger;
            }

            items[count++] = candidate;
        }

        public readonly Span<TriangleCandidate> AsSpan() => items == null ? default : items.AsSpan(0, count);

        public void Return()
        {
            if (items == null)
            {
                return;
            }

            ArrayPool<TriangleCandidate>.Shared.Return(items, clearArray: true);
            items = null;
            count = 0;
        }
    }

    private static void PickLines(Entity entity, Mesh mesh, in Matrix4x4F world, in PickRay pick, float aperture, ref PickHit nearest)
    {
        var ray = pick.Ray;
        if (!MayPassNear(mesh.Bounds, in world, in pick, aperture))
        {
            return;
        }

        var points = mesh.Points;
        var indices = mesh.Indices;
        var count = mesh.HasIndices ? indices.Length : points.Length;
        var strip = mesh.MeshTopology == PrimitiveType.LineStrip;
        var step = strip ? 1 : 2;

        for (int i = 0; i + 1 < count; i += step)
        {
            var a = mesh.HasIndices ? indices[i] : i;
            var b = mesh.HasIndices ? indices[i + 1] : i + 1;
            if (a < 0 || b < 0)
            {
                continue;
            }

            var start = Vector3F.TransformCoordinate(points[a].As<float>(), world);
            var end = Vector3F.TransformCoordinate(points[b].As<float>(), world);
            var gap = Collision.RayIntersectsLineSegment(ray, start, end, out var point);
            var depth = Vector3F.Dot(point - ray.Position, ray.Direction);
            if (depth >= 0 && gap <= aperture * pick.PixelSize(depth))
            {
                nearest = PickHit.Nearest(nearest, new PickHit(entity, point, depth));
            }
        }
    }

    private static bool MayPassNear(Bounds bounds, in Matrix4x4F world, in PickRay pick, float aperture)
    {
        var ray = pick.Ray;
        var center = Vector3F.TransformCoordinate(bounds.Center.As<float>(), world);
        var radius = (bounds.HalfExtent.As<float>()).Length() * MaxScale(in world);
        var along = Vector3F.Dot(center - ray.Position, ray.Direction);
        if (along + radius < 0)
        {
            return false;
        }

        var closest = ray.Position + ray.Direction * along;
        var reach = radius + aperture * pick.PixelSize(Math.Max(along + radius, 0));
        return Vector3F.DistanceSquared(center, closest) <= reach * reach;
    }

    private static float MaxScale(in Matrix4x4F world)
    {
        var x = new Vector3F(world.M11, world.M12, world.M13).Length();
        var y = new Vector3F(world.M21, world.M22, world.M23).Length();
        var z = new Vector3F(world.M31, world.M32, world.M33).Length();
        return Math.Max(x, Math.Max(y, z));
    }

    private static bool ToLocal(in Ray<float> ray, in Matrix4x4F world, out Ray<float> local)
    {
        var inverse = Matrix4x4F.Invert(world);
        if (inverse == Matrix4x4F.Zero)
        {
            local = default;
            return false;
        }

        local = new Ray<float>(Vector3F.TransformCoordinate(ray.Position, inverse), Vector3F.TransformNormal(ray.Direction, inverse));
        return true;
    }
}
