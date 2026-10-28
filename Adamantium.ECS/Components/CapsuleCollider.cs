using System;
using System.Collections.Generic;
using Adamantium.Graphics.Core.Models;
using Adamantium.Mathematics;
using Adamantium.MVVM;
using Adamantium.ProceduralGeometry;
using Adamantium.ProceduralGeometry.Shapes;

namespace Adamantium.ECS.Components;

/// <summary>
/// A capsule round an entity: a segment along <see cref="Direction"/> through the middle of its mesh, rounded at both
/// ends. Fitted to the mesh when added: along the mesh's longest side, as thick as the wider of the other two.
/// </summary>
[ViewModel]
public partial class CapsuleCollider : Collider
{
    private readonly Dictionary<CameraBase, BoundingCapsule> colliderData = new();
    private BoundingCapsule capsule;

    public CapsuleCollider()
    {
        Direction = CapsuleDirection.Y;
    }

    [Bindable]
    public partial CapsuleDirection Direction { get; set; }

    [Bindable]
    public partial float Radius { get; set; }

    /// <summary>End to end, the rounded ends included; a height below the diameter counts as the diameter.</summary>
    [Bindable]
    public partial float Height { get; set; }

    partial void OnDirectionChanged(CapsuleDirection value) => Rebuild();

    partial void OnRadiusChanged(float value) => Rebuild();

    partial void OnHeightChanged(float value) => Rebuild();

    /// <summary>The capsule in its entity's own space.</summary>
    public BoundingCapsule Capsule => capsule;

    public override void CalculateFromMesh(Mesh mesh)
    {
        base.CalculateFromMesh(mesh);
        var size = Bounds.Size.As<float>();
        Direction = size.X >= size.Y && size.X >= size.Z ? CapsuleDirection.X
            : size.Y >= size.Z ? CapsuleDirection.Y
            : CapsuleDirection.Z;
        Fit(size);
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
        var turn = Direction == CapsuleDirection.X ? Matrix4x4.CreateRotationZ<double>(float.DegreesToRadians(-90))
            : Direction == CapsuleDirection.Z ? Matrix4x4.CreateRotationX<double>(float.DegreesToRadians(90))
            : Matrix4x4<double>.Identity;
        Geometry = Shapes.Capsule.GenerateGeometry(GeometryType.Outlined, capsule.Length, Radius * 2, 40, turn * Matrix4x4.CreateTranslation<double>(capsule.Center.As<double>()));
        return Geometry;
    }

    public override void UpdateForCamera(CameraBase camera)
    {
        colliderData[camera] = capsule.Transform(Owner.Transform.GetMetadata(camera).WorldMatrixF);
    }

    public override ContainmentType IsInsideCameraFrustum(Camera camera)
    {
        var placed = colliderData.TryGetValue(camera, out var data) ? data : capsule;
        return placed.Within(camera.Frustum);
    }

    public override void Transform(ref Vector3F scale, ref QuaternionF rotation, ref Vector3F translation)
    {
        capsule = capsule.Transform(Matrix4x4.CreateScale<float>(scale) * Matrix4x4.CreateFromQuaternion<float>(rotation) * Matrix4x4.CreateTranslation<float>(translation));
    }

    public override void Transform(ref float uniformScale, ref QuaternionF rotation, ref Vector3F translation)
    {
        capsule = capsule.Transform(Matrix4x4.CreateScale<float>(uniformScale) * Matrix4x4.CreateFromQuaternion<float>(rotation) * Matrix4x4.CreateTranslation<float>(translation));
    }

    /// <summary>
    /// Grows the capsule, along the same direction, to take in <paramref name="collider"/> of this entity or of one
    /// under it, as it stands here.
    /// </summary>
    public override void Merge(Collider collider)
    {
        Span<Vector3F> corners = stackalloc Vector3F[2 * OrientedBoundingBox<float>.CornerCount];
        capsule.GetBoundingBox().GetCorners(corners[..OrientedBoundingBox<float>.CornerCount]);
        collider.Bounds.ToOrientedBoundingBox().As<float>().Transform(PlacementOf(collider.Owner))
            .GetCorners(corners[OrientedBoundingBox<float>.CornerCount..]);
        var merged = OrientedBoundingBox.CreateFromPoints<float>(corners);
        Bounds = Bounds.FromBoundingBox(merged.As<double>());
        Fit(merged.Size);
    }

    public override bool Intersects(ref Ray<float> ray, out Vector3F point)
    {
        return capsule.Intersects(ray, out point);
    }

    public override bool IntersectsForCamera(Camera camera, ref Ray<float> ray, out Vector3F point)
    {
        point = Vector3F.Zero;
        return colliderData.TryGetValue(camera, out var placed) && placed.Intersects(ray, out point);
    }

    public override IConvexShape<float> GetWorldShape()
    {
        return capsule.Transform(Owner.Transform.GetWorldMatrixF());
    }

    private void Fit(Vector3F size)
    {
        var along = Direction == CapsuleDirection.X ? size.X : Direction == CapsuleDirection.Y ? size.Y : size.Z;
        var across = Direction == CapsuleDirection.X ? Math.Max(size.Y, size.Z)
            : Direction == CapsuleDirection.Y ? Math.Max(size.X, size.Z)
            : Math.Max(size.X, size.Y);
        Radius = across / 2;
        Height = Math.Max(along, across);
        Rebuild();
    }

    private void Rebuild()
    {
        var axis = Direction == CapsuleDirection.X ? Vector3F.UnitX : Direction == CapsuleDirection.Y ? Vector3F.UnitY : Vector3F.UnitZ;
        var half = Math.Max(Height / 2 - Radius, 0);
        var center = LocalCenter;
        capsule = new BoundingCapsule(center - axis * half, center + axis * half, Radius);
    }
}
