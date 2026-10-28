using Adamantium.Graphics.Core.Models;
using Adamantium.Mathematics;
using Adamantium.MVVM;
using Adamantium.ProceduralGeometry;
using Adamantium.ProceduralGeometry.Shapes;

namespace Adamantium.ECS.Components;

/// <summary>A flat rectangle, its corners rounded by <see cref="Rounding"/> over <see cref="ProceduralShape.Tessellation"/>
/// segments each.</summary>
[ViewModel]
public partial class RectangleShape : ProceduralShape
{
    [Bindable] private Vector2<double> size;
    [Bindable] private double rounding;

    public RectangleShape()
    {
        Size = new Vector2<double>(1);
        Tessellation = 20;
    }

    partial void OnSizeChanged(Vector2<double> value) => Changed();

    partial void OnRoundingChanged(double value) => Changed();

    protected override Mesh Generate()
    {
        var size = Positive(Size);
        var radius = System.Math.Clamp(Rounding, 0, System.Math.Min(size.X, size.Y) / 2);
        return Shapes.Rectangle.GenerateGeometry(GeometryType.Solid, size.X, size.Y, new CornerRadius(radius), TessellationFrom(1));
    }
}
