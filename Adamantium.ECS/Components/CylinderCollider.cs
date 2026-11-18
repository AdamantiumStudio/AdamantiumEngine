using System;
using System.Collections.Generic;
using Adamantium.Graphics.Core.Models;
using Adamantium.Mathematics;
using Adamantium.MVVM;
using Adamantium.ProceduralGeometry;
using Adamantium.ProceduralGeometry.Shapes;

namespace Adamantium.ECS.Components;

/// <summary>A cylinder round an entity's mesh, fitted along the side whose cross-section is roundest: a wheel's thin
/// side, a barrel's height.</summary>
[ViewModel]
public partial class CylinderCollider : Collider
{
    private readonly Dictionary<CameraBase, BoundingCylinder> colliderData = new();
    private BoundingCylinder cylinder;

    public CylinderCollider()
    {
        Axis = ColliderAxis.Y;
        ChoosesAxis = true;
    }

    /// <summary>Whether fitting to a mesh chooses the axis; false keeps <see cref="Axis"/> for a shape known to stand
    /// so.</summary>
    [Bindable]
    public partial bool ChoosesAxis { get; set; }

    [Bindable]
    public partial ColliderAxis Axis { get; set; }

    [Bindable]
    public partial float Radius { get; set; }

    /// <summary>End to end.</summary>
    [Bindable]
    public partial float Height { get; set; }

    partial void OnAxisChanged(ColliderAxis value) => Rebuild();

    partial void OnRadiusChanged(float value) => Rebuild();

    partial void OnHeightChanged(float value) => Rebuild();

    /// <summary>The cylinder in its entity's own space.</summary>
    public BoundingCylinder Cylinder => cylinder;

    public override void CalculateFromMesh(Mesh mesh)
    {
        base.CalculateFromMesh(mesh);
        var size = Bounds.Size.As<float>();
        if (ChoosesAxis)
        {
            var acrossX = Math.Abs(size.Y - size.Z);
            var acrossY = Math.Abs(size.X - size.Z);
            var acrossZ = Math.Abs(size.X - size.Y);
            Axis = acrossY <= acrossX && acrossY <= acrossZ ? ColliderAxis.Y
                : acrossX <= acrossZ ? ColliderAxis.X
                : ColliderAxis.Z;
        }

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
        var turn = Axis == ColliderAxis.X ? Matrix4x4.CreateRotationZ<double>(float.DegreesToRadians(-90))
            : Axis == ColliderAxis.Z ? Matrix4x4.CreateRotationX<double>(float.DegreesToRadians(90))
            : Matrix4x4<double>.Identity;
        Geometry = Shapes.Cylinder.GenerateGeometry(GeometryType.Outlined, cylinder.Length, Radius * 2, 40,
            turn * Matrix4x4.CreateTranslation<double>(cylinder.Center.As<double>()));
        return Geometry;
    }

    public override void UpdateForCamera(CameraBase camera)
    {
        colliderData[camera] = cylinder.Transform(Owner.Transform.GetMetadata(camera).WorldMatrixF);
    }

    public override ContainmentType IsInsideCameraFrustum(Camera camera)
    {
        var placed = colliderData.TryGetValue(camera, out var data) ? data : cylinder;
        return placed.Within(camera.Frustum);
    }

    public override void Transform(ref Vector3F scale, ref QuaternionF rotation, ref Vector3F translation)
    {
        cylinder = cylinder.Transform(Matrix4x4.CreateScale<float>(scale) * Matrix4x4.CreateFromQuaternion<float>(rotation)
                                      * Matrix4x4.CreateTranslation<float>(translation));
    }

    public override void Transform(ref float uniformScale, ref QuaternionF rotation, ref Vector3F translation)
    {
        cylinder = cylinder.Transform(Matrix4x4.CreateScale<float>(uniformScale)
                                      * Matrix4x4.CreateFromQuaternion<float>(rotation)
                                      * Matrix4x4.CreateTranslation<float>(translation));
    }

    /// <summary>
    /// Grows the cylinder, along the same axis, to take in <paramref name="collider"/> of this entity or of one under
    /// it, as it stands here.
    /// </summary>
    public override void Merge(Collider collider)
    {
        Span<Vector3F> corners = stackalloc Vector3F[2 * OrientedBoundingBox<float>.CornerCount];
        cylinder.GetBoundingBox().GetCorners(corners[..OrientedBoundingBox<float>.CornerCount]);
        collider.Bounds.ToOrientedBoundingBox().As<float>().Transform(PlacementOf(collider.Owner))
            .GetCorners(corners[OrientedBoundingBox<float>.CornerCount..]);
        var merged = OrientedBoundingBox.CreateFromPoints<float>(corners);
        Bounds = Bounds.FromBoundingBox(merged.As<double>());
        Fit(merged.Size);
    }

    public override bool Intersects(ref Ray<float> ray, out Vector3F point)
    {
        return cylinder.Intersects(ray, out point);
    }

    public override bool IntersectsForCamera(Camera camera, ref Ray<float> ray, out Vector3F point)
    {
        point = Vector3F.Zero;
        return colliderData.TryGetValue(camera, out var placed) && placed.Intersects(ray, out point);
    }

    public override IConvexShape<float> GetWorldShape()
    {
        return cylinder.Transform(Owner.Transform.GetWorldMatrixF());
    }

    private void Fit(Vector3F size)
    {
        var along = Axis == ColliderAxis.X ? size.X : Axis == ColliderAxis.Y ? size.Y : size.Z;
        var across = Axis == ColliderAxis.X ? Math.Max(size.Y, size.Z)
            : Axis == ColliderAxis.Y ? Math.Max(size.X, size.Z)
            : Math.Max(size.X, size.Y);
        Radius = across / 2;
        Height = along;
        Rebuild();
    }

    private void Rebuild()
    {
        var axis = Axis == ColliderAxis.X ? Vector3F.UnitX : Axis == ColliderAxis.Y ? Vector3F.UnitY : Vector3F.UnitZ;
        var half = Math.Max(Height / 2, 0);
        var center = LocalCenter;
        cylinder = new BoundingCylinder(center - axis * half, center + axis * half, Radius);
    }
}
