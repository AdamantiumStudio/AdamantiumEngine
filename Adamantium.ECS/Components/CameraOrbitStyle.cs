namespace Adamantium.ECS.Components;

/// <summary>How <see cref="Camera.Orbit"/> turns a camera about a point.</summary>
public enum CameraOrbitStyle
{
    /// <summary>About the world's vertical and the camera's own sideways axis: the horizon stays level, and the camera
    /// stops short of straight up or down, as a free camera's look does.</summary>
    Turntable,

    /// <summary>About the camera's own axes, as a ball rolled under the hand: nothing holds it level, and it goes over
    /// the top.</summary>
    Trackball
}
