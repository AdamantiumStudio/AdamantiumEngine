using Adamantium.Graphics.Core.Models;
using Adamantium.Mathematics;
using Adamantium.MVVM;
using Adamantium.ProceduralGeometry;
using Adamantium.ProceduralGeometry.Shapes;

namespace Adamantium.ECS.Components;

/// <summary>A capsule filling <see cref="Size"/>, standing along Y, its round ends as wide as <see cref="Size"/>.X.</summary>
[ViewModel]
public partial class CapsuleShape : ProceduralShape
{
    [Bindable] private Vector3 size;

    public CapsuleShape()
    {
        Size = new Vector3(1, 2, 1);
        Tessellation = 16;
    }

    partial void OnSizeChanged(Vector3 value) => Changed();

    public override Collider CreateCollider() => new CapsuleCollider();

    protected override Mesh Generate() => Shapes.Capsule.GenerateGeometry(GeometryType.Solid, Positive(Size), TessellationFrom(8));
}
