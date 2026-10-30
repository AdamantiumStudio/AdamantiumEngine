using System.IO;
using Adamantium.Engine.Compiler.Models;
using Adamantium.Graphics.Core.Models;

namespace Adamantium.Content.Pipeline.Importers;

/// <summary>
/// Imports a 3D model file (Collada/OBJ/3DS/glTF) into engine-native <c>SceneData</c> via the existing
/// <see cref="ModelConverter"/>. Mesh optimization/normal+tangent generation happen inside the converter,
/// so no separate processor is needed yet.
/// </summary>
[ContentImporter("Model", ".dae", ".obj", ".3ds", ".gltf", ".glb")]
public sealed class ModelImporter : IContentImporter
{
    public object Import(string sourcePath, ContentBuildContext context)
    {
        var converter = new ModelConverter();
        // Null when the file could not be read as a model at all; written on, it made an empty asset file in silence.
        var scene = converter.ImportFile(sourcePath)
                    ?? throw new InvalidDataException("the file could not be read as a model: it is damaged or not of its kind");

        // Otherwise part of the model goes missing in silence, and the file looks whole until it is seen in engine.
        foreach (var unsupported in converter.UnsupportedFeatures)
        {
            context.Log($"{sourcePath} : warning ACB005: {unsupported}");
        }

        CopyTextures(scene, sourcePath, context);
        return scene;
    }

    /// <summary>Textures sit next to the source model, while the cooked artifact goes to another folder. The image
    /// reference inside it is relative - so the images have to travel with it, or the model arrives black. An absolute
    /// reference to a texture in the model's folder is made relative, so it works on another machine.</summary>
    private static void CopyTextures(SceneData scene, string sourcePath, ContentBuildContext context)
    {
        if (scene?.Images == null) return;

        var beside = Path.GetDirectoryName(Path.GetFullPath(sourcePath)) ?? string.Empty;

        foreach (var image in scene.Images.Values)
        {
            if (string.IsNullOrEmpty(image?.ImageName)) continue;

            var texture = Path.GetFullPath(Path.Combine(beside, image.ImageName));
            if (!File.Exists(texture))
            {
                context.Log($"{sourcePath} : warning ACB006: the texture {image.ImageName} is not found");
                continue;
            }

            if (Path.IsPathRooted(image.ImageName) && BuildCache.IsInside(beside, texture))
            {
                image.ImageName = Path.GetRelativePath(beside, texture).Replace('\\', '/');
            }

            context.CopyAlongside(texture, image.ImageName);
        }
    }
}
