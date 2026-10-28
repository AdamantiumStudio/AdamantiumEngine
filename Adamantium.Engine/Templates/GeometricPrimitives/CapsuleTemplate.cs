using System.Threading.Tasks;
using Adamantium.ECS;
using Adamantium.ECS.Components;
using Adamantium.Graphics;
using Adamantium.Mathematics;
using Adamantium.ProceduralGeometry;
using Adamantium.ProceduralGeometry.Shapes;

namespace Adamantium.Engine.Templates.GeometricPrimitives;

public class CapsuleTemplate : PrimitiveTemplate
{
    private float diameter;
    private float height;

    public CapsuleTemplate(
        GeometryType geometryType,
        float diameter,
        float height,
        int tessellation,
        Matrix4x4<double>? transform = null) : base(geometryType, tessellation, transform)
    {
        this.height = height;
        this.diameter = diameter;
    }

    public override Task<Entity> BuildEntity(
        Entity owner)
    {
        var primitive3D = Shapes.Capsule.GenerateGeometry(GeometryType, height, diameter, Tessellation, Transform);
        return Task.FromResult(BuildEntityFromPrimitive(owner, primitive3D));
    }
}