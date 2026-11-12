using System.Collections.Generic;
using System.Globalization;
using System.IO;
using Adamantium.Engine.Compiler.Converter.Configs;
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
    /// <summary>How many times larger the model is made, the whole of it from its root.</summary>
    public const string ScaleSetting = "Scale";

    /// <summary>The axis the source is up along: <see cref="FromFile"/>, <c>Y</c> or <c>Z</c>.</summary>
    public const string UpAxisSetting = "UpAxis";

    /// <summary>Where normals come from: <see cref="FromFile"/> - computed only where the file has none - or
    /// <see cref="AlwaysComputed"/>.</summary>
    public const string NormalsSetting = "Normals";

    /// <summary>Whether the model's clips are imported: <c>true</c> or <c>false</c>.</summary>
    public const string AnimationsSetting = "Animations";

    /// <summary>The value of a setting that takes what the file says.</summary>
    public const string FromFile = "File";

    /// <summary>The <see cref="NormalsSetting"/> value that computes every mesh's normals.</summary>
    public const string AlwaysComputed = "Always";

    private static readonly IReadOnlyList<ImportSetting> ModelSettings =
    [
        new(ScaleSetting, ImportSettingKind.Number, "1", "Scale"),
        new(UpAxisSetting, ImportSettingKind.Choice, FromFile, "Up axis",
            [new(FromFile, "From file"), new("Y", "Y"), new("Z", "Z")]),
        new(NormalsSetting, ImportSettingKind.Choice, FromFile, "Normals",
            [new(FromFile, "From the file, computed where missing"), new(AlwaysComputed, "Always computed")]),
        new(AnimationsSetting, ImportSettingKind.Toggle, "true", "Import the model's clips")
    ];

    public IReadOnlyList<ImportSetting> Settings => ModelSettings;

    public object Import(string sourcePath, ContentBuildContext context)
    {
        var scale = ReadScale(sourcePath, context);
        var animations = ReadChoice(sourcePath, context, AnimationsSetting, "true", "false") == "true";
        var config = new ConversionConfig(true)
        {
            SourceUpAxis = ReadChoice(sourcePath, context, UpAxisSetting, FromFile, "Y", "Z") switch
            {
                "Y" => UpAxis.Y_UP_RH,
                "Z" => UpAxis.Z_UP,
                _ => null
            },
            RecalculateNormals = ReadChoice(sourcePath, context, NormalsSetting, FromFile, AlwaysComputed) == AlwaysComputed,
            AnimationEnabled = animations
        };

        var converter = new ModelConverter();
        // Null when the file could not be read as a model at all; written on, it made an empty asset file in silence.
        var scene = converter.ImportFile(sourcePath, config)
                    ?? throw new InvalidDataException("the file could not be read as a model: it is damaged or not of its kind");

        // Otherwise part of the model goes missing in silence, and the file looks whole until it is seen in engine.
        foreach (var unsupported in converter.UnsupportedFeatures)
        {
            context.Log($"{sourcePath} : warning ACB005: {unsupported}");
        }

        if (!animations)
        {
            scene.Clips?.Clear();
            scene.AnimationSources?.Clear();
        }

        if (scale != 1f)
        {
            scene.Units ??= new SceneData.Unit();
            scene.Units.Value *= scale;
        }

        CopyTextures(scene, sourcePath, context);
        return scene;
    }

    private static float ReadScale(string sourcePath, ContentBuildContext context)
    {
        if (!context.Parameters.TryGetValue(ScaleSetting, out var text) || string.IsNullOrWhiteSpace(text))
        {
            return 1f;
        }

        if (float.TryParse(text, NumberStyles.Float, CultureInfo.InvariantCulture, out var scale) &&
            float.IsFinite(scale) && scale > 0)
        {
            return scale;
        }

        context.Log($"{sourcePath} : warning ACB011: the {ScaleSetting} '{text}' is not a number above zero; 1 is taken");
        return 1f;
    }

    private static string ReadChoice(string sourcePath, ContentBuildContext context, string name, params string[] allowed)
    {
        if (!context.Parameters.TryGetValue(name, out var text) || string.IsNullOrWhiteSpace(text))
        {
            return allowed[0];
        }

        foreach (var value in allowed)
        {
            if (string.Equals(value, text.Trim(), System.StringComparison.OrdinalIgnoreCase))
            {
                return value;
            }
        }

        context.Log($"{sourcePath} : warning ACB011: the {name} '{text}' is none of {string.Join(", ", allowed)}; " +
                    $"{allowed[0]} is taken");
        return allowed[0];
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
