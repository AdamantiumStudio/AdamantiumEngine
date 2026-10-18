using Adamantium.Graphics.Core.Models;
using Adamantium.Mathematics;
using Adamantium.MVVM;
using Adamantium.ProceduralGeometry;
using Adamantium.ProceduralGeometry.Shapes;

namespace Adamantium.ECS.Components;

/// <summary>A sphere stretched to fill <see cref="Size"/>, built as <see cref="Type"/> says: rings, a subdivided
/// octahedron or a puffed-out cube - the last two for a planet, which wants no crowded poles.</summary>
[ViewModel]
public partial class SphereShape : ProceduralShape
{
    [Bindable] private Vector3 size;
    [Bindable] private SphereType type;

    public SphereShape()
    {
        Size = new Vector3(1);
        Type = SphereType.UVSphere;
        Tessellation = 32;
    }

    partial void OnSizeChanged(Vector3 value) => Changed();

    partial void OnTypeChanged(SphereType value) => Changed();

    // A geosphere's tessellation counts subdivisions, each four times the triangles: past 7 it is not made finer.
    protected override Mesh Generate() =>
        Shapes.Sphere.GenerateGeometry(GeometryType.Solid, Type, Positive(Size),
            Type == SphereType.GeoSphere ? System.Math.Clamp(Tessellation, 0, 7) : TessellationFrom(3));
}
