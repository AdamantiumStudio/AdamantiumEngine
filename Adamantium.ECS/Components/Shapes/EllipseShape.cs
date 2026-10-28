using Adamantium.Graphics.Core.Models;
using Adamantium.Mathematics;
using Adamantium.MVVM;
using Adamantium.ProceduralGeometry;
using Adamantium.ProceduralGeometry.Shapes;

namespace Adamantium.ECS.Components;

/// <summary>A flat ellipse filling <see cref="Size"/>.</summary>
[ViewModel]
public partial class EllipseShape : ProceduralShape
{
    [Bindable] private Vector2<double> size;

    public EllipseShape()
    {
        Size = new Vector2<double>(1);
        Tessellation = 40;
    }

    partial void OnSizeChanged(Vector2<double> value) => Changed();

    protected override Mesh Generate() =>
        Shapes.Ellipse.GenerateGeometry(GeometryType.Solid, EllipseType.EdgeToEdge, Positive(Size), 0, 360, true, TessellationFrom(4));
}
