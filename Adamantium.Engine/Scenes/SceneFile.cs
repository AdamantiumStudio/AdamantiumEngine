using System;
using System.Collections.Generic;
using System.ComponentModel;
using System.IO;
using System.Text;
using System.Xml;
using System.Xml.Serialization;
using Adamantium.Core;

namespace Adamantium.Engine.Scenes;

/// <summary>
/// A scene as its file (<c>.ascene</c>) holds it, in readable XML: what is put in the scene, where and which way it
/// faces - the models by their asset names, nothing more of them; what no file holds - lights, cameras, spawn points,
/// groups and empty entities, procedural shapes by their recipes, components by their type and fields - described whole.
/// Each kind has a section of its own. Every entry has an id the others and the patches of mods refer to it by; an
/// entry stands in another by that one's id, so the file stays flat however deep the scene.
/// </summary>
[XmlRoot("Scene")]
public sealed class SceneFile
{
    /// <summary>The extension of a scene file.</summary>
    public const string FileExtension = Graphics.Core.Assets.AssetFileInfo.SceneExtension;

    /// <summary>The version of the format this engine writes; a file of a later one is refused. 2 keeps components by
    /// type in <see cref="Components"/>, which a reader of 1 would drop unread.</summary>
    public const int FormatVersion = 2;

    private static readonly XmlSerializer Serializer = new(typeof(SceneFile));

    /// <summary>The version the file was read in; <see cref="FormatVersion"/> for one made here. Written always as
    /// <see cref="FormatVersion"/>: what this engine writes is of its own format, whatever it read.</summary>
    [XmlIgnore]
    public int Version { get; set; } = FormatVersion;

    [XmlAttribute("Version"), EditorBrowsable(EditorBrowsableState.Never)]
    public int WrittenVersion
    {
        get => FormatVersion;
        set => Version = value;
    }

    /// <summary>The scene's own id, which it keeps across saving: the scene shown from the file is made with it. Null
    /// for a file without one - a new scene's - whose scene gets a new id.</summary>
    [XmlIgnore]
    public UInt128? Uid { get; set; }

    [XmlAttribute("Uid"), EditorBrowsable(EditorBrowsableState.Never)]
    public string UidText
    {
        get => Uid is { } uid ? SceneText.Format(uid) : null;
        set => Uid = string.IsNullOrEmpty(value) ? null : SceneText.ParseId(value);
    }

    [EditorBrowsable(EditorBrowsableState.Never)]
    public bool ShouldSerializeUidText() => Uid != null;

    [XmlArray("Models"), XmlArrayItem("Model")]
    public List<SceneModel> Models { get; set; } = [];

    [XmlArray("Lights"), XmlArrayItem("Light")]
    public List<SceneLight> Lights { get; set; } = [];

    [XmlArray("Cameras"), XmlArrayItem("Camera")]
    public List<SceneCamera> Cameras { get; set; } = [];

    [XmlArray("SpawnPoints"), XmlArrayItem("SpawnPoint")]
    public List<SceneSpawnPoint> SpawnPoints { get; set; } = [];

    [XmlArray("Markers"), XmlArrayItem("Marker")]
    public List<SceneMarker> Markers { get; set; } = [];

    [XmlArray("Shapes"), XmlArrayItem("Shape")]
    public List<SceneShape> Shapes { get; set; } = [];

    /// <summary>The components kept by their type and fields: the game's, and the engine's that no section of an entry
    /// keeps.</summary>
    [XmlArray("Components"), XmlArrayItem("Component")]
    public List<SceneComponent> Components { get; } = [];

    /// <summary>The section the components of the game's code had before <see cref="Components"/>: read into it, never
    /// written.</summary>
    [XmlArray("GameComponents"), XmlArrayItem("GameComponent"), EditorBrowsable(EditorBrowsableState.Never)]
    public List<SceneComponent> FormerGameComponents => Components;

    /// <summary>What the editor keeps of how the scene was worked on - its views' cameras, the selection - last in the
    /// file, as the editor wrote it; nothing of the engine's, which neither reads nor writes it. Null for none.</summary>
    [XmlAnyElement(EditorSection)]
    public XmlElement Editor { get; set; }

    /// <summary>The name of the <see cref="Editor"/> section's element.</summary>
    public const string EditorSection = "Editor";

    [EditorBrowsable(EditorBrowsableState.Never)]
    public bool ShouldSerializeModels() => Models.Count > 0;

