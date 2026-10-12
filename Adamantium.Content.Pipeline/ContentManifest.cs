using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Text.Encodings.Web;
using System.Text.Json;
using System.Text.Json.Serialization;
using System.Text.Unicode;

namespace Adamantium.Content.Pipeline;

/// <summary>
/// The content build description (the MonoGame <c>.mgcb</c> analog): the list of assets plus the build
/// output locations. The asset list can be bootstrapped/refreshed from a folder scan
/// (<see cref="ContentBuilder.ScanInto"/>); per-asset import settings live in <see cref="ContentAsset.Parameters"/>.
/// Serialized as JSON (e.g. <c>Content.acontent</c>).
/// </summary>
public sealed class ContentManifest
{
    private static readonly JsonSerializerOptions JsonOptions = new()
    {
        WriteIndented = true,
        DefaultIgnoreCondition = JsonIgnoreCondition.WhenWritingDefault,
        // Write non-ASCII text (e.g. Cyrillic asset names) literally instead of \uXXXX escapes.
        Encoder = JavaScriptEncoder.Create(UnicodeRanges.All)
    };

    /// <summary>Cooked output directory, relative to the project directory.</summary>
    public string OutputDirectory { get; set; } = "Content";

    /// <summary>Intermediate/cache directory, relative to the project directory.</summary>
    public string IntermediateDirectory { get; set; } = "obj/Content";

    public List<ContentAsset> Assets { get; set; } = [];

    /// <summary>Makes the asset list exactly <paramref name="sources"/>, paths relative to the project directory,
    /// keeping the settings of those already listed.</summary>
    public void SetSources(IEnumerable<string> sources)
    {
        var listed = new Dictionary<string, ContentAsset>(StringComparer.OrdinalIgnoreCase);
        foreach (var asset in Assets)
        {
            listed[Normalize(asset.Source)] = asset;
        }

        Assets = sources
            .Select(Normalize)
            .Distinct(StringComparer.OrdinalIgnoreCase)
            .Select(source => listed.TryGetValue(source, out var asset) ? asset : new ContentAsset { Source = source })
            .ToList();
    }

    public static ContentManifest Load(string path)
    {
        return JsonSerializer.Deserialize<ContentManifest>(File.ReadAllText(path), JsonOptions)
               ?? new ContentManifest();
    }

    public void Save(string path)
    {
        var dir = Path.GetDirectoryName(path);
        if (!string.IsNullOrEmpty(dir))
        {
            Directory.CreateDirectory(dir);
        }

        File.WriteAllText(path, JsonSerializer.Serialize(this, JsonOptions));
    }

    private static string Normalize(string path) => path.Replace('\\', '/');
}
