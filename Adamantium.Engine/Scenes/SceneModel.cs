using System.ComponentModel;
using System.Xml.Serialization;

namespace Adamantium.Engine.Scenes;

/// <summary>A model put in a scene: which model, where, which way it faces, how big. Nothing else of the model - its
/// parts and their settings live in its asset file.</summary>
public sealed class SceneModel : ScenePlacement
{
    /// <summary>The model, by its asset name - the name the asset service loads it by.</summary>
    [XmlAttribute]
    public string Asset { get; set; }

    /// <summary>How much bigger than the model's own size, along each axis.</summary>
    [XmlIgnore]
    public Vector3F Scale { get; set; } = Vector3F.One;

    [XmlAttribute("Scale"), EditorBrowsable(EditorBrowsableState.Never)]
    public string ScaleText
    {
        get => SceneText.Format(Scale);
        set => Scale = Vector3F.Parse(value);
    }

    [EditorBrowsable(EditorBrowsableState.Never)]
    public bool ShouldSerializeScaleText() => Scale != Vector3F.One;
}