    [EditorBrowsable(EditorBrowsableState.Never)]
    public bool ShouldSerializeMarkers() => Markers.Count > 0;

    [EditorBrowsable(EditorBrowsableState.Never)]
    public bool ShouldSerializeShapes() => Shapes.Count > 0;

    [EditorBrowsable(EditorBrowsableState.Never)]
    public bool ShouldSerializeLights() => Lights.Count > 0;

    [EditorBrowsable(EditorBrowsableState.Never)]
    public bool ShouldSerializeCameras() => Cameras.Count > 0;

    [EditorBrowsable(EditorBrowsableState.Never)]
    public bool ShouldSerializeSpawnPoints() => SpawnPoints.Count > 0;

    [EditorBrowsable(EditorBrowsableState.Never)]
    public bool ShouldSerializeComponents() => Components.Count > 0;

    [EditorBrowsable(EditorBrowsableState.Never)]
    public bool ShouldSerializeFormerGameComponents() => false;

    /// <summary>Puts every entry's body and collider on the physics layer <paramref name="from"/> on
    /// <paramref name="to"/>: the layer renamed.</summary>
    public void RenameLayer(string from, string to)
    {
        ArgumentException.ThrowIfNullOrEmpty(from);
        ArgumentException.ThrowIfNullOrEmpty(to);
        foreach (var placement in Placements())
        {
            if (placement.Body is { } body && body.Layer == from)
                body.Layer = to;

            if (placement.Collider?.Traits is { } traits && traits.Layer == from)
                traits.Layer = to;
        }
    }

    /// <summary>Whether an entry's body or collider is on the physics layer <paramref name="layer"/>.</summary>
    public bool NamesLayer(string layer)
    {
        foreach (var placement in Placements())
        {
            if (placement.Body?.Layer == layer || placement.Collider?.Traits?.Layer == layer)
                return true;
        }

        return false;
    }

    private IEnumerable<ScenePlacement> Placements()
    {
        foreach (var model in Models)
            yield return model;
        foreach (var light in Lights)
            yield return light;
        foreach (var camera in Cameras)
            yield return camera;
        foreach (var spawnPoint in SpawnPoints)
            yield return spawnPoint;
        foreach (var marker in Markers)
            yield return marker;
        foreach (var shape in Shapes)
            yield return shape;
    }

    /// <summary>Reads the scene file at <paramref name="path"/>.</summary>
    /// <exception cref="InvalidDataException">The file is not a scene file, or one of a later version.</exception>
    public static SceneFile Read(string path)
    {
        using var stream = File.OpenRead(path);
        try
        {
            return Read(stream);
        }
        catch (InvalidDataException exception)
        {
            throw new InvalidDataException($"{path}: {exception.Message}", exception);
        }
    }

    /// <summary>Reads a scene file from <paramref name="stream"/>.</summary>
    /// <exception cref="InvalidDataException">The stream holds no scene file, or one of a later version.</exception>
    public static SceneFile Read(Stream stream)
    {
        ArgumentNullException.ThrowIfNull(stream);
        // The layout between elements is the writer's, never data: kept, it would pile up in the editor's section with
        // every save.
        var settings = new XmlReaderSettings
        {
            DtdProcessing = DtdProcessing.Prohibit, XmlResolver = null, IgnoreWhitespace = true
        };
        SceneFile scene;
        try
        {
            using var reader = XmlReader.Create(stream, settings);
            scene = (SceneFile)Serializer.Deserialize(reader);
        }
        catch (InvalidOperationException exception)
        {
            throw new InvalidDataException($"not a scene file: {exception.InnerException?.Message ?? exception.Message}",
                exception);
        }

        if (scene.Version > FormatVersion)
        {
            throw new InvalidDataException(
                $"a scene file of version {scene.Version}; this engine reads up to {FormatVersion}");
        }

        return scene;
    }

    /// <summary>Writes the scene to <paramref name="path"/>: whole or not at all - a failed write leaves the file there
    /// was.</summary>
    public void Write(string path) => AtomicFile.Write(path, Write);

    /// <summary>Writes the scene to <paramref name="stream"/>.</summary>
    public void Write(Stream stream)
    {
        ArgumentNullException.ThrowIfNull(stream);
        var settings = new XmlWriterSettings { Indent = true, Encoding = new UTF8Encoding(false) };
        using var writer = XmlWriter.Create(stream, settings);
        Serializer.Serialize(writer, this, new XmlSerializerNamespaces([XmlQualifiedName.Empty]));
    }
}
