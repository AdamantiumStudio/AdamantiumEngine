using System.Threading.Tasks;
using Adamantium.ECS;
using Adamantium.ECS.Components;
using Adamantium.Graphics;
using Adamantium.Mathematics;
using Adamantium.ProceduralGeometry;
using Adamantium.ProceduralGeometry.Shapes;

namespace Adamantium.Engine.Templates.GeometricPrimitives;

public class SphereTemplate : PrimitiveTemplate
{
    private double diameter;
    private SphereType sphereType;

    public SphereTemplate(
        GeometryType geometryType,
        SphereType sphereType,
        double diameter,
        int tessellation = 3,
        Matrix4x4? transform = null) : base(geometryType, tessellation, transform)
    {
        this.diameter = diameter;
        this.sphereType = sphereType;
    }

    public override Task<Entity> BuildEntity(Entity owner)
    {
        var primitive3D = Shapes.Sphere.GenerateGeometry(GeometryType, sphereType, diameter, Tessellation, Transform);
        return Task.FromResult(BuildEntityFromPrimitive(owner, primitive3D, BoundingVolume.Sphere));
    }
}