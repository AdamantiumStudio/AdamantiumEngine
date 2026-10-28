using System.Threading.Tasks;
using Adamantium.ECS;
using Adamantium.ECS.Components;
using Adamantium.Graphics;
using Adamantium.Mathematics;
using Adamantium.ProceduralGeometry;
using Adamantium.ProceduralGeometry.Shapes;

namespace Adamantium.Engine.Templates.GeometricPrimitives;

public class PlaneTemplate : PrimitiveTemplate
{
    private float width;
    private float length;

    public PlaneTemplate(
        GeometryType geometryType,
        float width,
        float length,
        int tessellation = 1,
        Matrix4x4<double>? transform = null) : base(geometryType, tessellation, transform)
    {
        this.width = width;
        this.length = length;
    }

    public override Task<Entity> BuildEntity(Entity owner)
    {
        var primitive3D = Shapes.Plane.GenerateGeometry(GeometryType, width, length, Tessellation);
        return Task.FromResult(BuildEntityFromPrimitive(owner, primitive3D));
    }
}