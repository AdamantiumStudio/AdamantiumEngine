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
        BeginFit();
        Axis = ColliderAxis.Y;
        ChoosesAxis = true;
        EndFit();
    }

    /// <summary>Whether fitting to a mesh chooses the axis; false keeps <see cref="Axis"/> for a shape known to stand
    /// so.</summary>
    [Bindable]
    public partial bool ChoosesAxis { get; set; }

    /// <summary>The collider's own axis the cylinder stands along.</summary>
    [Bindable]
    public partial ColliderAxis Axis { get; set; }

    [Bindable]
    public partial float Radius { get; set; }

    /// <summary>End to end.</summary>
    [Bindable]
    public partial float Height { get; set; }

    // An axis the fit keeps is one the fitted cylinder fits along anew; otherwise it is set by hand.
    partial void OnAxisChanged(ColliderAxis value)
    {
        if (IsFitted && !ChoosesAxis && Owner?.GetComponent<MeshData>() is { Mesh: { } mesh })
        {
            FitTo(mesh);
            return;
        }

        Resize();
    }

    partial void OnRadiusChanged(float value) => Resize();

    partial void OnHeightChanged(float value) => Resize();

    // Let choose again, a fitted cylinder fits the mesh it stands on anew; told to keep its axis, it keeps the one it has.
    partial void OnChoosesAxisChanged(bool value)
    {
        if (value && IsFitted && Owner?.GetComponent<MeshData>() is { Mesh: { } mesh })
        {
            FitTo(mesh);
        }
    }

    /// <summary>The cylinder in its entity's own space.</summary>
    public BoundingCylinder Cylinder => cylinder;

    // Its ends are carried by its entity's scale; it stays round, its radius grown by the wider of the scales across the
    // entity's axis nearest its own.
    public override double MeasureVolume(Vector3F scale)
    {
        var size = Vector3F.Abs(scale);
        var along = Vector3F.Abs(cylinder.End - cylinder.Start);
        var across = along.X >= along.Y && along.X >= along.Z ? Math.Max(size.Y, size.Z)
            : along.Y >= along.Z ? Math.Max(size.X, size.Z)
            : Math.Max(size.X, size.Y);
        double radius = cylinder.Radius * across;
        double height = ((cylinder.End - cylinder.Start) * scale).Length();
        return Math.PI * radius * radius * height;
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
        colliderData[camera] = cylinder.Transform(Owner.Transform.GetMetadata(camera).WorldMatrixF);
    }

    public override ContainmentType IsInsideCameraFrustum(Camera camera)
    {
        var placed = colliderData.TryGetValue(camera, out var data) ? data : cylinder;
        return placed.Within(camera.Frustum);
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

    public override IConvexShape<float> GetWorldShape(WorldPosition origin)
    {
        return cylinder.Transform(Owner.Transform.GetWorldMatrixF(origin));
    }

    protected override void FitBox(Bounds box)
    {
        var size = box.Size.As<float>();
        if (ChoosesAxis)
        {
            var acrossX = Math.Abs(size.Y - size.Z);
            var acrossY = Math.Abs(size.X - size.Z);
            var acrossZ = Math.Abs(size.X - size.Y);
            Axis = acrossY <= acrossX && acrossY <= acrossZ ? ColliderAxis.Y
                : acrossX <= acrossZ ? ColliderAxis.X
                : ColliderAxis.Z;
        }

        Radius = AxisShape.Across(size, Axis) / 2;
        Height = AxisShape.Along(size, Axis);
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

    // A fit that does not choose the axis keeps the one the cylinder has.
    protected override void WriteKept(ColliderShapeEdits shape)
    {
        shape.ChoosesAxis = ChoosesAxis;
        if (!ChoosesAxis)
        {
            shape.Axis = Axis;
        }
    }

    protected override void TakeKept(ColliderShapeEdits shape)
    {
        ChoosesAxis = shape.ChoosesAxis;
        if (!shape.ChoosesAxis)
        {
            Axis = shape.Axis;
        }
    }

    // The scale along the axis stretches the height, the wider of those across it the radius.
    protected override Bounds PlaceShape()
    {
        var scale = Vector3F.Abs(Scale);
        var half = AxisShape.Direction(Axis) * (Math.Abs(Height) * AxisShape.Along(scale, Axis) / 2);
        var placed = new BoundingCylinder(-half, half, Math.Abs(Radius) * AxisShape.Across(scale, Axis)).Transform(Standing);
        // Another cylinder may stand in the same box: one turned the other way, or along another axis.
        if (placed.Start != cylinder.Start || placed.End != cylinder.End || placed.Radius != cylinder.Radius)
        {
            MarkOutlineStale();
        }

        cylinder = placed;
        return BoundsOf(cylinder.GetBoundingBox());
    }

    protected override Mesh BuildOutline()
    {
        return Shapes.Cylinder.GenerateGeometry(GeometryType.Outlined, cylinder.Length, cylinder.Radius * 2, 40,
            AxisShape.StandUp(cylinder.Start, cylinder.End));
    }
}
