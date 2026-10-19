using System.Collections.Generic;
using System.IO;
using MessagePack;
using MessagePack.Resolvers;

namespace Adamantium.Graphics.Core.Models;

/// <summary>
/// (De)serializes a <see cref="SceneData"/> graph to/from the baked binary model format (.aemf) using
/// MessagePack's contractless resolver plus a custom <see cref="MeshFormatter"/>. Back-references skipped
/// during serialization (Model.Parent, Joint.ParentJoint) and the mesh lookup tables are restored via
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
                    new DictionarySubclassFormatter<SceneData.AnimationCollection, string, SceneData.FrameCollection>(),
                    new DictionarySubclassFormatter<SceneData.AnimationSetCollection, string, SceneData.AnimationCollection>(),
                    new DictionarySubclassFormatter<SceneData.SkeletonCollection, string, List<SceneData.Joint>>(),
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
}
