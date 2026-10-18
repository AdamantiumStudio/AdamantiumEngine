using Adamantium.ECS;
using Adamantium.ECS.Components;
using Adamantium.Graphics;
using Adamantium.Mathematics;
using Adamantium.ProceduralGeometry;
using Adamantium.ProceduralGeometry.Shapes;

namespace Adamantium.Engine.Templates.Lights;

public class SpotLightMeshTemplate
{
    public Entity BuildEntity()
    {
        var transform = Matrix4x4.Translation(0, -0.5f, 0);
        var cone = Shapes.Cone.GenerateGeometry(GeometryType.Solid, 1, 1, 0, 40, transform);

        var root = new Entity(null, "Spot light mesh");
        var meshComponent = new MeshData();
        meshComponent.Mesh = cone;

        root.Components.Add(meshComponent);

        return root;
    }
}