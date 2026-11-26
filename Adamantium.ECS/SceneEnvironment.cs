using Adamantium.MVVM;

namespace Adamantium.ECS;

/// <summary>How a scene is lit and shown beyond its lights: the sky round it, which physical surfaces take their light
/// and reflections from, and how the frame is brought to the screen. Each scene has its own, saved with it.</summary>
[ViewModel]
public sealed partial class SceneEnvironment
{
    /// <summary>The default share of the light that spreads into the glow.</summary>
    public const float DefaultGlow = 0.04f;

    public SceneEnvironment()
    {
        SkyStrength = 1;
        Glow = DefaultGlow;
    }

    /// <summary>The sky round the scene: none leaves the frame's clear color behind it and the ambient light on it.</summary>
    [Bindable]
    public partial SkyKind Sky { get; set; }

    /// <summary>How bright the sky is, as a light and as a background: 1 as it is made.</summary>
    [Bindable]
    public partial float SkyStrength { get; set; }

    /// <summary>Whether the scene is lit in linear light without a ceiling, then brought to the screen by the AgX tone
    /// curve - bright colors go to white as on film - its brightest light spreading into a glow. Off, every surface is
    /// drawn straight into the frame, as the screen shows it.</summary>
    [Bindable]
    public partial bool HighDynamicRange { get; set; }

    /// <summary>How many stops brighter the light is taken before the tone curve; 0 takes it as it is.</summary>
    [Bindable]
    public partial float Exposure { get; set; }

    /// <summary>The share of the light that spreads into the glow, 0 to 1, in a high dynamic range frame.</summary>
    [Bindable]
    public partial float Glow { get; set; }

    /// <summary>Takes every setting of <paramref name="source"/>.</summary>
    public void CopyFrom(SceneEnvironment source)
    {
        Sky = source.Sky;
        SkyStrength = source.SkyStrength;
        HighDynamicRange = source.HighDynamicRange;
        Exposure = source.Exposure;
        Glow = source.Glow;
    }
}

/// <summary>A sky a scene can have, made by the engine itself.</summary>
public enum SkyKind
{
    /// <summary>No sky: the frame's clear color behind the scene, its ambient light on it.</summary>
    None,

    /// <summary>A soft studio: light from above, a pale horizon, a darker floor below.</summary>
    Studio,

    /// <summary>Space: black, scattered with stars and a faint band of the galaxy.</summary>
    Stars,

    /// <summary>The same light from every side, as under an overcast.</summary>
    Even
}
