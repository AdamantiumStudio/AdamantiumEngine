using System;
using System.IO;
using System.Threading.Tasks;
using Adamantium.Engine.Compiler.Models;
using Adamantium.Graphics.Core.Assets;
using Adamantium.Graphics.Core.Models;

namespace Adamantium.Multiverse;

/// <summary>
/// Loads a model into <see cref="SceneData"/>: a baked .aemf through <see cref="SceneDataSerializer"/>, a raw model
/// through <see cref="ModelConverter"/>. The resolver picks the file; building an Entity from it is the caller's job.
/// </summary>
public class SceneDataAssetReader : IAssetReader
{
    public Task<object> ReadAssetAsync(IAssetService assets, AssetReaderParameters parameters)
    {
        SceneData scene;
        if (parameters.AssetPath != null &&
            parameters.AssetPath.EndsWith(".aemf", StringComparison.OrdinalIgnoreCase))
        {
            using var stream = File.OpenRead(parameters.AssetPath);
            scene = SceneDataSerializer.Deserialize(stream);
        }
        else
        {
            scene = new ModelConverter().ImportFile(parameters.AssetPath);
        }

        if (scene != null)
        {
            scene.Name = Path.GetFileName(parameters.AssetName);
            scene.Source = parameters.AssetName;
            PointImagesAtTheModel(scene, parameters.AssetPath);
        }

        return Task.FromResult((object)scene);
    }

    // A baked scene keeps only relative image paths; they resolve against the file actually opened, since a cooked
    // artifact has its textures beside it.
    private static void PointImagesAtTheModel(SceneData scene, string assetPath)
    {
        if (scene.Images == null || string.IsNullOrEmpty(assetPath)) return;

        var beside = Path.GetDirectoryName(Path.GetFullPath(assetPath)) ?? string.Empty;

        foreach (var image in scene.Images.Values)
        {
            if (string.IsNullOrEmpty(image?.ImageName)) continue;

            image.FilePath = Path.GetFullPath(Path.Combine(beside, image.ImageName));
        }
    }
}
