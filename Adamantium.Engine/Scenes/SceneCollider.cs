using System;
using System.ComponentModel;
using System.Xml.Serialization;
using Adamantium.Graphics.Core.Models;
using Adamantium.Mathematics;

namespace Adamantium.Engine.Scenes;

/// <summary>The collider of a scene's shape, in place of the one its template makes: of this kind -
/// <see cref="ColliderKind.None"/> for none - and of these sizes and placement, each written only when it is not a new
/// collider's own.</summary>
public sealed class SceneCollider
{
    private static readonly ColliderShapeEdits Defaults = new();

    // A kind this engine does not know, kept as read so the entry is written back as it was.
    private string _unknownKind;

    /// <summary>The kind; null for one this engine does not know, which leaves the template's.</summary>
    [XmlIgnore]
    public ColliderKind? Kind { get; set; }

    /// <summary>The sizes and placement; null for a collider fitted to the mesh as its template makes it.</summary>
    [XmlIgnore]
    public ColliderShapeEdits Shape { get; set; }

    /// <summary>Whether it is a trigger, its layer and material; null for the defaults.</summary>
    [XmlIgnore]
    public ColliderTraits Traits { get; set; }

    [XmlAttribute("Kind"), EditorBrowsable(EditorBrowsableState.Never)]
    public string KindText
    {
        get => Kind?.ToString() ?? _unknownKind;
        set
        {
            Kind = Enum.TryParse<ColliderKind>(value, out var kind) && Enum.IsDefined(kind) ? kind : null;
            _unknownKind = Kind == null ? value : null;
        }
    }

    [XmlAttribute("Fitted"), EditorBrowsable(EditorBrowsableState.Never)]
    public bool IsFittedValue
    {
        get => Shape?.IsFitted ?? Defaults.IsFitted;
        set => Read().IsFitted = value;
    }

    [XmlAttribute("Position"), EditorBrowsable(EditorBrowsableState.Never)]
    public string PositionText
    {
        get => SceneText.Format(Shape?.Position ?? Defaults.Position);
        set => Read().Position = Vector3F.Parse(value);
    }

    [XmlAttribute("Rotation"), EditorBrowsable(EditorBrowsableState.Never)]
    public string RotationText
    {
        get => SceneText.Format(Shape?.Rotation ?? Defaults.Rotation);
        set => Read().Rotation = QuaternionF.Parse(value);
    }

    [XmlAttribute("Scale"), EditorBrowsable(EditorBrowsableState.Never)]
    public string ScaleText
    {
        get => SceneText.Format(Shape?.Scale ?? Defaults.Scale);
        set => Read().Scale = Vector3F.Parse(value);
    }

    [XmlAttribute("Size"), EditorBrowsable(EditorBrowsableState.Never)]
    public string SizeText
    {
        get => SceneText.Format(Shape?.Size ?? Defaults.Size);
        set => Read().Size = Vector3F.Parse(value);
    }

    [XmlAttribute("Radius"), EditorBrowsable(EditorBrowsableState.Never)]
    public float RadiusValue
    {
        get => Shape?.Radius ?? Defaults.Radius;
        set => Read().Radius = value;
    }

    [XmlAttribute("Height"), EditorBrowsable(EditorBrowsableState.Never)]
    public float HeightValue
    {
        get => Shape?.Height ?? Defaults.Height;
        set => Read().Height = value;
    }

    // An axis this engine does not know reads as the default; the entry's other values still count.
    [XmlAttribute("Axis"), EditorBrowsable(EditorBrowsableState.Never)]
    public string AxisText
    {
        get => (Shape?.Axis ?? Defaults.Axis).ToString();
        set => Read().Axis = Enum.TryParse<ColliderAxis>(value, out var axis) && Enum.IsDefined(axis) ? axis : Defaults.Axis;
    }

    [XmlAttribute("ChoosesAxis"), EditorBrowsable(EditorBrowsableState.Never)]
    public bool ChoosesAxisValue
    {
        get => Shape?.ChoosesAxis ?? Defaults.ChoosesAxis;
        set => Read().ChoosesAxis = value;
    }

    [XmlAttribute("Trigger"), EditorBrowsable(EditorBrowsableState.Never)]
    public bool IsTriggerValue
    {
        get => Traits?.IsTrigger ?? false;
        set => ReadTraits().IsTrigger = value;
    }

    [XmlAttribute("Layer"), EditorBrowsable(EditorBrowsableState.Never)]
    public string LayerText
    {
        get => Traits?.Layer;
        set => ReadTraits().Layer = value;
    }

    [XmlAttribute("Material"), EditorBrowsable(EditorBrowsableState.Never)]
    public string MaterialText
    {
        get => Traits?.MaterialPath;
        set => ReadTraits().MaterialPath = value;
    }

    [EditorBrowsable(EditorBrowsableState.Never)]
    public bool ShouldSerializeIsTriggerValue() => Traits?.IsTrigger == true;

    [EditorBrowsable(EditorBrowsableState.Never)]
    public bool ShouldSerializeLayerText() => !string.IsNullOrEmpty(Traits?.Layer);

    [EditorBrowsable(EditorBrowsableState.Never)]
    public bool ShouldSerializeMaterialText() => !string.IsNullOrEmpty(Traits?.MaterialPath);

    [EditorBrowsable(EditorBrowsableState.Never)]
    public bool ShouldSerializeIsFittedValue() => Shape != null && Shape.IsFitted != Defaults.IsFitted;

    [EditorBrowsable(EditorBrowsableState.Never)]
    public bool ShouldSerializePositionText() => Shape != null && Shape.Position != Defaults.Position;

    [EditorBrowsable(EditorBrowsableState.Never)]
    public bool ShouldSerializeRotationText() => Shape != null && Shape.Rotation != Defaults.Rotation;

    [EditorBrowsable(EditorBrowsableState.Never)]
    public bool ShouldSerializeScaleText() => Shape != null && Shape.Scale != Defaults.Scale;

    [EditorBrowsable(EditorBrowsableState.Never)]
    public bool ShouldSerializeSizeText() => Shape != null && Shape.Size != Defaults.Size;

    [EditorBrowsable(EditorBrowsableState.Never)]
    public bool ShouldSerializeRadiusValue() => Shape != null && Shape.Radius != Defaults.Radius;

    [EditorBrowsable(EditorBrowsableState.Never)]
    public bool ShouldSerializeHeightValue() => Shape != null && Shape.Height != Defaults.Height;

    [EditorBrowsable(EditorBrowsableState.Never)]
    public bool ShouldSerializeAxisText() => Shape != null && Shape.Axis != Defaults.Axis;

    [EditorBrowsable(EditorBrowsableState.Never)]
    public bool ShouldSerializeChoosesAxisValue() => Shape != null && Shape.ChoosesAxis != Defaults.ChoosesAxis;

    // A value read makes the entry one with sizes and placement of its own.
    private ColliderShapeEdits Read() => Shape ??= new ColliderShapeEdits();

    private ColliderTraits ReadTraits() => Traits ??= new ColliderTraits();
}
