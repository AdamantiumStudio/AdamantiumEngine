using System.Threading.Tasks;
using Adamantium.ECS;
using Adamantium.ECS.Components;
using Adamantium.Graphics;
using Adamantium.Mathematics;
using Adamantium.ProceduralGeometry;
using Adamantium.ProceduralGeometry.Shapes;

namespace Adamantium.Engine.Templates.GeometricPrimitives;

public class CylinderTemplate : PrimitiveTemplate
{
    private float diameter;
    private float height;

    public CylinderTemplate(
        GeometryType geometryType,
        float diameter,
        float height,
        int tessellation = 3,
        Matrix4x4? transform = null) : base(geometryType, tessellation, transform)
    {
        this.diameter = diameter;
        this.height = height;
    }

    public override Task<Entity> BuildEntity(Entity owner)
    {
        var primitive3D = Shapes.Cylinder.GenerateGeometry(GeometryType, height, diameter, Tessellation, Transform);
        return Task.FromResult(BuildEntityFromPrimitive(owner, primitive3D));
    }
}