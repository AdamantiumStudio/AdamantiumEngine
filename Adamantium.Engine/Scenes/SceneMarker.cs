using System.Xml.Serialization;

namespace Adamantium.Engine.Scenes;

/// <summary>An empty entity or a group of a scene: where it stands and which way it faces, nothing else.</summary>
public sealed class SceneMarker : ScenePlacement
{
    [XmlAttribute]
    public SceneMarkerKind Kind { get; set; }
}
