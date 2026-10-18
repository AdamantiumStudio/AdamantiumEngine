using System.Threading.Tasks;
using Adamantium.ECS;
using Adamantium.ECS.Components;
using Adamantium.Graphics;
using Adamantium.Mathematics;
using Adamantium.ProceduralGeometry;
using Adamantium.ProceduralGeometry.Shapes;

namespace Adamantium.Engine.Templates.GeometricPrimitives;

public class LineTemplate : PrimitiveTemplate
{
    private float thickness;
    private Vector3 startPoint;
    private Vector3 endPoint;

    public LineTemplate(
        GeometryType geometryType,
        Vector3 startPoint,
        Vector3 endPoint,
        float thickness,
        Matrix4x4? transform = null) : base(geometryType, 1, transform)
    {
        this. startPoint = startPoint;
        this. endPoint = endPoint;
        this.thickness = thickness;
    }

    public override Task<Entity> BuildEntity(Entity owner)
    {
        var primitive3D = Shapes.Line.GenerateGeometry(GeometryType.Solid, startPoint, endPoint, thickness);
        return Task.FromResult(BuildEntityFromPrimitive(owner, primitive3D));
    }
}