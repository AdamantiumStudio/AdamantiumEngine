using System;
using Adamantium.Graphics.Core.Models;
using Adamantium.Mathematics;
using Adamantium.MVVM;
using Adamantium.ProceduralGeometry;
using Adamantium.ProceduralGeometry.Shapes;

namespace Adamantium.ECS.Components;

/// <summary>A flat plane lying on the ground: <see cref="Size"/>.X wide along X and .Y long along Z, divided into
/// <see cref="ProceduralShape.Tessellation"/> cells each way.</summary>
[ViewModel]
public partial class PlaneShape : ProceduralShape
{
    [Bindable] private Vector2 size;

    public PlaneShape()
    {
        Size = new Vector2(1);
        Tessellation = 1;
    }

    partial void OnSizeChanged(Vector2 value) => Changed();

    protected override Mesh Generate()
    {
        var size = Positive(Size);
        return Shapes.Plane.GenerateGeometry(GeometryType.Solid, size.X, size.Y, TessellationFrom(1));
    }
}
