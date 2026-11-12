using System;
using System.ComponentModel;
using System.Xml.Serialization;
using Adamantium.Mathematics;

namespace Adamantium.Engine.Scenes;

/// <summary>Something put in a scene: its id, its name, where it stands and which way it faces, and what it stands in.
/// </summary>
public abstract class ScenePlacement
{
    /// <summary>Its id: what other entries and the patches of mods refer to it by.</summary>
    [XmlIgnore]
    public UInt128 Uid { get; set; }

    [XmlAttribute]
    public string Name { get; set; } = "";

    /// <summary>The id of what it stands in - an entry of any section; null for one standing in the scene itself.
    /// </summary>
    [XmlIgnore]
    public UInt128? Parent { get; set; }

    /// <summary>Where it stands, relative to its parent.</summary>
    [XmlIgnore]
    public Vector3<double> Position { get; set; }

    /// <summary>Which way it faces, relative to its parent.</summary>
    [XmlIgnore]
    public QuaternionF Rotation { get; set; } = QuaternionF.Identity;

    [XmlAttribute("Uid"), EditorBrowsable(EditorBrowsableState.Never)]
    public string UidText
    {
        get => SceneText.Format(Uid);
        set => Uid = SceneText.ParseId(value);
    }

    [XmlAttribute("Parent"), EditorBrowsable(EditorBrowsableState.Never)]
    public string ParentText
    {
        get => Parent is { } parent ? SceneText.Format(parent) : null;
        set => Parent = string.IsNullOrEmpty(value) ? null : SceneText.ParseId(value);
    }

    [XmlAttribute("Position"), EditorBrowsable(EditorBrowsableState.Never)]
    public string PositionText
    {
        get => SceneText.Format(Position);
        set => Position = Vector3<double>.Parse(value);
    }

    [XmlAttribute("Rotation"), EditorBrowsable(EditorBrowsableState.Never)]
    public string RotationText
    {
        get => SceneText.Format(Rotation);
        set => Rotation = QuaternionF.Parse(value);
    }

    [EditorBrowsable(EditorBrowsableState.Never)]
    public bool ShouldSerializeParentText() => Parent.HasValue;

    [EditorBrowsable(EditorBrowsableState.Never)]
    public bool ShouldSerializeRotationText() => Rotation != QuaternionF.Identity;
}
