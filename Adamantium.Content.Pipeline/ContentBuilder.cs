using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Reflection;
using Adamantium.Content.Pipeline.Importers;
using Adamantium.Content.Pipeline.Writers;
using Adamantium.Graphics.Core.Assets;

namespace Adamantium.Content.Pipeline;

/// <summary>
/// Orchestrates the content build: resolves an importer (and writer) per asset, runs
/// importer → (optional) processor → writer, and cooks incrementally against a <see cref="BuildCache"/>.
/// Shared core for the CLI, an MSBuild step, and the editor/file-watcher.
/// </summary>
public sealed class ContentBuilder
{
    private readonly Dictionary<string, IContentImporter> importersByName = new(StringComparer.OrdinalIgnoreCase);
    private readonly Dictionary<string, IContentImporter> importersByExtension = new(StringComparer.OrdinalIgnoreCase);
    private readonly Dictionary<string, IContentWriter> writersByImporter = new(StringComparer.OrdinalIgnoreCase);

    /// <summary>Builds a content builder with the built-in importers/writers registered.</summary>
    public ContentBuilder()
    {
        Register(new ModelImporter(), new SceneDataWriter());
    }

    /// <summary>
    /// Registers an importer (its <see cref="ContentImporterAttribute"/> supplies the name + extensions) and
    /// the writer that serializes its output.
    /// </summary>
    public void Register(IContentImporter importer, IContentWriter writer)
    {
        var attribute = importer.GetType().GetCustomAttribute<ContentImporterAttribute>()
                        ?? throw new InvalidOperationException(
                            $"Importer {importer.GetType().Name} is missing [ContentImporter].");

        importersByName[attribute.Name] = importer;
        writersByImporter[attribute.Name] = writer;
        foreach (var extension in attribute.Extensions)
        {
            importersByExtension[extension] = importer;
        }
    }

    /// <summary>True if the builder can handle the given source extension.</summary>
    public bool CanImport(string extension) => importersByExtension.ContainsKey(extension);

    /// <summary>
    /// Adds entries for every supported source file found under <paramref name="projectDirectory"/> that is
    /// not already present in the manifest. Bootstraps/refreshes a manifest from convention.
    /// </summary>
    public void ScanInto(ContentManifest manifest, string projectDirectory)
    {
        var existing = new HashSet<string>(
            manifest.Assets.Select(a => Normalize(a.Source)), StringComparer.OrdinalIgnoreCase);

        foreach (var file in Directory.EnumerateFiles(projectDirectory, "*.*", SearchOption.AllDirectories))
        {
            var extension = Path.GetExtension(file);
            if (!importersByExtension.TryGetValue(extension, out var importer))
            {
                continue;
            }

            var relative = Normalize(Path.GetRelativePath(projectDirectory, file));
            if (relative.StartsWith("obj/", StringComparison.OrdinalIgnoreCase) ||
                relative.StartsWith("bin/", StringComparison.OrdinalIgnoreCase) ||
                !existing.Add(relative))
            {
                continue;
            }

            manifest.Assets.Add(new ContentAsset
            {
                Source = relative,
                Importer = importer.GetType().GetCustomAttribute<ContentImporterAttribute>().Name
            });
        }
    }

