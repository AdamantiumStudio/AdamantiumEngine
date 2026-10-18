using System;
using Adamantium.Graphics.Core.Models;
using Adamantium.Mathematics;
using Adamantium.MVVM;
using Adamantium.ProceduralGeometry;
using Adamantium.ProceduralGeometry.Shapes;

namespace Adamantium.ECS.Components;

/// <summary>A box filling <see cref="Size"/>, each face divided into <see cref="ProceduralShape.Tessellation"/> cells
/// each way.</summary>
[ViewModel]
public partial class CubeShape : ProceduralShape
{
    [Bindable] private Vector3 size;

    public CubeShape()
    {
        Size = new Vector3(1);
        Tessellation = 1;
    }

    partial void OnSizeChanged(Vector3 value) => Changed();

    protected override Mesh Generate()
    {
        var size = Positive(Size);
        return Shapes.Cube.GenerateGeometry(GeometryType.Solid, size.X, size.Y, size.Z, TessellationFrom(1));
    }
}
