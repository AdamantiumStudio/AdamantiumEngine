using System;
using Adamantium.Graphics.Core.Models;
using Adamantium.Mathematics;
using Adamantium.MVVM;
using Adamantium.ProceduralGeometry;
using Adamantium.ProceduralGeometry.Shapes;

namespace Adamantium.ECS.Components;

/// <summary>A flat regular polygon filling <see cref="Size"/>, with <see cref="ProceduralShape.Tessellation"/> sides.</summary>
[ViewModel]
public partial class PolygonShape : ProceduralShape
{
    [Bindable] private Vector2 size;

    public PolygonShape()
    {
        Size = new Vector2(1);
        Tessellation = 6;
    }

    partial void OnSizeChanged(Vector2 value) => Changed();

    protected override Mesh Generate() =>
        Shapes.Polygon.GenerateGeometry(GeometryType.Solid, Positive(Size), TessellationFrom(3));
}
