using System.Threading.Tasks;
using Adamantium.ECS;
using Adamantium.ECS.Components;
using Adamantium.Graphics;
using Adamantium.Mathematics;
using Adamantium.ProceduralGeometry;
using Adamantium.ProceduralGeometry.Shapes;

namespace Adamantium.Engine.Templates.GeometricPrimitives;

public class TubeTemplate : PrimitiveTemplate
{
    private double diameter, height, thickness;

    public TubeTemplate(
        GeometryType geometryType,
        double diameter,
        double height,
        double thickness,
        int tessellation = 3,
        Matrix4x4<double>? transform = null) : base(geometryType, tessellation, transform)
    {
        this.diameter = diameter;
        this.height = height;
        this.thickness = thickness;
    }

    public override Task<Entity> BuildEntity(Entity owner)
    {
        var primitive3D = Shapes.Tube.GenerateGeometry(GeometryType, diameter, height, thickness, Tessellation, Transform);
        return Task.FromResult(BuildEntityFromPrimitive(owner, primitive3D));
    }
}