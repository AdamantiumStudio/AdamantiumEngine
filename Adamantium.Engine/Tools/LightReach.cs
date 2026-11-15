using Adamantium.ECS.Components;

namespace Adamantium.Engine.Tools;

/// <summary>How far a light reaches, as the editor's light handles change it: its range and its outer spot angle.</summary>
/// <param name="Range">The light's range.</param>
/// <param name="OuterSpotAngle">The light's outer spot angle.</param>
public readonly record struct LightReach(float Range, float OuterSpotAngle)
{
    /// <summary>How far <paramref name="light"/> reaches now.</summary>
    public static LightReach Of(Light light) => new(light.Range, light.OuterSpotAngle);

    /// <summary>Gives <paramref name="light"/> this reach; on the world's thread.</summary>
    public void ApplyTo(Light light)
    {
        light.Range = Range;
        light.OuterSpotAngle = OuterSpotAngle;
    }
}