    /// <summary>Cooks every asset in the manifest that is stale, writing artifacts under the output directory. An asset
    /// no importer takes is copied there as it is. What was built from an asset no longer listed is deleted.</summary>
    public BuildResult Build(ContentManifest manifest, string projectDirectory, Action<string> log = null)
    {
        log ??= _ => { };

        var outputDirectory = Path.GetFullPath(Path.Combine(projectDirectory, manifest.OutputDirectory));
        var intermediateDirectory = Path.GetFullPath(Path.Combine(projectDirectory, manifest.IntermediateDirectory));
        Directory.CreateDirectory(outputDirectory);
        Directory.CreateDirectory(intermediateDirectory);

        var cachePath = Path.Combine(intermediateDirectory, ".acache");
        var cache = BuildCache.LoadFor(cachePath, outputDirectory);
        var result = new BuildResult();

        foreach (var asset in manifest.Assets)
        {
            var sourceFull = Path.GetFullPath(Path.Combine(projectDirectory, asset.Source));
            if (!File.Exists(sourceFull))
            {
                log($"{sourceFull} : error ACB001: the source file is missing");
                result.Failed++;
                continue;
            }

            // The source extension is kept in the logical name so two models that share a base name in
            // different formats (e.g. foo.dae and foo.3ds — which may be entirely different models) don't
            // collide. Cooked artifact is "<source>.aemf", load name is the source-relative path.
            var logicalName = string.IsNullOrEmpty(asset.LogicalName)
                ? Normalize(asset.Source)
                : Normalize(asset.LogicalName);

            if (!BuildCache.IsInside(outputDirectory, Path.Combine(outputDirectory, logicalName)))
            {
                log($"{sourceFull} : error ACB008: the file lies outside the project; link it under a path inside");
                result.Failed++;
                continue;
            }

            var importer = ResolveImporter(asset, sourceFull);
            if (importer == null && !string.IsNullOrEmpty(asset.Importer))
            {
                log($"{sourceFull} : error ACB002: no importer is named '{asset.Importer}'");
                result.Failed++;
                continue;
            }

            if (importer == null)
            {
                Copy(asset, sourceFull, logicalName, outputDirectory, cache, result, log);
                continue;
            }

            var importerName = importer.GetType().GetCustomAttribute<ContentImporterAttribute>().Name;
            var writer = writersByImporter[importerName];
            var cookedRelative = logicalName + writer.OutputExtension;
            var cookedFull = Path.Combine(outputDirectory, cookedRelative);

            if (!cache.IsStale(asset.Source, sourceFull, asset.Parameters, cookedFull))
            {
                log($"  [up-to-date] {asset.Source}");
                result.Skipped++;
                continue;
            }

            log($"  [cook] {asset.Source} -> {cookedRelative}");
            var companions = new List<(string From, string To)>();
            var context = new ContentBuildContext
            {
                ProjectDirectory = projectDirectory,
                OutputDirectory = outputDirectory,
                IntermediateDirectory = intermediateDirectory,
                Parameters = asset.Parameters,
                Log = log,
                CopyAlongside = (from, to) => companions.Add((from, to))
            };

            try
            {
                var content = importer.Import(sourceFull, context);

                var cookedDirectory = Path.GetDirectoryName(cookedFull);
                Directory.CreateDirectory(cookedDirectory);
                WriteAssetFile(cookedFull, writer, content, new AssetSource
                {
                    Path = Normalize(Path.GetRelativePath(
                        Path.GetDirectoryName(Path.Combine(projectDirectory, logicalName)), sourceFull)),
                    Importer = importerName,
                    Parameters = asset.Parameters == null ? new() : new(asset.Parameters)
                });

                List<string> written = [];
                foreach (var (from, to) in companions)
                {
                    var destination = Path.GetFullPath(Path.Combine(cookedDirectory, to));
                    if (!BuildCache.IsInside(outputDirectory, destination))
                    {
                        log($"{sourceFull} : warning ACB003: the texture {to} lies outside the model's folder and is not carried with it");
                        continue;
                    }

                    Directory.CreateDirectory(Path.GetDirectoryName(destination));
                    File.Copy(from, destination, true);
                    written.Add(Normalize(Path.GetRelativePath(outputDirectory, destination)));
                }

                cache.Update(asset.Source, sourceFull, asset.Parameters, cookedRelative, written);
                result.Cooked++;
            }
            catch (Exception ex)
            {
                if (File.Exists(cookedFull))
                {
                    File.Delete(cookedFull);
                }

                log($"{sourceFull} : error ACB004: {ex.Message}");
                result.Failed++;
            }
        }

        cache.Prune(manifest.Assets.Select(asset => asset.Source), outputDirectory);
        cache.Save(cachePath);
        return result;
    }

    private static void WriteAssetFile(string path, IContentWriter writer, object content, AssetSource source)
    {
        using var body = new MemoryStream();
        writer.Write(body, content);

        using var stream = File.Create(path);
        new AssetFile { Source = source, Body = body.ToArray() }.WriteTo(stream);
    }

    private static void Copy(ContentAsset asset, string sourceFull, string logicalName, string outputDirectory,
        BuildCache cache, BuildResult result, Action<string> log)
    {
        var copyFull = Path.Combine(outputDirectory, logicalName);
        if (!cache.IsStale(asset.Source, sourceFull, asset.Parameters, copyFull))
        {
            log($"  [up-to-date] {asset.Source}");
            result.Skipped++;
            return;
        }

        log($"  [copy] {asset.Source}");
        Directory.CreateDirectory(Path.GetDirectoryName(copyFull));
        File.Copy(sourceFull, copyFull, true);
        cache.Update(asset.Source, sourceFull, asset.Parameters, logicalName);
        result.Copied++;
    }

    private IContentImporter ResolveImporter(ContentAsset asset, string sourceFull)
    {
        if (!string.IsNullOrEmpty(asset.Importer) && importersByName.TryGetValue(asset.Importer, out var byName))
        {
            return byName;
        }

        return importersByExtension.GetValueOrDefault(Path.GetExtension(sourceFull));
    }

    private static string Normalize(string path) => path.Replace('\\', '/');
}
