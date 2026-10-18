using Adamantium.ECS;
using Adamantium.Graphics;
using Adamantium.Mathematics;
using Adamantium.ProceduralGeometry;
using Adamantium.ProceduralGeometry.Shapes;

namespace Adamantium.Engine.Templates.Lights;

public class SpotLightVisualTemplate: LightVisualTemplate
{
    public override Entity BuildEntity(Entity owner, string name)
    {
        var transform = Matrix4x4.Translation(0, -0.5f, 0);
        var cone = Shapes.Cone.GenerateGeometry(GeometryType.Outlined, 1, 1, 0, 40, transform);
        var anchor = Shapes.Cube.GenerateGeometry(GeometryType.Solid, 1, 1);

        var root = BuildSubEntity(owner, name, Colors.Yellow, cone);
        BuildSubEntity(root, "AnchorPointCenter", Colors.Green, anchor, BoundingVolume.OrientedBox);
        BuildSubEntity(root, "AnchorPointRight", Colors.Red, anchor, BoundingVolume.OrientedBox);
        BuildSubEntity(root, "AnchorPointForward", Colors.Red, anchor, BoundingVolume.OrientedBox);
        BuildSubEntity(root, "AnchorPointLeft", Colors.Red, anchor, BoundingVolume.OrientedBox);
        BuildSubEntity(root, "AnchorPointBackward", Colors.Red, anchor, BoundingVolume.OrientedBox);
        return root;
    }
}
