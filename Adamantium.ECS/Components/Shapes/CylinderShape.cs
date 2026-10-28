using Adamantium.Graphics.Core.Models;
using Adamantium.Mathematics;
using Adamantium.MVVM;
using Adamantium.ProceduralGeometry;
using Adamantium.ProceduralGeometry.Shapes;

namespace Adamantium.ECS.Components;

/// <summary>A cylinder filling <see cref="Size"/>, standing along Y.</summary>
[ViewModel]
public partial class CylinderShape : ProceduralShape
{
    [Bindable] private Vector3<double> size;

    public CylinderShape()
    {
        Size = new Vector3<double>(1);
        Tessellation = 32;
    }

    partial void OnSizeChanged(Vector3<double> value) => Changed();

    protected override Mesh Generate() => Shapes.Cylinder.GenerateGeometry(GeometryType.Solid, Positive(Size), TessellationFrom(3));
}
