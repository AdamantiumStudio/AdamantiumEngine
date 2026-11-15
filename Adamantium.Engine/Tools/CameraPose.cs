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

    /// <summary>Makes <paramref name="camera"/> look so at once, with no flowing into it; on the world's thread.</summary>
    public void ApplyTo(Camera camera)
    {
        camera.Rotation = Rotation;
        camera.FocusDistance = FocusDistance;
        camera.Fov = FieldOfView;
        camera.ShowProjectionAtOnce(Projection, OrthoScale);
        camera.IsIsometric = IsIsometric;
    }
}
