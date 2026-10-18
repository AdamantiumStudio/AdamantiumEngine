using System.Threading.Tasks;
using Adamantium.ECS;
using Adamantium.ECS.Components;
using Adamantium.Graphics;
using Adamantium.Mathematics;
using Adamantium.ProceduralGeometry;
using Adamantium.ProceduralGeometry.Shapes;

namespace Adamantium.Engine.Templates.GeometricPrimitives;

public class RectangleTemplate : PrimitiveTemplate
{
    private float width, height;
    private CornerRadius corners;

    public RectangleTemplate(
        GeometryType geometryType,
        float width = 2,
        float height = 1,
        CornerRadius corners = default,
        int tessellation = 20,
        Matrix4x4? transform = null) : base(geometryType, tessellation, transform)
    {
        this.width = width;
        this.height = height;
        this.corners = corners;
    }

    public override Task<Entity> BuildEntity(Entity owner)
    {
        var geometry = Shapes.Rectangle.GenerateGeometry(
            GeometryType, 
            width, 
            height, 
            corners, 
            Tessellation, 
            Transform);
        return Task.FromResult(BuildEntityFromPrimitive(owner, geometry));
    }
}