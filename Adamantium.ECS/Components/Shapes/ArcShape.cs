using System;
using Adamantium.Graphics.Core.Models;
using Adamantium.Mathematics;
using Adamantium.MVVM;
using Adamantium.ProceduralGeometry;
using Adamantium.ProceduralGeometry.Shapes;

namespace Adamantium.ECS.Components;

/// <summary>A flat band <see cref="Thickness"/> wide along an ellipse filling <see cref="Size"/>, from
/// <see cref="StartAngle"/> to <see cref="StopAngle"/>, in degrees.</summary>
[ViewModel]
public partial class ArcShape : ProceduralShape
{
    [Bindable] private Vector2 size;
    [Bindable] private double thickness;
    [Bindable] private float startAngle;
    [Bindable] private float stopAngle;

    public ArcShape()
    {
        Size = new Vector2(1);
        Thickness = 0.1;
        StopAngle = 270;
        Tessellation = 40;
    }

    partial void OnSizeChanged(Vector2 value) => Changed();

    partial void OnThicknessChanged(double value) => Changed();

    partial void OnStartAngleChanged(float value) => Changed();

    partial void OnStopAngleChanged(float value) => Changed();

    protected override Mesh Generate()
    {
        var size = Positive(Size);
        var thickness = Math.Clamp(Thickness, 1e-3, Math.Min(size.X, size.Y) / 2);
        return Shapes.Arc.GenerateGeometry(GeometryType.Solid, size, thickness, StartAngle, StopAngle, true, TessellationFrom(3));
    }
}
