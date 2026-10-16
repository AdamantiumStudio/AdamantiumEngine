using Adamantium.ECS;
using Adamantium.Mathematics;

namespace Adamantium.Multiverse;

public class CameraTemplate
{
    public Entity BuildEntity(Entity owner, string name, Vector3 position, Vector3 lookAt, Vector3 up, uint width, uint height, float znear, float zfar)
    {
        Entity root = new Entity(owner, name) { IconKey = EntityIconKeys.Camera };
        root.Transform.Position = position;
        var camera = new ECS.Components.Camera(lookAt, up, 45, width, height, znear, zfar);
        root.AddComponent(camera);
        return root;
    }
}