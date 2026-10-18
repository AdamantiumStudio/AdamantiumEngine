using System.Threading.Tasks;
using Adamantium.ECS;
using Adamantium.ECS.Components;
using Adamantium.Graphics;
using Adamantium.Mathematics;
using Adamantium.ProceduralGeometry;
using Adamantium.ProceduralGeometry.Shapes;

namespace Adamantium.Engine.Templates.GeometricPrimitives;

public class ArcTemplate : PrimitiveTemplate
{
    private double thickness;
    private Vector2 diameter;
    private float startAngle = 0;
    private float stopAngle = 360;
    private bool isClockwise = true;

    public ArcTemplate(
        GeometryType geometryType,
        Vector2 diameter,
        float thickness,
        float startAngle = 0,
        float stopAngle = 360,
        bool isClockwise = true,
        int tessellation = 40,
        Matrix4x4? transform = null) : base(geometryType, tessellation, transform)
    {
        this.diameter = diameter;
        this.thickness = thickness;
        this.startAngle = startAngle;
        this.stopAngle = stopAngle;
        this.isClockwise = isClockwise;
    }

    public override Task<Entity> BuildEntity(Entity owner)
    {
        var geometry = Shapes.Arc.GenerateGeometry(
            GeometryType, 
            diameter, 
            thickness, 
            startAngle, 
            stopAngle,
            isClockwise, 
            Tessellation, 
            Transform);
        return Task.FromResult(BuildEntityFromPrimitive(owner, geometry));
    }
}