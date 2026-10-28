using System.Threading.Tasks;
using Adamantium.ECS;
using Adamantium.ECS.Components;
using Adamantium.Graphics;
using Adamantium.Mathematics;
using Adamantium.ProceduralGeometry;
using Adamantium.ProceduralGeometry.Shapes;

namespace Adamantium.Engine.Templates.GeometricPrimitives;

public class TeapotTemplate : PrimitiveTemplate
{
    private float size;

    public TeapotTemplate(
        float size,
        int tessellation = 1,
        Matrix4x4<double>? transform = null) : base(GeometryType.Solid, tessellation, transform)
    {
        this.size = size;
    }

    public override Task<Entity> BuildEntity(Entity owner)
    {
        var primitive3D = Shapes.Teapot.GenerateGeometry(GeometryType, size, Tessellation, Transform);
        return Task.FromResult(BuildEntityFromPrimitive(owner, primitive3D));
    }
}