using System.Collections.Generic;
using System.Linq;
using Adamantium.ECS.Components;
using Adamantium.Engine.Templates.Lights;
using Adamantium.Graphics.Core.Models;
using Adamantium.Mathematics;

namespace Adamantium.Engine.Screenshots;

/// <summary>The light a screenshot is taken in, apart from any scene's: lights of its own, which belong to no world,
/// and the light every surface gets besides.</summary>
public sealed class ScreenshotLighting
{
    public ScreenshotLighting(IReadOnlyList<Light> lights, Vector3F ambient)
    {
        Lights = lights ?? [];
        Ambient = ambient;
        DirectionalLights = [.. Lights.Where(light => light.Type == LightType.Directional)];
        PointLights = [.. Lights.Where(light => light.Type == LightType.Point)];
        SpotLights = [.. Lights.Where(light => light.Type == LightType.Spot)];
    }

    /// <summary>The lights, each the light of an entity of its own that no world holds.</summary>
    public IReadOnlyList<Light> Lights { get; }

    /// <summary>The light every surface gets whatever the lights.</summary>
    public Vector3F Ambient { get; }

    public IReadOnlyList<Light> DirectionalLights { get; }

    public IReadOnlyList<Light> PointLights { get; }

    public IReadOnlyList<Light> SpotLights { get; }

    /// <summary>Two directional lights, a key from above, front and right and a dimmer fill from the left, and a little
    /// ambient light: a model's shape reads from every side the camera of a thumbnail sees.</summary>
    public static ScreenshotLighting Studio()
    {
        var key = new LightTemplate().BuildEntity(null, "Key light", LightType.Directional);
        key.Transform.Position = new Vector3<double>(4, -8, -6);
        key.Transform.Rotation = Quaternion.CreateFromYawPitchRoll<float>(float.DegreesToRadians(-30),
            float.DegreesToRadians(140), 0);

        var fill = new LightTemplate().BuildEntity(null, "Fill light", LightType.Directional);
        fill.Transform.Position = new Vector3<double>(-6, -2, -4);
        fill.Transform.Rotation = Quaternion.CreateFromYawPitchRoll<float>(float.DegreesToRadians(50),
            float.DegreesToRadians(170), 0);
        fill.GetComponent<Light>().Intensity = 0.4f;

        return new ScreenshotLighting([key.GetComponent<Light>(), fill.GetComponent<Light>()],
            new Vector3F(0.25f, 0.25f, 0.27f));
    }
}
