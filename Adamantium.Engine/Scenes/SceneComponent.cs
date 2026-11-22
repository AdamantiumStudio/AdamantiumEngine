using System;
using System.Collections.Generic;
using System.ComponentModel;
using System.Xml;
using System.Xml.Serialization;

namespace Adamantium.Engine.Scenes;

/// <summary>A component on something put in a scene, kept by its type and the values of its fields as the file writes
/// them: one of the game's or a mod's code, or one of the engine's that no section of an entry keeps. Kept as it is when
/// its type is not loaded - the code not built, the mod off - so the scene does not lose it.</summary>
public sealed class SceneComponent
{
    /// <summary>The entry's own attributes, which no field of the component can be named.</summary>
    public static readonly IReadOnlyCollection<string> OwnAttributes = ["On", "Type"];

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
        get => SceneValues.ToAttributes(Values);
        set => SceneValues.FromAttributes(value, Values);
    }

    /// <summary>A copy of the entry, on <paramref name="on"/>.</summary>
    public SceneComponent CopyOn(UInt128 on)
    {
        var copy = new SceneComponent { On = on, Type = Type };
        copy.Values.AddRange(Values);
        return copy;
    }
}
