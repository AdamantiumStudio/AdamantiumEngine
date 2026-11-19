using System;
using System.Collections.Generic;
using Adamantium.Graphics.Core;
using Adamantium.Graphics.Core.Models;
using Adamantium.Mathematics;
using Adamantium.MVVM;

namespace Adamantium.ECS.Components;

/// <summary>
/// An entity's mesh itself as its collider, triangle by triangle: a level, a building, a rock face. It holds no volume,
/// so only a body that does not move by forces - static or kinematic - collides with it; picking meets its triangles.
/// The collider's placement carries the mesh from its center, which a fit makes <see cref="Collider.Position"/>.
/// </summary>
[ViewModel]
public partial class MeshCollider : Collider
{
    private readonly Dictionary<CameraBase, PlacedCorners> colliderData = new();
    private Vector3F meshCenter;
    private Mesh placedFrom;
    private int placedVersion;
    private Matrix4x4F placedBy;
    private Matrix4x4F carry = Matrix4x4F.Identity;
    private Vector3F[] corners = [];

    /// <summary>The mesh, as its entity draws it, or null while the collider is empty.</summary>
    [Bindable]
    public partial Mesh Source { get; private set; }

    /// <summary>How <see cref="Source"/> is carried from its own space to where the collider stands in its entity's:
    /// from its center by the collider's placement.</summary>
    public Matrix4x4F Carry => carry;

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
        if (Source == null)
        {
            return ContainmentType.Disjoint;
        }

        return camera.Frustum.Contains(colliderData.TryGetValue(camera, out var placed) ? placed.Corners : corners);
    }

    /// <summary>A mesh collider holds its mesh: it does not grow round other colliders.</summary>
    public override void Merge(Collider collider) =>
        throw new NotSupportedException("A mesh collider holds its mesh; it does not grow round other colliders.");

    public override bool Intersects(ref Ray<float> ray, out Vector3F point)
    {
        return IntersectsCarried(ray, carry, out point);
    }

    public override bool IntersectsForCamera(Camera camera, ref Ray<float> ray, out Vector3F point)
    {
        point = Vector3F.Zero;
        return colliderData.TryGetValue(camera, out var placed) && IntersectsCarried(ray, carry * placed.World, out point);
    }

    /// <summary>A surface holds no space: a mesh collider adds nothing to its body's weight.</summary>
    public override double MeasureVolume(Vector3F scale) => 0;

    /// <summary>The box round the carried mesh where its entity stands: a mesh is no convex shape to test.</summary>
    public override IConvexShape<float> GetWorldShape()
    {
        if (Source == null)
        {
            return null;
        }

        var box = Source.Bounds;
        return new OrientedBoundingBox<float>(box.Center.As<float>(), box.HalfExtent.As<float>(), box.Orientation.As<float>())
            .Transform(carry * Owner.Transform.GetWorldMatrixF());
    }

    protected override void FitShape(Mesh mesh) => TakeUnplaced(HoldsTriangles(mesh) ? mesh : null);

    // Placed by hand, the collider still holds the mesh its entity draws.
    protected override void TakeMeshShape(Mesh mesh) => Hold(HoldsTriangles(mesh) ? mesh : null);

    // Only Merge fits to a box, and a mesh collider does not merge.
    protected override void FitBox(Bounds box) => TakeUnplaced(null);

    // A mesh has no sizes of its own: only its placement is set by hand.
    protected override void WriteSizes(ColliderShapeEdits shape)
    {
    }

    protected override void TakeSizes(ColliderShapeEdits shape)
    {
    }

    protected override Bounds PlaceShape()
    {
        // A mesh is edited in place: its version tells it changed, though it is the same mesh.
        var placement = Matrix4x4.CreateTranslation<float>(-meshCenter) * Placement;
        if (Source != null && ReferenceEquals(Source, placedFrom) && Source.Version == placedVersion
            && placement.Equals(placedBy))
        {
            return Bounds;
        }

        placedFrom = Source;
        placedVersion = Source?.Version ?? 0;
        placedBy = placement;
        carry = placement;
        MarkOutlineStale();
        corners = [];
        if (Source == null)
        {
            return default;
        }

        Span<Vector3<double>> boxCorners = stackalloc Vector3<double>[OrientedBoundingBox<double>.CornerCount];
        Source.Bounds.GetCorners(boxCorners);
        corners = new Vector3F[boxCorners.Length];
        var minimum = new Vector3F(float.MaxValue);
        var maximum = new Vector3F(float.MinValue);
        for (int i = 0; i < corners.Length; i++)
        {
            corners[i] = Vector3F.TransformCoordinate(boxCorners[i].As<float>(), carry);
            minimum = Vector3F.Min(minimum, corners[i]);
            maximum = Vector3F.Max(maximum, corners[i]);
        }

        return BoundsOf(new BoundingBox<float>(minimum, maximum));
    }

    protected override Mesh BuildOutline()
    {
        List<Vector3<double>> lines = [];
        var mesh = Source;
        if (mesh != null)
        {
            var triangles = mesh.GatherTriangles();
            var points = mesh.Points;
            // An edge is kept once, by its two corners: the lower in the high half.
            var edges = new HashSet<long>();
            for (int i = 0; i < triangles.Length; i += 3)
            {
                for (int k = 0; k < 3; k++)
                {
                    var from = triangles[i + k];
                    var to = triangles[i + (k + 1) % 3];
                    if (edges.Add(((long)Math.Min(from, to) << 32) | (uint)Math.Max(from, to)))
                    {
                        lines.Add(Vector3F.TransformCoordinate(points[from].As<float>(), carry).As<double>());
                        lines.Add(Vector3F.TransformCoordinate(points[to].As<float>(), carry).As<double>());
                    }
                }
            }
        }

        return new Mesh(PrimitiveType.LineList).SetPoints(lines);
    }

    // Lines and points give no surface to collide with.
    private static bool HoldsTriangles(Mesh mesh) =>
        mesh.HasPoints && (mesh.MeshTopology == PrimitiveType.TriangleList || mesh.MeshTopology == PrimitiveType.TriangleStrip);

    // Its center is taken anew with it, a mesh edited in place too: the placement carries the mesh from where its
    // center is now.
    private void Hold(Mesh mesh)
    {
        Source = mesh;
        meshCenter = mesh == null ? Vector3F.Zero : mesh.Bounds.Center.As<float>();
    }

    // The mesh as it stands in its entity's space: placed at its own center, it stays there.
    private void TakeUnplaced(Mesh mesh)
    {
        Hold(mesh);
        Position = meshCenter;
        Rotation = QuaternionF.Identity;
    }

    // A ray carried into the mesh's own space keeps its lengths along it, so the distance the mesh's triangles give
    // places the point on the ray as it was.
    private bool IntersectsCarried(Ray<float> ray, Matrix4x4F toWorld, out Vector3F point)
    {
        point = Vector3F.Zero;
        if (Source == null)
        {
            return false;
        }

        var inverse = Matrix4x4F.Invert(toWorld);
        var local = new Ray<float>(Vector3F.TransformCoordinate(ray.Position, inverse),
            Vector3F.TransformNormal(ray.Direction, inverse));
        if (!Source.GetTriangleTree().Intersect(local, out var distance))
        {
            return false;
        }

        point = ray.Position + ray.Direction * distance;
        return true;
    }
}
