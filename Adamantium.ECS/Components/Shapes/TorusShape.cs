using System;
using Adamantium.Graphics.Core.Models;
using Adamantium.Mathematics;
using Adamantium.MVVM;
using Adamantium.ProceduralGeometry.Shapes;

namespace Adamantium.ECS.Components;

/// <summary>A torus lying on the ground, filling <see cref="Size"/>, its tube <see cref="Thickness"/> of its outer
/// radius thick: 1 closes the hole.</summary>
[ViewModel]
public partial class TorusShape : ProceduralShape
{
    [Bindable] private Vector3 size;
    [Bindable] private double thickness;

    public TorusShape()
    {
        Size = new Vector3(2, 0.5, 2);
        Thickness = 0.5;
        Tessellation = 32;
    }

    partial void OnSizeChanged(Vector3 value) => Changed();

    partial void OnThicknessChanged(double value) => Changed();

    protected override Mesh Generate() =>
        Shapes.Torus.GenerateGeometry(Positive(Size), Math.Clamp(Thickness, 0.01, 1), TessellationFrom(3));
}
