using Adamantium.Graphics.Core.Models;
using Adamantium.Mathematics;
using Adamantium.MVVM;
using Adamantium.ProceduralGeometry;
using Adamantium.ProceduralGeometry.Shapes;

namespace Adamantium.ECS.Components;

/// <summary>A flat straight line along X: <see cref="Size"/>.X long and .Y thick. It has nothing to divide, so it takes
/// no <see cref="ProceduralShape.Tessellation"/>.</summary>
[ViewModel]
public partial class LineShape : ProceduralShape
{
    [Bindable] private Vector2<double> size;

    public LineShape()
    {
        Size = new Vector2<double>(1, 0.05);
    }

    partial void OnSizeChanged(Vector2<double> value) => Changed();

    protected override Mesh Generate()
    {
        var size = Positive(Size);
        return Shapes.Line.GenerateGeometry(GeometryType.Solid, new Vector3<double>(-size.X / 2, 0, 0), new Vector3<double>(size.X / 2, 0, 0), size.Y);
    }
}
