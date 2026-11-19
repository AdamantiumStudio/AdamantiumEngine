using System;
using System.ComponentModel;
using System.Xml.Serialization;
using Adamantium.DarkMatter;
using Adamantium.Graphics.Core.Models;
using Adamantium.Mathematics;

namespace Adamantium.Engine.Scenes;

/// <summary>The joint of a scene's entry, holding its body to another entry's or to the world: the settings of a joint
/// component, each written only when it is not a new joint's own. A joint inside a model is the model's edits'.</summary>
public sealed class SceneJoint
{
    private static readonly JointEdits Defaults = new();

    // A kind this engine does not know, kept as read so the entry is written back as it was.
    private string _unknownKind;

    /// <summary>The kind; null for one this engine does not know, which leaves the joint out.</summary>
    [XmlIgnore]
    public JointKind? Kind { get; set; } = Defaults.Kind;

    /// <summary>The id of the entry whose body it holds to; null for the world.</summary>
    [XmlIgnore]
    public UInt128? Connected { get; set; }

    [XmlIgnore]
    public Vector3F Anchor { get; set; }

    [XmlIgnore]
    public Vector3F Axis { get; set; } = Defaults.Axis;

    [XmlIgnore]
    public Vector3F ConnectedAnchor { get; set; }

    [XmlAttribute]
    public bool CollideConnected { get; set; }

    [XmlAttribute]
    public bool UsesLimits { get; set; }

    [XmlAttribute]
    public float LowerLimit { get; set; }

    [XmlAttribute]
    public float UpperLimit { get; set; }

    [XmlAttribute]
    public bool UsesMotor { get; set; }

    [XmlAttribute]
    public float MotorSpeed { get; set; }

    [XmlAttribute]
    public float MaxMotorForce { get; set; }

    [XmlAttribute]
    public float SpringHertz { get; set; }

    [XmlAttribute]
    public float SpringDampingRatio { get; set; } = Defaults.SpringDampingRatio;

    [XmlAttribute("Kind"), EditorBrowsable(EditorBrowsableState.Never)]
    public string KindText
    {
        get => Kind?.ToString() ?? _unknownKind;
        set
        {
            Kind = Enum.TryParse<JointKind>(value, out var kind) && Enum.IsDefined(kind) ? kind : null;
            _unknownKind = Kind == null ? value : null;
        }
    }

    [XmlAttribute("Connected"), EditorBrowsable(EditorBrowsableState.Never)]
    public string ConnectedText
    {
        get => Connected is { } connected ? SceneText.Format(connected) : null;
        set => Connected = string.IsNullOrEmpty(value) ? null : SceneText.ParseId(value);
    }

    [XmlAttribute("Anchor"), EditorBrowsable(EditorBrowsableState.Never)]
    public string AnchorText
    {
        get => SceneText.Format(Anchor);
        set => Anchor = Vector3F.Parse(value);
    }

    [XmlAttribute("Axis"), EditorBrowsable(EditorBrowsableState.Never)]
    public string AxisText
    {
        get => SceneText.Format(Axis);
        set => Axis = Vector3F.Parse(value);
    }

    [XmlAttribute("ConnectedAnchor"), EditorBrowsable(EditorBrowsableState.Never)]
    public string ConnectedAnchorText
    {
        get => SceneText.Format(ConnectedAnchor);
        set => ConnectedAnchor = Vector3F.Parse(value);
    }

    [EditorBrowsable(EditorBrowsableState.Never)]
    public bool ShouldSerializeKindText() => Kind != Defaults.Kind;

    [EditorBrowsable(EditorBrowsableState.Never)]
    public bool ShouldSerializeConnectedText() => Connected.HasValue;

    [EditorBrowsable(EditorBrowsableState.Never)]
    public bool ShouldSerializeAnchorText() => Anchor != Vector3F.Zero;

    [EditorBrowsable(EditorBrowsableState.Never)]
    public bool ShouldSerializeAxisText() => Axis != Defaults.Axis;

    [EditorBrowsable(EditorBrowsableState.Never)]
    public bool ShouldSerializeConnectedAnchorText() => ConnectedAnchor != Vector3F.Zero;

    [EditorBrowsable(EditorBrowsableState.Never)]
    public bool ShouldSerializeCollideConnected() => CollideConnected;

    [EditorBrowsable(EditorBrowsableState.Never)]
    public bool ShouldSerializeUsesLimits() => UsesLimits;

    [EditorBrowsable(EditorBrowsableState.Never)]
    public bool ShouldSerializeLowerLimit() => LowerLimit != 0;

    [EditorBrowsable(EditorBrowsableState.Never)]
    public bool ShouldSerializeUpperLimit() => UpperLimit != 0;

    [EditorBrowsable(EditorBrowsableState.Never)]
    public bool ShouldSerializeUsesMotor() => UsesMotor;

    [EditorBrowsable(EditorBrowsableState.Never)]
    public bool ShouldSerializeMotorSpeed() => MotorSpeed != 0;

    [EditorBrowsable(EditorBrowsableState.Never)]
    public bool ShouldSerializeMaxMotorForce() => MaxMotorForce != 0;

    [EditorBrowsable(EditorBrowsableState.Never)]
    public bool ShouldSerializeSpringHertz() => SpringHertz != 0;

    [EditorBrowsable(EditorBrowsableState.Never)]
    public bool ShouldSerializeSpringDampingRatio() => SpringDampingRatio != Defaults.SpringDampingRatio;

    /// <summary>The joint's settings, as a model's edits keep them, its other body left to <see cref="Connected"/>;
    /// null for a kind this engine does not know.</summary>
    public JointEdits ToEdits() => Kind is { } kind
        ? new JointEdits
        {
            Kind = kind, Anchor = Anchor, Axis = Axis, ConnectedAnchor = ConnectedAnchor,
            CollideConnected = CollideConnected, UsesLimits = UsesLimits, LowerLimit = LowerLimit,
            UpperLimit = UpperLimit, UsesMotor = UsesMotor, MotorSpeed = MotorSpeed, MaxMotorForce = MaxMotorForce,
            SpringHertz = SpringHertz, SpringDampingRatio = SpringDampingRatio
        }
        : null;

    /// <summary>The entry of a joint of <paramref name="edits"/>' settings, held to the entry
    /// <paramref name="connected"/> names, or to the world.</summary>
    public static SceneJoint From(JointEdits edits, UInt128? connected) => new()
    {
        Kind = edits.Kind, Connected = connected, Anchor = edits.Anchor, Axis = edits.Axis,
        ConnectedAnchor = edits.ConnectedAnchor, CollideConnected = edits.CollideConnected,
        UsesLimits = edits.UsesLimits, LowerLimit = edits.LowerLimit, UpperLimit = edits.UpperLimit,
        UsesMotor = edits.UsesMotor, MotorSpeed = edits.MotorSpeed, MaxMotorForce = edits.MaxMotorForce,
        SpringHertz = edits.SpringHertz, SpringDampingRatio = edits.SpringDampingRatio
    };
}
