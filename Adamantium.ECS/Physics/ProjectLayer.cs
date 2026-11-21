using System.Xml.Serialization;

namespace Adamantium.ECS.Physics;

/// <summary>A layer of a project's physics, by its name.</summary>
public sealed class ProjectLayer
{
    [XmlAttribute]
    public string Name { get; set; }
}
