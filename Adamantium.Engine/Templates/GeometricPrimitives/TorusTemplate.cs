using System.Threading.Tasks;
using Adamantium.ECS;
using Adamantium.ECS.Components;
using Adamantium.Graphics;
using Adamantium.Mathematics;
using Adamantium.ProceduralGeometry;
using Adamantium.ProceduralGeometry.Shapes;

namespace Adamantium.Engine.Templates.GeometricPrimitives;

public class TorusTemplate : PrimitiveTemplate
{
    private double diameter,
        thickness;

    public TorusTemplate(
        GeometryType geometryType,
        double diameter,
        double thickness,
        int tessellation = 3,
        Matrix4x4? transform = null) : base(geometryType, tessellation, transform)
    {
        this.diameter = diameter;
        this.thickness = thickness;
    }

    public override Task<Entity> BuildEntity(Entity owner)
    {
        var primitive3D = Shapes.Torus.GenerateGeometry(GeometryType, diameter, thickness, Tessellation, Transform);
        return Task.FromResult(BuildEntityFromPrimitive(owner, primitive3D));
    }
}