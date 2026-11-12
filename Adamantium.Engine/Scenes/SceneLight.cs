using System.ComponentModel;
using System.Xml.Serialization;
using Adamantium.Graphics.Core.Models;

namespace Adamantium.Engine.Scenes;

/// <summary>A light of a scene, described whole: no file holds it.</summary>
public sealed class SceneLight : ScenePlacement
{
    [XmlAttribute]
    public LightType Type { get; set; } = LightType.Point;

    [XmlIgnore]
    public Vector3F Color { get; set; } = Vector3F.One;

    [XmlAttribute("Color"), EditorBrowsable(EditorBrowsableState.Never)]
    public string ColorText
    {
        get => SceneText.Format(Color);
        set => Color = Vector3F.Parse(value);
    }

    [XmlAttribute]
    public float Intensity { get; set; } = 1;

    /// <summary>How far a point light or a spot reaches; 1 unless the file says, as a new light has it.</summary>
    [XmlAttribute]
    public float Range { get; set; } = 1;

    /// <summary>The angle, in degrees, of a spot's full light.</summary>
    [XmlAttribute]
    public float InnerSpotAngle { get; set; }

    /// <summary>The angle, in degrees, where a spot's light ends; 45 unless the file says, as a new light has it.
    /// </summary>
    [XmlAttribute]
    public float OuterSpotAngle { get; set; } = 45;

    [XmlAttribute]
    public bool Shadows { get; set; }

    /// <summary>The side of its shadow map, in texels.</summary>
    [XmlAttribute]
    public int ShadowMapResolution { get; set; }
}
