using System;
using System.Collections.Generic;
using Adamantium.Graphics.Core;
using Adamantium.Graphics.Core.Models;
using Adamantium.Mathematics;
using Adamantium.MVVM;

namespace Adamantium.ECS.Components;

/// <summary>
/// The convex hull of an entity's mesh: the tightest convex shape round it. The mesh builds the hull once, and every
/// entity drawing that mesh shares it. A mesh with no points leaves the collider empty, and nothing meets it. The hull
/// is fitted only; the collider's placement moves, turns and stretches it.
/// </summary>
[ViewModel]
public partial class ConvexHullCollider : Collider
{
    private readonly Dictionary<CameraBase, PlacedCorners> colliderData = new();
    private ConvexHull placedHull;
    private ConvexHull placedFrom;
    private Matrix4x4F placedBy;
    private Vector3F hullCenter;
    private double placedVolume;
    private Vector3F[] corners = [];

    /// <summary>The mesh's hull, in its entity's space as the mesh stands, or null while the collider is empty. The
    /// collider's placement carries it from its center, which a fit makes <see cref="Collider.Position"/>.</summary>
    [Bindable]
    public partial ConvexHull Hull { get; private set; }

    /// <summary>The hull in its entity's own space - <see cref="Hull"/> placed by the collider's placement; the mesh's
    /// own while the placement leaves it where it is.</summary>
    public ConvexHull PlacedHull => placedHull;

    partial void OnHullChanged(ConvexHull value) => hullCenter = value == null ? Vector3F.Zero : CenterOf(value);

    public override void ClearData()
    {
        colliderData.Clear();
    }

    public override bool ContainsDataFor(CameraBase camera)
    {
        return colliderData.ContainsKey(camera);
    }

    public override void UpdateForCamera(CameraBase camera)
    {
        if (!colliderData.TryGetValue(camera, out var placed))
        {
            placed = new PlacedCorners();
            colliderData[camera] = placed;
        }

        placed.World = Owner.Transform.GetMetadata(camera).WorldMatrixF;
        if (placed.Corners.Length != corners.Length)
        {
            placed.Corners = new Vector3F[corners.Length];
        }

        for (int i = 0; i < corners.Length; i++)
        {
            placed.Corners[i] = Vector3F.TransformCoordinate(corners[i], placed.World);
        }
    }

    public override ContainmentType IsInsideCameraFrustum(Camera camera)
    {
        if (placedHull == null)
        {
            return ContainmentType.Disjoint;
        }

        return camera.Frustum.Contains(colliderData.TryGetValue(camera, out var placed) ? placed.Corners : corners);
    }

    /// <summary>
    /// Grows the hull to take in <paramref name="collider"/> of this entity or of one under it, as it stands here: its
    /// hull if it has one, else the corners of its bounds. The grown hull stands in its entity's space, unplaced.
    /// </summary>
    public override void Merge(Collider collider)
    {
        ArgumentNullException.ThrowIfNull(collider);
        var placement = PlacementOf(collider.Owner);
        List<Vector3F> points = [.. corners];
        if (collider is ConvexHullCollider { PlacedHull: { } other })
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

        BeginFit();
        try
        {
            Scale = Vector3F.One;
            TakeUnplaced(ConvexHull.FromPoints(points));
        }
        finally
        {
            EndFit();
        }
    }

    public override bool Intersects(ref Ray<float> ray, out Vector3F point)
    {
        point = Vector3F.Zero;
        return placedHull != null && placedHull.Intersects(ray, out point);
    }

    public override bool IntersectsForCamera(Camera camera, ref Ray<float> ray, out Vector3F point)
    {
        point = Vector3F.Zero;
        if (placedHull == null || !colliderData.TryGetValue(camera, out var placed))
        {
            return false;
        }

        var inverse = Matrix4x4F.Invert(placed.World);
        var local = new Ray<float>(Vector3F.TransformCoordinate(ray.Position, inverse), Vector3F.TransformNormal(ray.Direction, inverse));
        if (!placedHull.Intersects(local, out float distance))
        {
            return false;
        }

        point = ray.Position + ray.Direction * distance;
        return true;
    }

    // The scale stretches all the hull's space alike; the hull's own volume is measured once, when it is placed.
    public override double MeasureVolume(Vector3F scale)
    {
        var size = Vector3F.Abs(scale);
        return placedVolume * size.X * size.Y * size.Z;
    }

    public override double MeasureCarriedVolume(double stretch) => placedVolume * stretch;

    public override IConvexShape<float> GetWorldShape(Vector3<double> origin)
    {
        return placedHull?.Transform(Owner.Transform.GetWorldMatrixF(origin));
    }

    protected override void FitShape(Mesh mesh) => TakeUnplaced(mesh.HasPoints ? mesh.GetConvexHull() : null);

    // Placed by hand, the hull is still the mesh's.
    protected override void TakeMeshShape(Mesh mesh) => Hull = mesh.HasPoints ? mesh.GetConvexHull() : null;

    protected override void FitBox(Bounds box)
    {
        Span<Vector3<double>> boxCorners = stackalloc Vector3<double>[OrientedBoundingBox<double>.CornerCount];
        box.GetCorners(boxCorners);
        var points = new Vector3F[boxCorners.Length];
        for (int i = 0; i < points.Length; i++)
        {
            points[i] = boxCorners[i].As<float>();
        }

        TakeUnplaced(ConvexHull.FromPoints(points));
    }

    // A hull has no sizes of its own: only its placement is set by hand.
    protected override void WriteSizes(ColliderShapeEdits shape)
    {
    }

    protected override void TakeSizes(ColliderShapeEdits shape)
    {
    }

    protected override Bounds PlaceShape()
    {
        // From the hull's center by the placement; the same hull placed the same keeps the hull it made.
        var carry = Matrix4x4.CreateTranslation<float>(-hullCenter) * Placement;
        if (Hull != null && ReferenceEquals(Hull, placedFrom) && carry.Equals(placedBy))
        {
            return Bounds;
        }

        placedFrom = Hull;
        placedBy = carry;
        placedHull = Hull == null ? null : carry.Equals(Matrix4x4F.Identity) ? Hull : Hull.Transform(carry);
        placedVolume = placedHull?.MeasureVolume() ?? 0;
        MarkOutlineStale();
        corners = [];
        if (placedHull == null)
        {
            return default;
        }

        corners = new Vector3F[placedHull.Vertices.Count];
        for (int i = 0; i < corners.Length; i++)
        {
            corners[i] = placedHull.Vertices[i];
        }

        return BoundsOf(placedHull.GetBoundingBox());
    }

    protected override Mesh BuildOutline()
    {
        List<Vector3<double>> lines = [];
        var hull = placedHull;
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

    private static Vector3F CenterOf(ConvexHull hull)
    {
        var box = hull.GetBoundingBox();
        return (box.Minimum + box.Maximum) * 0.5f;
    }

    // A hull already standing where it should in its entity's space: placed at its own center, it stays there.
    private void TakeUnplaced(ConvexHull hull)
    {
        Hull = hull;
        Position = hullCenter;
        Rotation = QuaternionF.Identity;
    }
}
