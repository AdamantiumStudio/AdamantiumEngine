using System;
using Adamantium.ECS;
using Adamantium.ECS.Components;
using Adamantium.Mathematics;

namespace Adamantium.Multiverse;

public class CameraTemplate
{
    public Entity BuildEntity(Entity owner, string name, Vector3<double> position, Vector3<double> lookAt, Vector3<double> up, uint width, uint height, float znear, float zfar)
    {
        var root = new Entity(owner, name);
        root.Transform.Position = position;
        Build(root, lookAt, up, 45, width, height, znear, zfar);
        return root;
    }

    /// <summary>Makes <paramref name="entity"/> a camera; the camera it is given.</summary>
    public Camera Build(Entity entity, Vector3<double> lookAt, Vector3<double> up, float fieldOfView, uint width,
        uint height, float znear, float zfar)
    {
        ArgumentNullException.ThrowIfNull(entity);
        entity.IconKey = EntityIconKeys.Camera;
        var camera = new Camera(lookAt, up, fieldOfView, width, height, znear, zfar);
        entity.AddComponent(camera);
        return camera;
    }
}
