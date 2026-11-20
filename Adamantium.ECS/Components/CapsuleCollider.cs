using System;
using System.Collections.Generic;
using Adamantium.Graphics.Core.Models;
using Adamantium.Mathematics;
using Adamantium.MVVM;
using Adamantium.ProceduralGeometry;
using Adamantium.ProceduralGeometry.Shapes;

namespace Adamantium.ECS.Components;

/// <summary>
/// A capsule round an entity: a segment along <see cref="Axis"/> through the collider's center, rounded at both ends.
/// A fit stands it along the mesh's longest side, as thick as the wider of the other two.
/// </summary>
[ViewModel]
public partial class CapsuleCollider : Collider
{
    private readonly Dictionary<CameraBase, BoundingCapsule> colliderData = new();
    private BoundingCapsule capsule;

    public CapsuleCollider()
    {
        BeginFit();
        Axis = ColliderAxis.Y;
        EndFit();
    }

    /// <summary>The collider's own axis the capsule stands along.</summary>
    [Bindable]
    public partial ColliderAxis Axis { get; set; }

    [Bindable]
    public partial float Radius { get; set; }

    /// <summary>End to end, the rounded ends included; a height below the diameter counts as the diameter.</summary>
    [Bindable]
    public partial float Height { get; set; }

    partial void OnAxisChanged(ColliderAxis value) => Resize();

    partial void OnRadiusChanged(float value) => Resize();

    partial void OnHeightChanged(float value) => Resize();

    /// <summary>The capsule in its entity's own space.</summary>
    public BoundingCapsule Capsule => capsule;

    // Its ends are carried by its entity's scale; it stays round, its radius grown by the largest.
    public override double MeasureVolume(Vector3F scale)
    {
        double radius = capsule.Radius * Vector3F.Max(Vector3F.Abs(scale));
        double length = ((capsule.End - capsule.Start) * scale).Length();
        return Math.PI * radius * radius * (length + 4.0 / 3.0 * radius);
    }

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
        colliderData[camera] = capsule.Transform(Owner.Transform.GetMetadata(camera).WorldMatrixF);
    }

    public override ContainmentType IsInsideCameraFrustum(Camera camera)
    {
        var placed = colliderData.TryGetValue(camera, out var data) ? data : capsule;
        return placed.Within(camera.Frustum);
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

    public override IConvexShape<float> GetWorldShape(Vector3<double> origin)
    {
        return capsule.Transform(Owner.Transform.GetWorldMatrixF(origin));
    }

    protected override void FitBox(Bounds box)
    {
        var size = box.Size.As<float>();
        Axis = size.X >= size.Y && size.X >= size.Z ? ColliderAxis.X
            : size.Y >= size.Z ? ColliderAxis.Y
            : ColliderAxis.Z;
        Radius = AxisShape.Across(size, Axis) / 2;
        Height = Math.Max(AxisShape.Along(size, Axis), Radius * 2);
        Position = box.Center.As<float>();
        Rotation = TurnOf(box);
    }

    protected override void WriteSizes(ColliderShapeEdits shape)
    {
        shape.Axis = Axis;
        shape.Radius = Radius;
        shape.Height = Height;
    }

    protected override void TakeSizes(ColliderShapeEdits shape)
    {
        Axis = shape.Axis;
        Radius = shape.Radius;
        Height = shape.Height;
    }

    // The scale along the axis stretches the height, the wider of those across it the radius.
    protected override Bounds PlaceShape()
    {
        var scale = Vector3F.Abs(Scale);
        var radius = Math.Abs(Radius) * AxisShape.Across(scale, Axis);
        var height = Math.Abs(Height) * AxisShape.Along(scale, Axis);
        var half = AxisShape.Direction(Axis) * Math.Max(height / 2 - radius, 0);
        var placed = new BoundingCapsule(-half, half, radius).Transform(Standing);
        // Another capsule may stand in the same box: one turned the other way, or along another axis.
        if (placed.Start != capsule.Start || placed.End != capsule.End || placed.Radius != capsule.Radius)
        {
            MarkOutlineStale();
        }

        capsule = placed;
        return BoundsOf(capsule.GetBoundingBox());
    }

    protected override Mesh BuildOutline()
    {
        return Shapes.Capsule.GenerateGeometry(GeometryType.Outlined, capsule.Length, capsule.Radius * 2, 40,
            AxisShape.StandUp(capsule.Start, capsule.End));
    }
}
