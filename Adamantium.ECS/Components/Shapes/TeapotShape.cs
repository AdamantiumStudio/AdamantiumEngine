using System;
using Adamantium.Graphics.Core.Models;
using Adamantium.Mathematics;
using Adamantium.MVVM;
using Adamantium.ProceduralGeometry;
using Adamantium.ProceduralGeometry.Shapes;

namespace Adamantium.ECS.Components;

/// <summary>The teapot, scaled by <see cref="Size"/> along each axis.</summary>
[ViewModel]
public partial class TeapotShape : ProceduralShape
{
    [Bindable] private Vector3<double> size;

    public TeapotShape()
    {
        Size = new Vector3<double>(1);
        Tessellation = 8;
    }

    partial void OnSizeChanged(Vector3<double> value) => Changed();

    protected override Mesh Generate() =>
        Shapes.Teapot.GenerateGeometry(GeometryType.Solid, Positive(Size), TessellationFrom(1));
}
