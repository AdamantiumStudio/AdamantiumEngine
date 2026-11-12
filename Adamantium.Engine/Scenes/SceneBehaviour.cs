using System;
using System.Collections.Generic;
using System.ComponentModel;
using System.Xml;
using System.Xml.Serialization;

namespace Adamantium.Engine.Scenes;

/// <summary>A component of the game's or a mod's code on something put in a scene: which it is on, its type, and the
/// values of its fields as the file writes them. Kept as it is when its type is not loaded - the code not built, the mod
/// off - so the scene does not lose it.</summary>
public sealed class SceneBehaviour
{
    /// <summary>The id of the entry it is on.</summary>
    [XmlIgnore]
    public UInt128 On { get; set; }

    /// <summary>The full name of its type.</summary>
    [XmlAttribute]
    public string Type { get; set; }

    /// <summary>Its fields' values as text, in the order the file has them.</summary>
    [XmlIgnore]
    public List<SceneValue> Values { get; } = [];

    [XmlAttribute("On"), EditorBrowsable(EditorBrowsableState.Never)]
    public string OnText
    {
        get => SceneText.Format(On);
        set => On = SceneText.ParseId(value);
    }

    [XmlAnyAttribute, EditorBrowsable(EditorBrowsableState.Never)]
    public XmlAttribute[] ValueAttributes
    {
        get
        {
            var document = new XmlDocument();
            var attributes = new XmlAttribute[Values.Count];
            for (var i = 0; i < Values.Count; i++)
            {
                attributes[i] = document.CreateAttribute(Values[i].Name);
                attributes[i].Value = Values[i].Text;
            }

            return attributes;
        }
        set
        {
            Values.Clear();
            if (value == null) return;
            foreach (var attribute in value)
            {
                Values.Add(new SceneValue(attribute.Name, attribute.Value));
            }
        }
    }
}
