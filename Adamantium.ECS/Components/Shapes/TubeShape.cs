using System;
using Adamantium.Graphics.Core.Models;
using Adamantium.Mathematics;
using Adamantium.MVVM;
using Adamantium.ProceduralGeometry.Shapes;

namespace Adamantium.ECS.Components;

/// <summary>A tube filling <see cref="Size"/>, standing along Y, its wall <see cref="Thickness"/> of its outer radius
/// thick.</summary>
[ViewModel]
public partial class TubeShape : ProceduralShape
{
    [Bindable] private Vector3 size;
    [Bindable] private double thickness;

    public TubeShape()
    {
        Size = new Vector3(1);
        Thickness = 0.3;
        Tessellation = 32;
    }

    partial void OnSizeChanged(Vector3 value) => Changed();

    partial void OnThicknessChanged(double value) => Changed();

    protected override Mesh Generate() =>
        Shapes.Tube.GenerateGeometry(Positive(Size), Math.Clamp(Thickness, 0.01, 0.99), TessellationFrom(3));
}
