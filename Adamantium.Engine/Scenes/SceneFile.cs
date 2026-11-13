using System;
using System.Collections.Generic;
using System.ComponentModel;
using System.IO;
using System.Text;
using System.Xml;
using System.Xml.Serialization;

namespace Adamantium.Engine.Scenes;

/// <summary>
/// A scene as its file (<c>.ascene</c>) holds it, in readable XML: what is put in the scene, where and which way it
/// faces - the models by their asset names, nothing more of them; what no file holds - lights, cameras, spawn points,
/// groups and empty entities, procedural shapes by their recipes, the components of the game's code - described whole.
/// Each kind has a section of its own. Every entry has an id the others and the patches of mods refer to it by; an
/// entry stands in another by that one's id, so the file stays flat however deep the scene.
/// </summary>
[XmlRoot("Scene")]
public sealed class SceneFile
{
    /// <summary>The extension of a scene file.</summary>
    public const string FileExtension = ".ascene";

    /// <summary>The version of the format this engine writes; a file of a later one is refused.</summary>
    public const int FormatVersion = 1;

    private static readonly XmlSerializer Serializer = new(typeof(SceneFile));

    [XmlAttribute]
    public int Version { get; set; } = FormatVersion;

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

    [XmlArray("Behaviours"), XmlArrayItem("Behaviour")]
    public List<SceneBehaviour> Behaviours { get; set; } = [];

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
    public bool ShouldSerializeBehaviours() => Behaviours.Count > 0;

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
        var settings = new XmlReaderSettings { DtdProcessing = DtdProcessing.Prohibit, XmlResolver = null };
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
    public void Write(string path)
    {
        var directory = Path.GetDirectoryName(Path.GetFullPath(path));
        Directory.CreateDirectory(directory);
        var writing = Path.Combine(directory, $".{Path.GetFileName(path)}.{Guid.NewGuid():N}.writing");
        try
        {
            using (var stream = File.Create(writing))
            {
                Write(stream);
            }

            File.Move(writing, path, true);
        }
        finally
        {
            File.Delete(writing);
        }
    }

    /// <summary>Writes the scene to <paramref name="stream"/>.</summary>
    public void Write(Stream stream)
    {
        ArgumentNullException.ThrowIfNull(stream);
        var settings = new XmlWriterSettings { Indent = true, Encoding = new UTF8Encoding(false) };
        using var writer = XmlWriter.Create(stream, settings);
        Serializer.Serialize(writer, this, new XmlSerializerNamespaces([XmlQualifiedName.Empty]));
    }
}
