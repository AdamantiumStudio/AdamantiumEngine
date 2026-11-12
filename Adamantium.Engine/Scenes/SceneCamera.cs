using System.ComponentModel;
using System.Xml.Serialization;
using Adamantium.ECS.Components;
using Adamantium.Graphics.Core;

namespace Adamantium.Engine.Scenes;

/// <summary>A camera of a scene, described whole: no file holds it. Its rotation is which way it faces, as for every
/// other entry; one that follows something stands in it (<see cref="ScenePlacement.Parent"/>) and has a third-person
/// <see cref="Type"/>.</summary>
public sealed class SceneCamera : ScenePlacement
{
    /// <summary>How it moves: free, first-person, third-person…</summary>
    [XmlIgnore]
    public CameraType Type { get; set; } = CameraType.Free;

    [XmlAttribute]
    public CameraProjectionType Projection { get; set; } = CameraProjectionType.Perspective;

    /// <summary>The field of view of a perspective camera, in degrees.</summary>
    [XmlAttribute]
    public float FieldOfView { get; set; } = 45;

    [XmlAttribute]
    public float Near { get; set; } = 0.1f;

    [XmlAttribute]
    public float Far { get; set; } = 1000;

    /// <summary>How much of the world an orthographic camera takes in, along each axis.</summary>
    [XmlIgnore]
    public Vector2F OrthoScale { get; set; } = Vector2F.One;

    [XmlAttribute("Type"), EditorBrowsable(EditorBrowsableState.Never)]
    public string TypeText
    {
        get => Type.ToString();
        set => Type = CameraType.Parse(value);
    }

    [XmlAttribute("OrthoScale"), EditorBrowsable(EditorBrowsableState.Never)]
    public string OrthoScaleText
    {
        get => SceneText.Format(OrthoScale);
        set => OrthoScale = Vector2F.Parse(value);
    }

    [EditorBrowsable(EditorBrowsableState.Never)]
    public bool ShouldSerializeOrthoScaleText() => Projection == CameraProjectionType.Ortho;
}
