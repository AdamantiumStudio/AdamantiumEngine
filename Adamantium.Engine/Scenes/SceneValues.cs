using System.Collections.Generic;
using System.Xml;

namespace Adamantium.Engine.Scenes;

/// <summary>A component's field values as the attributes of its scene entry, and back.</summary>
internal static class SceneValues
{
    public static XmlAttribute[] ToAttributes(List<SceneValue> values)
    {
        var document = new XmlDocument();
        var attributes = new XmlAttribute[values.Count];
        for (var i = 0; i < values.Count; i++)
        {
            attributes[i] = document.CreateAttribute(values[i].Name);
            attributes[i].Value = values[i].Text;
        }

        return attributes;
    }

    public static void FromAttributes(XmlAttribute[] attributes, List<SceneValue> values)
    {
        values.Clear();
        if (attributes == null) return;

        foreach (var attribute in attributes)
        {
            values.Add(new SceneValue(attribute.Name, attribute.Value));
        }
    }
}
