using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Reflection;
using System.Security.Cryptography;
using System.Text;
using System.Threading;
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
    private readonly Dictionary<string, IReadOnlyDictionary<string, string>> defaultsByImporter =
        new(StringComparer.OrdinalIgnoreCase);

    private static readonly string EngineVersion =
        typeof(AssetFile).Assembly.GetCustomAttribute<AssemblyInformationalVersionAttribute>()?.InformationalVersion;

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

    /// <summary>The names of the importers registered.</summary>
    public IReadOnlyCollection<string> ImporterNames => importersByName.Keys;

    /// <summary>The full path of the source of <paramref name="manifest"/> imported into the asset file at
    /// <paramref name="assetFile"/>; null when none is.</summary>
    public string FindSource(ContentManifest manifest, string projectDirectory, string assetFile)
    {
        ArgumentNullException.ThrowIfNull(manifest);
        ArgumentException.ThrowIfNullOrEmpty(projectDirectory);
        if (string.IsNullOrEmpty(assetFile))
        {
            return null;
        }

        var assetFull = Path.GetFullPath(assetFile);
        var comparison = OperatingSystem.IsLinux() ? StringComparison.Ordinal : StringComparison.OrdinalIgnoreCase;
        foreach (var asset in manifest.Assets)
        {
            var sourceFull = Path.GetFullPath(Path.Combine(projectDirectory, asset.Source));
            if (ResolveImporter(asset, sourceFull) is not { } importer)
            {
                continue;
            }

            var logicalName = string.IsNullOrEmpty(asset.LogicalName) ? Normalize(asset.Source) : Normalize(asset.LogicalName);
            var writer = writersByImporter[importer.GetType().GetCustomAttribute<ContentImporterAttribute>().Name];
            if (string.Equals(Path.GetFullPath(GetAssetFilePath(projectDirectory, sourceFull, logicalName, writer)),
                    assetFull, comparison))
            {
                return sourceFull;
            }
        }

        return null;
    }

    /// <summary>The name of the importer that takes the file at <paramref name="sourcePath"/> by its extension; null when
    /// none does.</summary>
    public string GetImporterName(string sourcePath) =>
        importersByExtension.GetValueOrDefault(Path.GetExtension(sourcePath ?? string.Empty)) is { } importer
            ? importer.GetType().GetCustomAttribute<ContentImporterAttribute>().Name
            : null;

    /// <summary>The settings the importer named <paramref name="importerName"/> reads; empty for an importer not
    /// registered.</summary>
    public IReadOnlyList<ImportSetting> GetSettings(string importerName) =>
        importerName != null && importersByName.TryGetValue(importerName, out var importer) ? importer.Settings : [];

    /// <summary>The parameters an asset of the importer named <paramref name="importerName"/> is imported with when the
    /// manifest gives it none and its asset file records none - it has no asset file yet, or one that cannot be read.
    /// Null or empty: the importer's own defaults.</summary>
    public void SetDefaultParameters(string importerName, IReadOnlyDictionary<string, string> parameters)
    {
        ArgumentException.ThrowIfNullOrEmpty(importerName);
        if (parameters == null || parameters.Count == 0)
        {
            defaultsByImporter.Remove(importerName);
            return;
        }

        defaultsByImporter[importerName] = new Dictionary<string, string>(parameters);
    }

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

    /// <summary>Imports each asset an importer takes into its asset file beside it (<c>Ship.dae.aemf</c> beside
    /// <c>Ship.dae</c>; for a source outside the project, under <c>obj/assets</c>) when there is none yet, it cannot be
    /// read, or the source changed, or the manifest gives it settings other than those it was imported with - not when
    /// only the format or the importer did - and copies the asset file to the output directory. Imported again, it keeps
    /// the settings its asset file records unless the manifest gives some; a new one takes the defaults set by
    /// <see cref="SetDefaultParameters"/>. An asset no importer takes is copied there as it is. What was built from an
    /// asset no longer listed is deleted from the output. One build or <see cref="Import"/> of a project runs at a time,
    /// in all processes of the user: another waits for it.</summary>
    public BuildResult Build(ContentManifest manifest, string projectDirectory, Action<string> log = null)
    {
        log ??= _ => { };
        using var projectLock = ProjectLock.Take(projectDirectory);

        var outputDirectory = Path.GetFullPath(Path.Combine(projectDirectory, manifest.OutputDirectory));
        var intermediateDirectory = Path.GetFullPath(Path.Combine(projectDirectory, manifest.IntermediateDirectory));
        Directory.CreateDirectory(outputDirectory);
        Directory.CreateDirectory(intermediateDirectory);

        var cachePath = Path.Combine(intermediateDirectory, ".acache");
        var cache = BuildCache.LoadFor(cachePath, outputDirectory);
        var result = new BuildResult();
        var importTargets = GetImportTargets(manifest, projectDirectory);

        foreach (var asset in manifest.Assets)
        {
            var sourceFull = Path.GetFullPath(Path.Combine(projectDirectory, asset.Source));
            if (importTargets.Contains(sourceFull))
            {
                continue;
            }

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

            var writer = writersByImporter[importer.GetType().GetCustomAttribute<ContentImporterAttribute>().Name];
            var assetFull = GetAssetFilePath(projectDirectory, sourceFull, logicalName, writer);
            var cookedRelative = logicalName + writer.OutputExtension;
            var cookedFull = Path.Combine(outputDirectory, cookedRelative);
            var cookedDirectory = Path.GetDirectoryName(cookedFull);

            try
            {
                var imported = ImportIfNeeded(asset, importer, sourceFull, logicalName, assetFull, projectDirectory,
                    outputDirectory, intermediateDirectory, log);
                if (imported == null && !cache.IsStale(asset.Source, assetFull, asset.Parameters, cookedFull))
                {
                    log($"  [up-to-date] {asset.Source}");
                    result.Skipped++;
                    continue;
                }

                Directory.CreateDirectory(cookedDirectory);
                File.Copy(assetFull, cookedFull, true);
                var written = CopyCompanions(assetFull, cookedDirectory, outputDirectory, sourceFull, log);
                cache.Update(asset.Source, assetFull, asset.Parameters, cookedRelative, written);
                if (imported is { } done)
                {
                    result.AddImported(done);
                }
                else
                {
                    result.Copied++;
                }
            }
            catch (Exception ex)
            {
                log($"{sourceFull} : error ACB004: {ex.Message}");
                result.Failed++;
            }
        }

        cache.Prune(manifest.Assets.Select(asset => asset.Source), outputDirectory);
        cache.Save(cachePath);
        return result;
    }

    /// <summary>Imports each asset an importer takes into its asset file, as <see cref="Build"/> does, and nothing
    /// more: no output is written and no other asset is copied. In the result, <see cref="BuildResult.Cooked"/> counts
    /// the assets imported and <see cref="BuildResult.Skipped"/> those whose asset file is up to date. Stopped through
    /// <paramref name="cancellation"/>, it ends between two assets, the asset files written so far kept.</summary>
    public BuildResult Import(ContentManifest manifest, string projectDirectory, Action<string> log = null,
        CancellationToken cancellation = default)
    {
        log ??= _ => { };
        var result = new BuildResult();
        using var projectLock = ProjectLock.Take(projectDirectory, cancellation);
        if (projectLock == null)
        {
            return result;
        }

        var outputDirectory = Path.GetFullPath(Path.Combine(projectDirectory, manifest.OutputDirectory));
        var intermediateDirectory = Path.GetFullPath(Path.Combine(projectDirectory, manifest.IntermediateDirectory));
        var importTargets = GetImportTargets(manifest, projectDirectory);

        foreach (var asset in manifest.Assets)
        {
            if (cancellation.IsCancellationRequested)
            {
                break;
            }

            var sourceFull = Path.GetFullPath(Path.Combine(projectDirectory, asset.Source));
            if (importTargets.Contains(sourceFull))
            {
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
                continue;
            }

            if (!File.Exists(sourceFull))
            {
                log($"{sourceFull} : error ACB001: the source file is missing");
                result.Failed++;
                continue;
            }

            var logicalName = string.IsNullOrEmpty(asset.LogicalName) ? Normalize(asset.Source) : Normalize(asset.LogicalName);
            if (!BuildCache.IsInside(outputDirectory, Path.Combine(outputDirectory, logicalName)))
            {
                log($"{sourceFull} : error ACB008: the file lies outside the project; link it under a path inside");
                result.Failed++;
                continue;
            }

            var writer = writersByImporter[importer.GetType().GetCustomAttribute<ContentImporterAttribute>().Name];
            var assetFull = GetAssetFilePath(projectDirectory, sourceFull, logicalName, writer);
            try
            {
                if (ImportIfNeeded(asset, importer, sourceFull, logicalName, assetFull, projectDirectory, outputDirectory,
                        intermediateDirectory, log) is { } imported)
                {
                    result.AddImported(imported);
                }
                else
                {
                    result.Skipped++;
                }
            }
            catch (Exception ex)
            {
                log($"{sourceFull} : error ACB004: {ex.Message}");
                result.Failed++;
            }
        }

        return result;
    }

    /// <summary>Imports the asset of <paramref name="manifest"/> whose source is the file at <paramref name="source"/>
    /// into its asset file, up to date or not, with <paramref name="parameters"/> - with the manifest's when it gives the
    /// asset some, which a build would go back to. Waits for the project's lock as <see cref="Import"/> does. A source
    /// the manifest does not list, or no importer takes, is an error in the result.</summary>
    public BuildResult ImportAsset(ContentManifest manifest, string projectDirectory, string source,
        IReadOnlyDictionary<string, string> parameters, Action<string> log = null)
    {
        ArgumentNullException.ThrowIfNull(manifest);
        ArgumentException.ThrowIfNullOrEmpty(projectDirectory);
        ArgumentException.ThrowIfNullOrEmpty(source);
        log ??= _ => { };
        var result = new BuildResult();
        using var projectLock = ProjectLock.Take(projectDirectory);

        var sourceFull = Path.GetFullPath(source);
        var comparison = OperatingSystem.IsLinux() ? StringComparison.Ordinal : StringComparison.OrdinalIgnoreCase;
        var asset = manifest.Assets.FirstOrDefault(listed =>
            string.Equals(Path.GetFullPath(Path.Combine(projectDirectory, listed.Source)), sourceFull, comparison));
        var importer = asset == null ? null : ResolveImporter(asset, sourceFull);
        if (importer == null)
        {
            log(!string.IsNullOrEmpty(asset?.Importer)
                ? $"{sourceFull} : error ACB002: no importer is named '{asset.Importer}'"
                : $"{sourceFull} : error ACB012: the project imports no asset from this file");
            result.Failed++;
            return result;
        }

        if (!File.Exists(sourceFull))
        {
            log($"{sourceFull} : error ACB001: the source file is missing");
            result.Failed++;
            return result;
        }

        var outputDirectory = Path.GetFullPath(Path.Combine(projectDirectory, manifest.OutputDirectory));
        var intermediateDirectory = Path.GetFullPath(Path.Combine(projectDirectory, manifest.IntermediateDirectory));
        var logicalName = string.IsNullOrEmpty(asset.LogicalName) ? Normalize(asset.Source) : Normalize(asset.LogicalName);
        if (!BuildCache.IsInside(outputDirectory, Path.Combine(outputDirectory, logicalName)))
        {
            log($"{sourceFull} : error ACB008: the file lies outside the project; link it under a path inside");
            result.Failed++;
            return result;
        }

        var writer = writersByImporter[importer.GetType().GetCustomAttribute<ContentImporterAttribute>().Name];
        var assetFull = GetAssetFilePath(projectDirectory, sourceFull, logicalName, writer);
        try
        {
            if (ImportIfNeeded(asset, importer, sourceFull, logicalName, assetFull, projectDirectory, outputDirectory,
                    intermediateDirectory, log, parameters ?? new Dictionary<string, string>()) is { } imported)
            {
                result.AddImported(imported);
            }
        }
        catch (Exception ex)
        {
            log($"{sourceFull} : error ACB004: {ex.Message}");
            result.Failed++;
        }

        return result;
    }

    // Beside the source; for a source outside the project, in one place under obj whatever the build's cache is, so the
    // editor's imports and every configuration's build share it.
    private static string GetAssetFilePath(string projectDirectory, string sourceFull, string logicalName,
        IContentWriter writer) =>
        BuildCache.IsInside(projectDirectory, sourceFull)
            ? sourceFull + writer.OutputExtension
            : Path.Combine(projectDirectory, "obj", "assets", logicalName + writer.OutputExtension);

    // Imports the source into its asset file when it needs it and says why; null when the asset file is up to date.
    private ImportedAsset? ImportIfNeeded(ContentAsset asset, IContentImporter importer, string sourceFull,
        string logicalName, string assetFull, string projectDirectory, string outputDirectory,
        string intermediateDirectory, Action<string> log, IReadOnlyDictionary<string, string> asked = null)
    {
        var importerName = importer.GetType().GetCustomAttribute<ContentImporterAttribute>().Name;
        var file = new FileInfo(sourceFull);
        var written = file.LastWriteTimeUtc;
        var length = file.Length;
        string hash = null;
        string Hash() => hash ??= AssetSource.ComputeHash(sourceFull);
        var reason = GetImportReason(assetFull, sourceFull, written, length, Hash, importerName, asset.Parameters,
            defaultsByImporter.GetValueOrDefault(importerName), asked == null ? log : _ => { }, out var hadThumbnail,
            out var parameters, out var keptAsItIs);
        if (asked != null)
        {
            if (keptAsItIs != null)
            {
                throw new InvalidDataException(keptAsItIs.Replace("; import its source again", string.Empty) +
                                               "; a newer engine wrote it, and this one does not write over it");
            }

            reason = ImportReason.Asked;
            parameters = asset.Parameters is { Count: > 0 } ? asset.Parameters : asked;
        }

        if (reason is not { } why)
        {
            return null;
        }

        log($"  [import] {asset.Source} -> {Path.GetFileName(assetFull)} ({Describe(why)})");
        List<(string From, string To)> companions = [];
        var context = new ContentBuildContext
        {
            ProjectDirectory = projectDirectory,
            OutputDirectory = outputDirectory,
            IntermediateDirectory = intermediateDirectory,
            Parameters = parameters,
            Log = log,
            CopyAlongside = (from, to) => companions.Add((from, to))
        };

        var content = importer.Import(sourceFull, context);
        WriteAssetFile(assetFull, writersByImporter[importerName], content, new AssetSource
        {
            Path = Normalize(Path.GetRelativePath(
                Path.GetDirectoryName(Path.Combine(projectDirectory, logicalName)), sourceFull)),
            Hash = Hash(),
            Written = written,
            Length = length,
            Importer = importerName,
            Parameters = new(parameters),
            EngineVersion = EngineVersion,
            Companions = companions.Select(companion => Normalize(companion.To)).Distinct().ToList()
        });
        return new ImportedAsset(assetFull, why, hadThumbnail);
    }

    private static string Describe(ImportReason reason) => reason switch
    {
        ImportReason.New => "new",
        ImportReason.SourceChanged => "source changed",
        ImportReason.SettingsChanged => "settings changed",
        ImportReason.Unreadable => "unreadable",
        _ => "asked"
    };

    private HashSet<string> GetImportTargets(ContentManifest manifest, string projectDirectory)
    {
        HashSet<string> targets = new(OperatingSystem.IsLinux() ? StringComparer.Ordinal : StringComparer.OrdinalIgnoreCase);
        foreach (var asset in manifest.Assets)
        {
            var sourceFull = Path.GetFullPath(Path.Combine(projectDirectory, asset.Source));
            if (BuildCache.IsInside(projectDirectory, sourceFull) && ResolveImporter(asset, sourceFull) is { } importer)
            {
                var name = importer.GetType().GetCustomAttribute<ContentImporterAttribute>().Name;
                targets.Add(sourceFull + writersByImporter[name].OutputExtension);
            }
        }

        return targets;
    }

    private static ImportReason? GetImportReason(string assetFull, string sourceFull, DateTime sourceWritten,
        long sourceLength, Func<string> sourceHash, string importerName,
        IReadOnlyDictionary<string, string> manifestParameters, IReadOnlyDictionary<string, string> defaults,
        Action<string> log, out bool hadThumbnail, out IReadOnlyDictionary<string, string> parameters,
        out string keptAsItIs)
    {
        hadThumbnail = false;
        keptAsItIs = null;
        var givenByManifest = manifestParameters is { Count: > 0 };
        parameters = givenByManifest ? manifestParameters : defaults ?? new Dictionary<string, string>();
        if (!File.Exists(assetFull))
        {
            return ImportReason.New;
        }

        AssetFile file;
        try
        {
            file = AssetFile.Read(assetFull, AssetFileSection.Source);
        }
        catch (AssetFormatException exception) when (exception.WrittenVersion != null)
        {
            log($"{sourceFull} : warning ACB009: {exception.Message}");
            keptAsItIs = exception.WrittenVersion > AssetFile.FormatVersion ? exception.Message : null;
            return null;
        }
        catch (AssetFormatException)
        {
            return ImportReason.Unreadable;
        }

        hadThumbnail = (file.Sections & AssetFileSection.Thumbnail) != 0;

        if (file.Version != AssetFile.FormatVersion)
        {
            log($"{sourceFull} : warning ACB010: {Path.GetFileName(assetFull)} is in format version {file.Version}, " +
                $"this engine writes {AssetFile.FormatVersion}; import its source again to bring it up to date");
        }

        var source = file.Source;
        var sameImporter = source != null && string.Equals(source.Importer, importerName, StringComparison.OrdinalIgnoreCase);
        if (sameImporter && !givenByManifest)
        {
            parameters = source.Parameters ?? new Dictionary<string, string>();
        }

        if (source == null || (source.Written != sourceWritten || source.Length != sourceLength) && source.Hash != sourceHash())
        {
            return ImportReason.SourceChanged;
        }

        return sameImporter && (!givenByManifest || HaveSameParameters(source.Parameters, manifestParameters))
            ? null
            : ImportReason.SettingsChanged;
    }

    private static bool HaveSameParameters(IReadOnlyDictionary<string, string> recorded,
        IReadOnlyDictionary<string, string> wanted)
    {
        var count = wanted?.Count ?? 0;
        return recorded.Count == count
               && (count == 0 || wanted.All(pair => recorded.TryGetValue(pair.Key, out var value) && value == pair.Value));
    }

    private static List<string> CopyCompanions(string assetFull, string cookedDirectory, string outputDirectory,
        string sourceFull, Action<string> log)
    {
        List<string> written = [];
        List<string> companions;
        try
        {
            companions = AssetFile.Read(assetFull, AssetFileSection.Source).Source?.Companions ?? [];
        }
        catch (AssetFormatException)
        {
            return written;
        }

        var sourceDirectory = Path.GetDirectoryName(sourceFull);
        foreach (var to in companions)
        {
            var destination = Path.GetFullPath(Path.Combine(cookedDirectory, to));
            if (!BuildCache.IsInside(outputDirectory, destination))
            {
                log($"{sourceFull} : warning ACB003: the texture {to} lies outside the model's folder and is not carried with it");
                continue;
            }

            var from = Path.GetFullPath(Path.Combine(sourceDirectory, to));
            if (!File.Exists(from))
            {
                log($"{sourceFull} : warning ACB006: the texture {to} is not found");
                continue;
            }

            Directory.CreateDirectory(Path.GetDirectoryName(destination));
            File.Copy(from, destination, true);
            written.Add(Normalize(Path.GetRelativePath(outputDirectory, destination)));
        }

        return written;
    }

    /// <summary>Puts <paramref name="png"/> into the asset file at <paramref name="assetFile"/> as its thumbnail, the
    /// rest of the file as it was, under the lock of the content project in <paramref name="projectDirectory"/> - null
    /// for a file in no project - so no import writes the file meanwhile. Nothing is written, and false given, when the
    /// file is gone, was written again since <paramref name="written"/> (its last write time, in UTC, when the picture was
    /// taken of it), or is of an older format version, whose body this engine does not write.</summary>
    public static bool WriteThumbnail(string projectDirectory, string assetFile, byte[] png, DateTime written)
    {
        ArgumentException.ThrowIfNullOrEmpty(assetFile);
        ArgumentNullException.ThrowIfNull(png);

        using var projectLock = projectDirectory == null ? null : ProjectLock.Take(projectDirectory);
        if (!File.Exists(assetFile) || File.GetLastWriteTimeUtc(assetFile) != written)
        {
            return false;
        }

        var file = AssetFile.Read(assetFile, AssetFileSection.All);
        if (file.Version != AssetFile.FormatVersion)
        {
            return false;
        }

        file.Thumbnail = png;
        Replace(assetFile, file);
        return true;
    }

    private static void WriteAssetFile(string path, IContentWriter writer, object content, AssetSource source)
    {
        using var body = new MemoryStream();
        writer.Write(body, content);
        Replace(path, new AssetFile { Source = source, Body = body.ToArray() });
    }

    private static void Replace(string path, AssetFile file)
    {
        // Hidden and named afresh, so no content glob lists it and no other writer meets it.
        var writing = Path.Combine(Path.GetDirectoryName(path),
            $".{Path.GetFileName(path)}.{Guid.NewGuid():N}.writing");
        Directory.CreateDirectory(Path.GetDirectoryName(path));
        try
        {
            using (var stream = File.Create(writing))
            {
                file.WriteTo(stream);
            }

            File.Move(writing, path, true);
        }
        finally
        {
            File.Delete(writing);
        }
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

    // The user's one lock on a project's content, across processes: an editor's import and a build wait for each other
    // instead of writing the same asset files at once.
    private sealed class ProjectLock : IDisposable
    {
        private static readonly TimeSpan WaitStep = TimeSpan.FromMilliseconds(100);

        private readonly Mutex _mutex;

        private ProjectLock(Mutex mutex) => _mutex = mutex;

        // Null when stopped through the token before it was free.
        public static ProjectLock Take(string projectDirectory, CancellationToken cancellation = default)
        {
            var key = Path.GetFullPath(projectDirectory).TrimEnd(Path.DirectorySeparatorChar, Path.AltDirectorySeparatorChar);
            if (!OperatingSystem.IsLinux())
            {
                key = key.ToUpperInvariant();
            }

            var name = "adamantium-content-" +
                       Convert.ToHexString(SHA256.HashData(Encoding.UTF8.GetBytes(key)), 0, 12).ToLowerInvariant();
            var mutex = new Mutex(false, name, new NamedWaitHandleOptions { CurrentUserOnly = true, CurrentSessionOnly = false },
                out _);
            // A named mutex cannot be waited on together with another handle on every platform: wait on it alone, a
            // little at a time.
            try
            {
                while (!mutex.WaitOne(WaitStep))
                {
                    if (cancellation.IsCancellationRequested)
                    {
                        mutex.Dispose();
                        return null;
                    }
                }
            }
            catch (AbandonedMutexException)
            {
            }

            return new ProjectLock(mutex);
        }

        public void Dispose()
        {
            _mutex.ReleaseMutex();
            _mutex.Dispose();
        }
    }
}
