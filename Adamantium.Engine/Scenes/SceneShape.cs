using System.Collections.Generic;
using System.ComponentModel;
using System.Xml;
using System.Xml.Serialization;

namespace Adamantium.Engine.Scenes;

/// <summary>A procedural shape of a scene - a cube, a cylinder, a teapot: its recipe, not its mesh. Which shape, its
/// parameters, its color, where it stands, which way it faces and how big it is.</summary>
public sealed class SceneShape : ScenePlacement
{
    /// <summary>The entry's own attributes, which no parameter of the shape can be named.</summary>
    public static readonly IReadOnlyCollection<string> OwnAttributes =
        ["Uid", "Name", "Parent", "Position", "Rotation", "Scale", "Type", "Color"];

    /// <summary>The full name of the shape's type.</summary>
    [XmlAttribute]
    public string Type { get; set; }

    [XmlIgnore]
    public Vector4F Color { get; set; } = Vector4F.One;

    [XmlIgnore]
    public Vector3F Scale { get; set; } = Vector3F.One;

    /// <summary>The shape's parameters as text, in the order the file has them.</summary>
    [XmlIgnore]
    public List<SceneValue> Values { get; } = [];

    [XmlAttribute("Color"), EditorBrowsable(EditorBrowsableState.Never)]
    public string ColorText
    {
        get => SceneText.Format(Color);
        set => Color = Vector4F.Parse(value);
    }

    [XmlAttribute("Scale"), EditorBrowsable(EditorBrowsableState.Never)]
    public string ScaleText
    {
        get => SceneText.Format(Scale);
        set => Scale = Vector3F.Parse(value);
    }

    [XmlAnyAttribute, EditorBrowsable(EditorBrowsableState.Never)]
    public XmlAttribute[] ValueAttributes
    {
        get => SceneValues.ToAttributes(Values);
        set => SceneValues.FromAttributes(value, Values);
    }

    [EditorBrowsable(EditorBrowsableState.Never)]
    public bool ShouldSerializeScaleText() => Scale != Vector3F.One;
}
