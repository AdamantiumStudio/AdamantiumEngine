using Adamantium.ECS.Components;
using Adamantium.Mathematics;

namespace Adamantium.Engine.Tools;

/// <summary>How a camera looks, besides where its entity stands: the way it faces, what it focuses on and how it
/// projects - all that steering it changes.</summary>
/// <param name="Rotation">The camera's own rotation.</param>
/// <param name="FocusDistance">How far ahead it focuses.</param>
/// <param name="Projection">How it projects.</param>
/// <param name="OrthoScale">Its scale when it projects flat.</param>
/// <param name="FieldOfView">Its field of view.</param>
/// <param name="IsIsometric">Whether it shows an isometric view.</param>
public readonly record struct CameraPose(
    QuaternionF Rotation,
    double FocusDistance,
    CameraProjectionType Projection,
    Vector2F OrthoScale,
    float FieldOfView,
    bool IsIsometric)
{
    /// <summary>How <paramref name="camera"/> looks now.</summary>
    public static CameraPose Of(Camera camera) => new(camera.Rotation, camera.FocusDistance, camera.ProjectionType,
        camera.OrthoScale, camera.Fov, camera.IsIsometric);

    /// <summary>Makes <paramref name="camera"/> look so where it differs from <paramref name="from"/> - the pose a step
    /// takes it from - on the world's thread. The projection and the isometric view change as the camera changes them
    /// when asked, flowing into the new one; the rest at once. What the step did not change is left as it is.</summary>
    public void ApplyTo(Camera camera, CameraPose from)
    {
        if (Rotation != from.Rotation)
        {
            camera.Rotation = Rotation;
        }

        if (FocusDistance != from.FocusDistance)
        {
            camera.FocusDistance = FocusDistance;
        }

        if (FieldOfView != from.FieldOfView)
        {
            camera.Fov = FieldOfView;
        }

        if (Projection != from.Projection)
        {
            camera.ProjectionType = Projection;
        }
        else if (Projection != CameraProjectionType.Perspective && OrthoScale != from.OrthoScale)
        {
            camera.OrthoScale = OrthoScale;
        }

        if (IsIsometric != from.IsIsometric)
        {
            camera.IsIsometric = IsIsometric;
        }
    }
}
