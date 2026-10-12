using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Security.Cryptography;
using System.Text;
using System.Text.Json;
using Adamantium.Engine.Compiler.Models;
using Adamantium.Graphics.Core.Models;

namespace Adamantium.Content.Pipeline;

/// <summary>
/// Incremental build cache (the <c>.acache</c> file). Records, per asset source, a content hash of the
/// source plus a hash of its import parameters and the cooked output path, so the builder can skip assets
/// whose source/params are unchanged and whose output still exists.
/// </summary>
public sealed class BuildCache
{
    public Dictionary<string, CacheEntry> Entries { get; set; } = new();

    /// <summary>The version of the pipeline that built what the cache records. Another version cooks it all again.</summary>
    public string Compiler { get; set; }

    /// <summary>The pipeline's own version, which <see cref="Compiler"/> is compared with: the identities of the
    /// assemblies that shape the output - importers and writers, the model converter, the scene format. A deterministic
    /// build gives an assembly a new identity only when its code changes.</summary>
    public static string CurrentCompiler { get; } = Identity([typeof(BuildCache), typeof(ModelConverter), typeof(SceneData)]);

    /// <summary>Loads the cache for a build into <paramref name="outputDirectory"/>. One written by another version of the
    /// pipeline is dropped together with everything it recorded, so all is cooked again.</summary>
    public static BuildCache LoadFor(string path, string outputDirectory)
    {
        var cache = Load(path);
        if (cache.Compiler == CurrentCompiler)
        {
            return cache;
        }

        cache.Prune([], outputDirectory);
        return new BuildCache { Compiler = CurrentCompiler };
    }

    public static BuildCache Load(string path)
    {
        if (!File.Exists(path))
        {
            return new BuildCache();
        }

        return JsonSerializer.Deserialize<BuildCache>(File.ReadAllText(path)) ?? new BuildCache();
    }

    public void Save(string path)
    {
        var dir = Path.GetDirectoryName(path);
        if (!string.IsNullOrEmpty(dir))
        {
            Directory.CreateDirectory(dir);
        }

        File.WriteAllText(path, JsonSerializer.Serialize(this, new JsonSerializerOptions { WriteIndented = true }));
    }

    public bool IsStale(string sourceKey, string sourceFullPath, IReadOnlyDictionary<string, string> parameters, string outputFullPath)
    {
        if (!Entries.TryGetValue(sourceKey, out var entry))
        {
            return true;
        }

        if (!File.Exists(outputFullPath))
        {
            return true;
        }

        return entry.SourceHash != HashFile(sourceFullPath) || entry.ParametersHash != HashParameters(parameters);
    }

    public void Update(string sourceKey, string sourceFullPath, IReadOnlyDictionary<string, string> parameters,
        string outputRelativePath, IEnumerable<string> companions = null)
    {
        Entries[sourceKey] = new CacheEntry
        {
            SourceHash = HashFile(sourceFullPath),
            ParametersHash = HashParameters(parameters),
            Output = outputRelativePath,
            Companions = companions?.ToList() ?? []
        };
    }

    /// <summary>Forgets every source not in <paramref name="sources"/> and deletes what was built from it, unless a
    /// source still listed wrote the same file. Nothing the cache did not record is touched.</summary>
    public void Prune(IEnumerable<string> sources, string outputDirectory)
    {
        var kept = new HashSet<string>(sources, StringComparer.OrdinalIgnoreCase);
        var gone = Entries.Keys.Where(source => !kept.Contains(source)).ToList();
        if (gone.Count == 0)
        {
            return;
        }

        var stillWritten = new HashSet<string>(
            Entries.Where(pair => kept.Contains(pair.Key)).SelectMany(pair => Files(pair.Value)),
            StringComparer.OrdinalIgnoreCase);

        foreach (var source in gone)
        {
            foreach (var file in Files(Entries[source]).Where(file => !stillWritten.Contains(file)))
            {
                var full = Path.GetFullPath(Path.Combine(outputDirectory, file));
                if (IsInside(outputDirectory, full) && File.Exists(full))
                {
                    File.Delete(full);
                }
            }

            Entries.Remove(source);
        }
    }

    private static string Identity(Type[] types)
    {
        return string.Join("+", types.Select(type => type.Assembly.ManifestModule.ModuleVersionId.ToString("N")));
    }

    internal static bool IsInside(string directory, string path)
    {
        var relative = Path.GetRelativePath(Path.GetFullPath(directory), Path.GetFullPath(path));
        return relative != ".." && !relative.StartsWith(".." + Path.DirectorySeparatorChar)
               && !relative.StartsWith("../") && !Path.IsPathRooted(relative);
    }

    private static IEnumerable<string> Files(CacheEntry entry)
    {
        return entry.Companions.Prepend(entry.Output).Where(file => !string.IsNullOrEmpty(file));
    }

    private static string HashFile(string path)
    {
        using var stream = File.OpenRead(path);
        using var sha = SHA256.Create();
        return Convert.ToHexString(sha.ComputeHash(stream));
    }

    private static string HashParameters(IReadOnlyDictionary<string, string> parameters)
    {
        if (parameters == null || parameters.Count == 0)
        {
            return string.Empty;
        }

        var builder = new StringBuilder();
        foreach (var pair in parameters.OrderBy(p => p.Key, StringComparer.Ordinal))
        {
            builder.Append(pair.Key).Append('=').Append(pair.Value).Append(';');
        }

        using var sha = SHA256.Create();
        return Convert.ToHexString(sha.ComputeHash(Encoding.UTF8.GetBytes(builder.ToString())));
    }
}
