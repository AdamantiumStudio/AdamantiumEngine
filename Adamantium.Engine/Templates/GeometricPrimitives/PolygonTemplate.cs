using System.Threading.Tasks;
using Adamantium.ECS;
using Adamantium.ECS.Components;
using Adamantium.Graphics;
using Adamantium.Mathematics;
using Adamantium.ProceduralGeometry;
using Adamantium.ProceduralGeometry.Shapes;

namespace Adamantium.Engine.Templates.GeometricPrimitives;

public class PolygonTemplate : PrimitiveTemplate
{
    private Vector2<double> diameter;

    public PolygonTemplate(
        GeometryType geometryType,
        Vector2<double> diameter,
        int tessellation,
        Matrix4x4<double>? transform = null) : base(geometryType, tessellation, transform)
    {
        this.diameter = diameter;
    }

    public override Task<Entity> BuildEntity(Entity owner)
    {
        var primitive = Shapes.Polygon.GenerateGeometry(GeometryType, diameter / 2, Tessellation, transform: Transform);
        return Task.FromResult(BuildEntityFromPrimitive(owner, primitive));
    }
}