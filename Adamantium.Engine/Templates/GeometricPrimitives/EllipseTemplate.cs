using System.Threading.Tasks;
using Adamantium.ECS;
using Adamantium.ECS.Components;
using Adamantium.Graphics;
using Adamantium.Mathematics;
using Adamantium.ProceduralGeometry;
using Adamantium.ProceduralGeometry.Shapes;

namespace Adamantium.Engine.Templates.GeometricPrimitives;

public class EllipseTemplate : PrimitiveTemplate
{
    private EllipseType ellipseType;
    private Vector2 diameter;
    private float startAngle;
    private float stopAngle;

    private bool isClockwise;

    public EllipseTemplate(
        GeometryType geometryType,
        EllipseType ellipseType,
        Vector2 diameter,
        float startAngle = 0,
        float stopAngle = 360,
        bool isClockwise = true,
        int tessellation = 40,
        Matrix4x4? transform = null) : base(geometryType, tessellation, transform)
    {
        this.diameter = diameter;
        this.ellipseType = ellipseType;
        this.startAngle = startAngle;
        this.stopAngle = stopAngle;
        this.isClockwise = isClockwise;
    }

    public override Task<Entity> BuildEntity(Entity owner)
    {
        var geometry = Shapes.Ellipse.GenerateGeometry(
            GeometryType, 
            ellipseType, 
            diameter,
            startAngle,
            stopAngle - startAngle,
            isClockwise,
            Tessellation, 
            Transform);
        return Task.FromResult(BuildEntityFromPrimitive(owner, geometry));
    }
}