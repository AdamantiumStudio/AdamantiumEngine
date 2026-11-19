using System;
using System.Collections.Generic;
using System.Runtime.InteropServices;
using Adamantium.Graphics.Core;
using Adamantium.Graphics.Core.Models;
using Adamantium.Mathematics;
using Adamantium.MVVM;

namespace Adamantium.ECS.Components;

/// <summary>
/// The convex hull of an entity's mesh: the tightest convex shape round it. The mesh builds the hull once, and every
/// entity drawing that mesh shares it. A mesh with no points leaves the collider empty, and nothing meets it.
/// </summary>
[ViewModel]
public partial class ConvexHullCollider : Collider
{
    private readonly Dictionary<CameraBase, Placement> colliderData = new();
    private Vector3F[] corners = [];

    /// <summary>The hull in its entity's own space, or null while the collider is empty; a new one tells what follows
    /// the collider's shape - the physics.</summary>
    [Bindable]
    public partial ConvexHull Hull { get; private set; }

    partial void OnHullChanged(ConvexHull value) => MarkOutlineStale();

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

    protected override Mesh BuildOutline()
    {
        List<Vector3<double>> lines = [];
        var hull = Hull;
        if (hull != null)
        {
            // An edge is kept once, by its two corners: the lower in the high half.
            var edges = new HashSet<long>();
            for (int i = 0; i < hull.Triangles.Count; i += 3)
            {
                for (int k = 0; k < 3; k++)
                {
                    var from = hull.Triangles[i + k];
                    var to = hull.Triangles[i + (k + 1) % 3];
                    if (edges.Add(((long)Math.Min(from, to) << 32) | (uint)Math.Max(from, to)))
                    {
                        lines.Add(hull.Vertices[from].As<double>());
                        lines.Add(hull.Vertices[to].As<double>());
                    }
                }
            }
        }

        return new Mesh(PrimitiveType.LineList).SetPoints(lines);
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
        if (Hull == null)
        {
            return ContainmentType.Disjoint;
        }

        return camera.Frustum.Contains(colliderData.TryGetValue(camera, out var placement) ? placement.Corners : corners);
    }

    public override void Transform(ref Vector3F scale, ref QuaternionF rotation, ref Vector3F translation)
    {
        Take(Hull?.Transform(Matrix4x4.CreateScale<float>(scale) * Matrix4x4.CreateFromQuaternion<float>(rotation) * Matrix4x4.CreateTranslation<float>(translation)));
    }

    public override void Transform(ref float uniformScale, ref QuaternionF rotation, ref Vector3F translation)
    {
        Take(Hull?.Transform(Matrix4x4.CreateScale<float>(uniformScale) * Matrix4x4.CreateFromQuaternion<float>(rotation) * Matrix4x4.CreateTranslation<float>(translation)));
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
        return Hull != null && Hull.Intersects(ray, out point);
    }

    public override bool IntersectsForCamera(Camera camera, ref Ray<float> ray, out Vector3F point)
    {
        point = Vector3F.Zero;
        if (Hull == null || !colliderData.TryGetValue(camera, out var placement))
        {
            return false;
        }

        var inverse = Matrix4x4F.Invert(placement.World);
        var local = new Ray<float>(Vector3F.TransformCoordinate(ray.Position, inverse), Vector3F.TransformNormal(ray.Direction, inverse));
        if (!Hull.Intersects(local, out float distance))
        {
            return false;
        }

        point = ray.Position + ray.Direction * distance;
        return true;
    }

    public override IConvexShape<float> GetWorldShape()
    {
        return Hull?.Transform(Owner.Transform.GetWorldMatrixF());
    }

    private void Take(ConvexHull taken)
    {
        corners = [];
        if (taken != null)
        {
            corners = new Vector3F[taken.Vertices.Count];
            for (int i = 0; i < corners.Length; i++)
            {
                corners[i] = taken.Vertices[i];
            }
        }

        Hull = taken;
    }

    private sealed class Placement
    {
        public Matrix4x4F World { get; set; }

        public Vector3F[] Corners { get; set; } = [];
    }
}
