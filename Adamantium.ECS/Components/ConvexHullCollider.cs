using System;
using System.Collections.Generic;
using System.Runtime.InteropServices;
using Adamantium.Graphics.Core;
using Adamantium.Graphics.Core.Models;
using Adamantium.Mathematics;

namespace Adamantium.ECS.Components;

/// <summary>
/// The convex hull of an entity's mesh: the tightest convex shape round it. The mesh builds the hull once, and every
/// entity drawing that mesh shares it. A mesh with no points leaves the collider empty, and nothing meets it.
/// </summary>
public class ConvexHullCollider : Collider
{
    private readonly Dictionary<CameraBase, Placement> colliderData = new();
    private ConvexHull hull;
    private Vector3F[] corners = [];

    /// <summary>The hull in its entity's own space, or null while the collider is empty.</summary>
    public ConvexHull Hull => hull;

    public override void CalculateFromMesh(Mesh mesh)
    {
        base.CalculateFromMesh(mesh);
        Take(mesh.HasPoints ? mesh.GetConvexHull() : null);
    }

    public override void ClearData()
    {
        colliderData.Clear();
    }

    public override bool ContainsDataFor(CameraBase camera)
    {
        return colliderData.ContainsKey(camera);
    }

    public override Mesh GetVisualRepresentation()
    {
        List<Vector3<double>> lines = [];
        if (hull != null)
        {
            var edges = new HashSet<(int, int)>();
            for (int i = 0; i < hull.Triangles.Count; i += 3)
            {
                for (int k = 0; k < 3; k++)
                {
                    var a = hull.Triangles[i + k];
                    var b = hull.Triangles[i + (k + 1) % 3];
                    if (edges.Add(a < b ? (a, b) : (b, a)))
                    {
                        lines.Add(hull.Vertices[a].As<double>());
                        lines.Add(hull.Vertices[b].As<double>());
                    }
                }
            }
        }

        Geometry = new Mesh(PrimitiveType.LineList).SetPoints(lines);
        return Geometry;
    }

    public override void UpdateForCamera(CameraBase camera)
    {
        if (!colliderData.TryGetValue(camera, out var placement))
        {
            placement = new Placement();
            colliderData[camera] = placement;
        }

        placement.World = Owner.Transform.GetMetadata(camera).WorldMatrixF;
        if (placement.Corners.Length != corners.Length)
        {
            placement.Corners = new Vector3F[corners.Length];
        }

        for (int i = 0; i < corners.Length; i++)
        {
            placement.Corners[i] = Vector3F.TransformCoordinate(corners[i], placement.World);
        }
    }

    public override ContainmentType IsInsideCameraFrustum(Camera camera)
    {
        if (hull == null)
        {
            return ContainmentType.Disjoint;
        }

        return camera.Frustum.Contains(colliderData.TryGetValue(camera, out var placement) ? placement.Corners : corners);
    }

    public override void Transform(ref Vector3F scale, ref QuaternionF rotation, ref Vector3F translation)
    {
        Take(hull?.Transform(Matrix4x4.CreateScale<float>(scale) * Matrix4x4.CreateFromQuaternion<float>(rotation) * Matrix4x4.CreateTranslation<float>(translation)));
    }

    public override void Transform(ref float uniformScale, ref QuaternionF rotation, ref Vector3F translation)
    {
        Take(hull?.Transform(Matrix4x4.CreateScale<float>(uniformScale) * Matrix4x4.CreateFromQuaternion<float>(rotation) * Matrix4x4.CreateTranslation<float>(translation)));
    }

    /// <summary>
    /// Grows the hull to take in <paramref name="collider"/> of this entity or of one under it, as it stands here: its
    /// hull if it has one, else the corners of its bounds.
    /// </summary>
    public override void Merge(Collider collider)
    {
        var placement = PlacementOf(collider.Owner);
        List<Vector3F> points = [.. corners];
        if (collider is ConvexHullCollider { Hull: { } other })
        {
            foreach (var vertex in other.Vertices)
            {
                points.Add(Vector3F.TransformCoordinate(vertex, placement));
            }
        }
        else
        {
            Span<Vector3F> part = stackalloc Vector3F[OrientedBoundingBox<float>.CornerCount];
            collider.Bounds.ToOrientedBoundingBox().As<float>().Transform(placement).GetCorners(part);
            points.AddRange(part);
        }

        Take(ConvexHull.FromPoints(points));
        Bounds = Bounds.FromBoundingBox(OrientedBoundingBox.CreateFromPoints<float>(CollectionsMarshal.AsSpan(points)).As<double>());
    }

    public override bool Intersects(ref Ray<float> ray, out Vector3F point)
    {
        point = Vector3F.Zero;
        return hull != null && hull.Intersects(ray, out point);
    }

    public override bool IntersectsForCamera(Camera camera, ref Ray<float> ray, out Vector3F point)
    {
        point = Vector3F.Zero;
        if (hull == null || !colliderData.TryGetValue(camera, out var placement))
        {
            return false;
        }

        var inverse = Matrix4x4F.Invert(placement.World);
        var local = new Ray<float>(Vector3F.TransformCoordinate(ray.Position, inverse), Vector3F.TransformNormal(ray.Direction, inverse));
        if (!hull.Intersects(local, out float distance))
        {
            return false;
        }

        point = ray.Position + ray.Direction * distance;
        return true;
    }

    public override IConvexShape<float> GetWorldShape()
    {
        return hull?.Transform(Owner.Transform.GetWorldMatrixF());
    }

    private void Take(ConvexHull taken)
    {
        hull = taken;
        corners = [];
        if (hull == null)
        {
            return;
        }

        corners = new Vector3F[hull.Vertices.Count];
        for (int i = 0; i < corners.Length; i++)
        {
            corners[i] = hull.Vertices[i];
        }
    }

    private sealed class Placement
    {
        public Matrix4x4F World { get; set; }

        public Vector3F[] Corners { get; set; } = [];
    }
}
