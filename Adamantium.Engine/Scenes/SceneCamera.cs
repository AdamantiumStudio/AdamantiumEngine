using System;
using System.ComponentModel;
using System.Xml.Serialization;
using Adamantium.ECS.Components;

namespace Adamantium.Engine.Scenes;

/// <summary>A camera of a scene, described whole: no file holds it.</summary>
public sealed class SceneCamera : ScenePlacement
{
    [XmlAttribute]
    public CameraProjectionType Projection { get; set; } = CameraProjectionType.Perspective;

    /// <summary>The field of view of a perspective camera, in degrees.</summary>
    [XmlAttribute]
    public float FieldOfView { get; set; } = 60;

    [XmlAttribute]
    public float Near { get; set; } = 0.1f;

    [XmlAttribute]
    public float Far { get; set; } = 10000;

    /// <summary>How much of the world an orthographic camera takes in, along each axis.</summary>
    [XmlIgnore]
    public Vector2F OrthoScale { get; set; } = Vector2F.One;

    /// <summary>The id of what the camera keeps looking at; null for none.</summary>
    [XmlIgnore]
    public UInt128? Follows { get; set; }

    [XmlAttribute("OrthoScale"), EditorBrowsable(EditorBrowsableState.Never)]
    public string OrthoScaleText
    {
        get => SceneText.Format(OrthoScale);
        set => OrthoScale = Vector2F.Parse(value);
    }

    [XmlAttribute("Follows"), EditorBrowsable(EditorBrowsableState.Never)]
    public string FollowsText
    {
        get => Follows is { } follows ? SceneText.Format(follows) : null;
        set => Follows = string.IsNullOrEmpty(value) ? null : SceneText.ParseId(value);
    }

    [EditorBrowsable(EditorBrowsableState.Never)]
    public bool ShouldSerializeOrthoScaleText() => Projection == CameraProjectionType.Ortho;

    [EditorBrowsable(EditorBrowsableState.Never)]
    public bool ShouldSerializeFollowsText() => Follows.HasValue;
}
