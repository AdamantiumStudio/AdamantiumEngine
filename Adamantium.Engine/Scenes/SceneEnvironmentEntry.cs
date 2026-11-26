using System.Xml.Serialization;
using Adamantium.ECS;

namespace Adamantium.Engine.Scenes;

/// <summary>The scene's sky and how its frame is brought to the screen, as its file keeps them; a file without the section
/// has the defaults.</summary>
public sealed class SceneEnvironmentEntry
{
    [XmlAttribute]
    public SkyKind Sky { get; set; }

    [XmlAttribute]
    public float SkyStrength { get; set; } = 1;

    [XmlAttribute]
    public bool HighDynamicRange { get; set; }

    [XmlAttribute]
    public float Exposure { get; set; }

    [XmlAttribute]
    public float Glow { get; set; } = SceneEnvironment.DefaultGlow;

    /// <summary>The entry of <paramref name="environment"/>; null when it is as a new scene's, so the file leaves the
    /// section out.</summary>
    public static SceneEnvironmentEntry Of(SceneEnvironment environment)
    {
        var entry = new SceneEnvironmentEntry
        {
            Sky = environment.Sky,
            SkyStrength = environment.SkyStrength,
            HighDynamicRange = environment.HighDynamicRange,
            Exposure = environment.Exposure,
            Glow = environment.Glow
        };
        return entry.IsDefault ? null : entry;
    }

    /// <summary>Gives <paramref name="environment"/> the entry's settings.</summary>
    public void ApplyTo(SceneEnvironment environment)
    {
        environment.Sky = Sky;
        environment.SkyStrength = SkyStrength;
        environment.HighDynamicRange = HighDynamicRange;
        environment.Exposure = Exposure;
        environment.Glow = Glow;
    }

    private bool IsDefault => Sky == SkyKind.None && SkyStrength == 1 && !HighDynamicRange && Exposure == 0
                              && Glow == SceneEnvironment.DefaultGlow;
}
