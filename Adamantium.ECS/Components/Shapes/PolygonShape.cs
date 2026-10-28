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
    [Bindable] private Vector2<double> size;

    public PolygonShape()
    {
        Size = new Vector2<double>(1);
        Tessellation = 6;
    }

    partial void OnSizeChanged(Vector2<double> value) => Changed();

    protected override Mesh Generate() =>
        Shapes.Polygon.GenerateGeometry(GeometryType.Solid, Positive(Size) / 2, TessellationFrom(3));
}
