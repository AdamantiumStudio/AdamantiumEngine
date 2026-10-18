using System;
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
    /// <param name="aperture">How close a line has to pass, in pixels.</param>
    /// <param name="withDisabled">Whether a disabled entity and what is under it are picked too.</param>
    public static PickHit Pick(this Entity root, in PickRay ray, PickMode mode, float aperture = 0, bool withDisabled = false)
    {
        Validate(mode);
        var nearest = default(PickHit);
        PickTree(root, in ray, mode, aperture, withDisabled, ref nearest);
        return nearest;
    }

    /// <param name="aperture">How close a line has to pass, in pixels.</param>
    /// <param name="withDisabled">Whether a disabled entity and what is under it are picked too.</param>
    public static PickHit Pick(IReadOnlyList<Entity> roots, in PickRay ray, PickMode mode, float aperture = 0, bool withDisabled = false)
    {
        Validate(mode);
        var nearest = default(PickHit);
        for (int i = 0; i < roots.Count; i++)
        {
            PickTree(roots[i], in ray, mode, aperture, withDisabled, ref nearest);
        }

        return nearest;
    }

    /// <summary>
    /// Where a collider placed by <paramref name="world"/> meets the ray, for a shape that is not an entity of its own.
    /// </summary>
    public static bool PickCollider(Collider collider, in Matrix4x4F world, in Ray ray, out Vector3F point, out float depth)
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

    private static void PickTree(Entity entity, in PickRay ray, PickMode mode, float aperture, bool withDisabled, ref PickHit nearest)
    {
        if (!entity.Visible || (!entity.IsEnabled && !withDisabled))
        {
            return;
        }

        if (!entity.IgnoreInCollisionDetection)
        {
            PickEntity(entity, in ray, mode, aperture, ref nearest);
        }

        var children = entity.Dependencies;
        for (int i = 0; i < children.Count; i++)
        {
            PickTree(children[i], in ray, mode, aperture, withDisabled, ref nearest);
        }
    }

    private static void PickEntity(Entity entity, in PickRay ray, PickMode mode, float aperture, ref PickHit nearest)
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
            PickTriangles(entity, mesh, in world, ray.Ray, ref nearest);
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

    private static void PickTriangles(Entity entity, Mesh mesh, in Matrix4x4F world, in Ray ray, ref PickHit nearest)
    {
        if (!ToLocal(in ray, in world, out var local))
        {
            return;
        }

        BoundingBox bounds = mesh.Bounds;
        if (!Collision.RayIntersectsBox(ref local, ref bounds, out float _))
        {
            return;
        }

        if (mesh.GetTriangleTree().Intersect(local, out var distance))
        {
            nearest = PickHit.Nearest(nearest, new PickHit(entity, ray.Position + ray.Direction * distance, distance));
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

            var start = Vector3F.TransformCoordinate((Vector3F)points[a], world);
            var end = Vector3F.TransformCoordinate((Vector3F)points[b], world);
            var gap = Collision.RayIntersectsLineSegment(ref ray, start, end, out var point);
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
        var center = Vector3F.TransformCoordinate((Vector3F)bounds.Center, world);
        var radius = ((Vector3F)bounds.HalfExtent).Length() * MaxScale(in world);
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

    private static bool ToLocal(in Ray ray, in Matrix4x4F world, out Ray local)
    {
        var matrix = world;
        Matrix4x4F.Invert(ref matrix, out var inverse);
        if (inverse == Matrix4x4F.Zero)
        {
            local = default;
            return false;
        }

        local = new Ray(Vector3F.TransformCoordinate(ray.Position, inverse), Vector3F.TransformNormal(ray.Direction, inverse));
        return true;
    }
}
