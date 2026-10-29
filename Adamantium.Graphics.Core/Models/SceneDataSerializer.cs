using System.IO;
using Adamantium.Graphics.Core.Assets;
using MessagePack;
using MessagePack.Resolvers;

namespace Adamantium.Graphics.Core.Models;

/// <summary>
/// (De)serializes a <see cref="SceneData"/> graph - the body of a baked model (.aemf, an <see cref="AssetFile"/>) -
/// using MessagePack's contractless resolver plus a custom <see cref="MeshFormatter"/>. Back-references skipped
/// during serialization (Model.Parent) and the mesh lookup tables are restored via
/// <see cref="SceneData.RebuildHierarchy"/> after deserialization.
/// </summary>
public static class SceneDataSerializer
{
    private static readonly MessagePackSerializerOptions Options =
        MessagePackSerializerOptions.Standard.WithResolver(
            CompositeResolver.Create(
                [
                    new MeshFormatter(),
                    new ColorFormatter(),
                    // Custom Dictionary<,>/List<> subclasses (MessagePack would otherwise mis-resolve them
                    // via the non-generic collection formatter).
                    new DictionarySubclassFormatter<SceneData.ImageCollection, string, SceneData.Image>(),
                    new DictionarySubclassFormatter<SceneData.ControllerCollection, string, SceneData.Controller>(),
                    new DictionarySubclassFormatter<SceneData.MaterialCollection, string, SceneData.Material>(),
                    new DictionarySubclassFormatter<SceneData.CameraCollection, string, SceneData.Camera>(),
                    new DictionarySubclassFormatter<SceneData.LightCollection, string, SceneData.Light>(),
                    new DictionarySubclassFormatter<SceneData.SamplerCollection, string, SceneData.Sampler>(),
                    new DictionarySubclassFormatter<SceneData.TextureCollection, string, SceneData.Texture>(),
                    new DictionarySubclassFormatter<SceneData.AnimationCollection, string, SceneData.FrameCollection>(),
                    new DictionarySubclassFormatter<SceneData.AnimationSetCollection, string, SceneData.AnimationCollection>(),
                    new ListSubclassFormatter<SceneData.FrameCollection, SceneData.KeyFrame>()
                ],
                [ContractlessStandardResolver.Instance]))
            .WithCompression(MessagePackCompression.Lz4BlockArray);

    public static byte[] Serialize(SceneData scene) => MessagePackSerializer.Serialize(scene, Options);

    public static void Serialize(Stream stream, SceneData scene) =>
        MessagePackSerializer.Serialize(stream, scene, Options);

    public static SceneData Deserialize(byte[] data)
    {
        var scene = MessagePackSerializer.Deserialize<SceneData>(data, Options);
        scene?.RebuildHierarchy();
        return scene;
    }

    public static SceneData Deserialize(Stream stream)
    {
        var scene = MessagePackSerializer.Deserialize<SceneData>(stream, Options);
        scene?.RebuildHierarchy();
        return scene;
    }

    /// <summary>Reads the model of the baked model file at <paramref name="path"/>. Throws
    /// <see cref="AssetFormatException"/> when this engine does not read the file or the model in it.</summary>
    public static SceneData ReadAssetFile(string path)
    {
        var body = AssetFile.Read(path, AssetFileSection.Body).Body
                   ?? throw new AssetFormatException($"{path}: the file holds no model; import its source again");
        try
        {
            return Deserialize(body);
        }
        catch (MessagePackSerializationException exception)
        {
            throw new AssetFormatException($"{path}: its model cannot be read; import its source again", exception);
        }
    }
}
