using System;
using Adamantium.Graphics.Core.Models;
using Adamantium.Mathematics;
using Adamantium.MVVM;
using Adamantium.ProceduralGeometry;
using Adamantium.ProceduralGeometry.Shapes;

namespace Adamantium.ECS.Components;

/// <summary>A cone filling <see cref="Size"/>, standing along Y, its top <see cref="TopRatio"/> of its base: 0 a point,
/// 1 a cylinder.</summary>
[ViewModel]
public partial class ConeShape : ProceduralShape
{
    [Bindable] private Vector3<double> size;
    [Bindable] private double topRatio;

    public ConeShape()
    {
        Size = new Vector3<double>(1);
        Tessellation = 32;
    }

    partial void OnSizeChanged(Vector3<double> value) => Changed();

    partial void OnTopRatioChanged(double value) => Changed();

    protected override Mesh Generate() =>
        Shapes.Cone.GenerateGeometry(GeometryType.Solid, Positive(Size), Math.Clamp(TopRatio, 0, 1), TessellationFrom(3));
}
