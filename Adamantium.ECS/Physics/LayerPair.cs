using System.Xml.Serialization;

namespace Adamantium.ECS.Physics;

/// <summary>Two layers of a project's physics, by their names.</summary>
public sealed class LayerPair
{
    [XmlAttribute]
    public string First { get; set; }

    [XmlAttribute]
    public string Second { get; set; }
}
