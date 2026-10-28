using System.Threading.Tasks;
using Adamantium.ECS;
using Adamantium.ECS.Components;
using Adamantium.Graphics;
using Adamantium.Mathematics;
using Adamantium.ProceduralGeometry;
using Adamantium.ProceduralGeometry.Shapes;

namespace Adamantium.Engine.Templates.GeometricPrimitives;

public class ConeTemplate : PrimitiveTemplate
{
    private float topDiameter,
        bottomDiameter,
        height;

    public ConeTemplate(
        GeometryType geometryType,
        float topDiameter,
        float bottomDiameter,
        float height,
        int tessellation = 3,
        Matrix4x4<double>? transform = null) : base(geometryType, tessellation, transform)
    {
        this.topDiameter = topDiameter;
        this.bottomDiameter = bottomDiameter;
        this.height = height;
    }

    public override Task<Entity> BuildEntity(Entity owner)
    {
        var geometry = Shapes.Cone.GenerateGeometry(
            GeometryType, 
            height, 
            topDiameter, 
            bottomDiameter, 
            Tessellation,
            Transform);
        return Task.FromResult(BuildEntityFromPrimitive(owner, geometry));
    }
}