using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using Adamantium.Content.Pipeline;
using Adamantium.Graphics.Core.Assets;
using Adamantium.Graphics.Core.Models;

namespace Adamantium.Engine.ContentCompiler.CLI
{
    /// <summary>
    /// Command-line content compiler (the MonoGame MGCB analog) built on <see cref="ContentBuilder"/>.
    /// The same pipeline core is reused by the editor and the file-watcher.
    /// </summary>
    internal static class Program
    {
        private const string DefaultManifestName = "Content.acontent";

        private static int Main(string[] args)
        {
            if (args.Length < 2)
            {
                PrintUsage();
                return 1;
            }

            var command = args[0].ToLowerInvariant();

            if (command == "verify")
            {
                return Verify(Path.GetFullPath(args[1]));
            }

            var projectDirectory = Path.GetFullPath(args[1]);
            var manifestPath = GetOption(args, "--manifest") ?? Path.Combine(projectDirectory, DefaultManifestName);
            var builder = new ContentBuilder();

            switch (command)
            {
                case "scan":
                {
                    var manifest = File.Exists(manifestPath) ? ContentManifest.Load(manifestPath) : new ContentManifest();
                    var before = manifest.Assets.Count;
                    builder.ScanInto(manifest, projectDirectory);
                    manifest.Save(manifestPath);
                    Console.WriteLine($"Scanned {projectDirectory}: +{manifest.Assets.Count - before} new, {manifest.Assets.Count} total -> {manifestPath}");
                    return 0;
                }
                case "build":
                {
                    var files = GetOption(args, "--files");
                    ContentManifest manifest;
                    if (File.Exists(manifestPath))
                    {
                        manifest = ContentManifest.Load(manifestPath);
                    }
                    else
                    {
                        manifest = new ContentManifest();
                        if (files == null)
                        {
                            builder.ScanInto(manifest, projectDirectory);
                            Console.WriteLine($"No manifest at {manifestPath}; scanned {manifest.Assets.Count} asset(s) by convention.");
                        }
                    }

                    if (files != null)
                    {
                        var listed = File.ReadAllLines(files)
                            .Where(line => line.Length > 0)
                            .Select(line => line.Split('\t'))
                            .Select(parts => (Path: parts[0], Link: parts.Length > 1 ? parts[1] : string.Empty));
                        manifest.SetListedFiles(projectDirectory, listed, manifestPath, Console.WriteLine);
                    }

                    manifest.OutputDirectory = GetOption(args, "--out") ?? manifest.OutputDirectory;
                    manifest.IntermediateDirectory = GetOption(args, "--cache") ?? manifest.IntermediateDirectory;

                    Console.WriteLine($"Building content for {projectDirectory}");
                    var result = builder.Build(manifest, projectDirectory, Console.WriteLine);
                    Console.WriteLine(result.ToString());
                    return result.Failed > 0 ? 2 : 0;
                }
                default:
                    PrintUsage();
                    return 1;
            }
        }

        private static int Verify(string aemfPath)
        {
            if (!File.Exists(aemfPath))
            {
                Console.WriteLine($"Not found: {aemfPath}");
                return 1;
            }

            AssetFile file;
            SceneData scene;
            try
            {
                file = AssetFile.Read(aemfPath, AssetFileSection.Thumbnail | AssetFileSection.Source);
                scene = SceneDataSerializer.ReadAssetFile(aemfPath);
            }
            catch (AssetFormatException exception)
            {
                Console.WriteLine(exception.Message);
                return 2;
            }

            if (scene?.Models == null)
            {
                Console.WriteLine("Deserialized to null scene.");
                return 2;
            }

            int models = 0, meshes = 0, brokenParents = 0;
            long vertices = 0, indices = 0;
            var stack = new Stack<SceneData.Model>();
            stack.Push(scene.Models);
            while (stack.Count > 0)
            {
                var node = stack.Pop();
                models++;
                foreach (var mesh in node.Meshes)
                {
                    meshes++;
                    vertices += mesh.Points?.Length ?? 0;
                    indices += mesh.Indices?.Length ?? 0;
                }

                foreach (var child in node.Dependencies)
                {
                    if (!ReferenceEquals(child.Parent, node))
                    {
                        brokenParents++;
                    }

                    stack.Push(child);
                }
            }

            Console.WriteLine($"OK: '{scene.Name}'  models={models}  meshes={meshes}  vertices={vertices}  indices={indices}  " +
                              $"materials={scene.Materials.Count}  skins={scene.Controllers.Count}  parentLinks={(brokenParents == 0 ? "ok" : $"BROKEN x{brokenParents}")}  " +
                              $"source={file.Source?.Path ?? "-"}  thumbnail={(file.Thumbnail == null ? "none" : $"{file.Thumbnail.Length} bytes")}");
            return brokenParents == 0 ? 0 : 3;
        }

        private static string GetOption(string[] args, string name)
        {
            for (var i = 0; i < args.Length - 1; i++)
            {
                if (string.Equals(args[i], name, StringComparison.OrdinalIgnoreCase))
                {
                    return args[i + 1];
                }
            }

            return null;
        }

        private static void PrintUsage()
        {
            Console.WriteLine("Adamantium content compiler");
            Console.WriteLine("  scan  <projectDir> [--manifest <path>]   add new assets to the manifest by convention");
            Console.WriteLine("  build <projectDir> [--manifest <path>]   cook all stale assets into the output directory");
            Console.WriteLine("        [--files <list>]                   the assets are the files listed, one a line; the");
            Console.WriteLine("                                           manifest, if any, only gives their settings");
            Console.WriteLine("        [--out <dir>] [--cache <dir>]      where the output and the build cache go");
            Console.WriteLine("  verify <aemfPath>                        deserialize a cooked .aemf and print its stats");
        }
    }
}
