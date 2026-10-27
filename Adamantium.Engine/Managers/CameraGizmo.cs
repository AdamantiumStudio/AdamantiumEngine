using Adamantium.Core;
using Adamantium.Engine.Rendering;
using Adamantium.Engine.Templates.CameraTemplates;
using Adamantium.ECS;
using Adamantium.ECS.Components;
using Adamantium.ECS.Components.Extensions;
using Adamantium.Multiverse;
using Adamantium.Graphics;
using Adamantium.Graphics.Core.EffectsFramework;
using Adamantium.Mathematics;

namespace Adamantium.Engine.Managers;

/// <summary>
/// Cameras as objects in the scene, for the editor: a new one set in front of the current one, and a frustum showing
/// where the selected one looks. A universe without the editor has no use for any of it.
/// </summary>
public class CameraGizmo
{
    private const float DefaultZNear = 0.1f;
    private const float DefaultZFar = 1000000.0f;

    private readonly EntityWorld entityWorld;

    private Entity CameraIcon;
    private Entity CameraVisual;

    private Entity SelectedCamera;

    private static int cameraNumber = 1;


    public CameraGizmo(EntityWorld entityWorld)
    {
        this.entityWorld = entityWorld;
        CreateCameraIcon();
        CreateCameraVisual();
    }

    private void CreateCameraIcon()
    {
        CameraIcon = new CameraIconTemplate().BuildEntity(null, "Camera icon");
    }

    private void CreateCameraVisual()
    {
        CameraVisual = new CameraVisualTemplate().BuildEntity(null, "Camera Debug");
    }

    public void SetSelected(Entity camera)
    {
        SelectedCamera = camera;
    }

    /// <summary>
    /// Adds a camera to <paramref name="output"/> just in front of the one it looks through, and makes it current.
    /// </summary>
    public Camera CreateCamera(UniverseOutput output, string name)
    {
        if (string.IsNullOrEmpty(name))
        {
            name = $"Camera ({cameraNumber})";
            cameraNumber++;
        }

        var position = Vector3.Zero;
        var current = output.Camera;
        if (current != null)
        {
            position = (CameraIcon.GetDiameter() * 2 * (Vector3)current.Forward) + current.GetOwnerPosition();
        }

        var entity = new CameraTemplate().BuildEntity(null, name, position, Vector3.ForwardLH, -Vector3.Up, output.Width, output.Height, DefaultZNear, DefaultZFar);
        entityWorld.EntityManager.AddEntity(entity);
        var camera = entity.GetComponent<Camera>();
        output.Camera = camera;
        return camera;
    }


    public void DrawDebugCamera(Effect effect, Camera camera, GraphicsDevice drawingContext, MeshGeometryCache geometryCache, AppTime appTime)
    {
        if (SelectedCamera == null || !SelectedCamera.IsEnabled)
        {
            return;
        }

        var cameraRender = CameraVisual.GetComponent<MeshData>();
        var view = camera.ViewMatrix;
        var proj = camera.ProjectionMatrix;
        effect.Parameters["viewMatrix"].SetValue(view);
        effect.Parameters["projectionMatrix"].SetValue(proj);

        if (SelectedCamera == camera.Owner)
        {
            return;
        }

        var transform = SelectedCamera.Transform.GetMetadata(camera);
        var world = Matrix4x4F.RotationQuaternion(camera.Rotation) * transform.WorldMatrixF;
        effect.Parameters["transparency"].SetValue(1.0f);
        effect.Parameters["worldMatrix"].SetValue(world);
        effect.Parameters["wvp"].SetValue(world * view * proj);
        effect.Parameters["meshColor"].SetValue(Colors.Beige.ToVector3());
        effect.Techniques["MeshVertex"].Passes["NoLight"].Apply();
        geometryCache.DrawMesh(drawingContext, cameraRender);
    }
}
