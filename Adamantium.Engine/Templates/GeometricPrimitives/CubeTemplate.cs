using System.Threading.Tasks;
using Adamantium.ECS;
using Adamantium.ECS.Components;
using Adamantium.Graphics;
using Adamantium.Mathematics;
using Adamantium.ProceduralGeometry;
using Adamantium.ProceduralGeometry.Shapes;

namespace Adamantium.Engine.Templates.GeometricPrimitives;

public class CubeTemplate : PrimitiveTemplate
{
    private float width, height, depth;
    private GeometryType geometryType;

    public CubeTemplate(
        GeometryType geometryType,
        float width = 1,
        float height = 1,
        float depth = 1,
        int tessellation = 1,
        Matrix4x4? transform = null) : base(geometryType, tessellation, transform)
    {
        this.width = width;
        this.height = height;
        this.depth = depth;
        this.geometryType = geometryType;
    }

    public override Task<Entity> BuildEntity(Entity owner)
    {
        var primitive3D = Shapes.Cube.GenerateGeometry(geometryType, width, height, depth, Tessellation);
        return Task.FromResult(BuildEntityFromPrimitive(owner, primitive3D));
    }
}