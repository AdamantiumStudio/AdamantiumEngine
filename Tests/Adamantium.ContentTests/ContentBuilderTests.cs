using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;
using Adamantium.Content.Pipeline;
using Adamantium.Graphics.Core.Assets;
using Adamantium.Graphics.Core.Models;
using NUnit.Framework;

namespace Adamantium.ContentTests;

[TestFixture]
public class ContentBuilderTests
{
    private const string Triangle = "v 0 0 0\nv 1 0 0\nv 0 1 0\nvn 0 0 1\nf 1//1 2//1 3//1\n";

    private string _project;
    private string _output;

    [SetUp]
    public void SetUp()
    {
        _project = Path.Combine(Path.GetTempPath(), "adamantium-content-" + Guid.NewGuid().ToString("N"));
        _output = Path.Combine(_project, "out");
        Directory.CreateDirectory(_project);
    }

    [TearDown]
    public void TearDown()
    {
        Directory.Delete(_project, true);
    }

    [Test]
    public void AFileNoImporterTakes_IsCopiedAsItIs()
    {
        Write("Notes/readme.txt", "hello");

        var result = Build("Notes/readme.txt");

        Assert.That(result.Copied, Is.EqualTo(1));
        Assert.That(result.Failed, Is.EqualTo(0));
        Assert.That(File.ReadAllText(Path.Combine(_output, "Notes", "readme.txt")), Is.EqualTo("hello"));
    }

    [Test]
    public void AModel_IsImportedBesideItsSource_AndOnlyTheAssetFileGoesToTheOutput()
    {
        Write("Models/triangle.obj", Triangle);

        var result = Build("Models/triangle.obj");

        Assert.Multiple(() =>
        {
            Assert.That(result.Cooked, Is.EqualTo(1), result.ToString());
            Assert.That(File.Exists(Path.Combine(_project, "Models", "triangle.obj.aemf")), Is.True);
            Assert.That(File.ReadAllBytes(Path.Combine(_output, "Models", "triangle.obj.aemf")),
                Is.EqualTo(File.ReadAllBytes(Path.Combine(_project, "Models", "triangle.obj.aemf"))));
            Assert.That(File.Exists(Path.Combine(_output, "Models", "triangle.obj")), Is.False);
        });
    }

    [Test]
    public void AnUnchangedModel_IsNotImportedAgain()
    {
        Write("Models/triangle.obj", Triangle);
        Build("Models/triangle.obj");
        var asset = Path.Combine(_project, "Models", "triangle.obj.aemf");
        var written = File.GetLastWriteTimeUtc(asset);

        var result = Build("Models/triangle.obj");

        Assert.Multiple(() =>
        {
            Assert.That(result.Cooked, Is.EqualTo(0), result.ToString());
            Assert.That(result.Skipped, Is.EqualTo(1));
            Assert.That(File.GetLastWriteTimeUtc(asset), Is.EqualTo(written));
        });
    }

    [Test]
    public void AChangedSource_IsImportedAgain()
    {
        Write("Models/triangle.obj", Triangle);
        Build("Models/triangle.obj");
        Write("Models/triangle.obj", Triangle + "v 1 1 0\n");

        var result = Build("Models/triangle.obj");

        Assert.That(result.Cooked, Is.EqualTo(1), result.ToString());
    }

    [Test]
    public void ChangedSettings_AreImportedAgain()
    {
        Write("Models/triangle.obj", Triangle);
        Build("Models/triangle.obj");
        var manifest = Manifest();
        manifest.SetSources(["Models/triangle.obj"]);
        manifest.Assets[0].Parameters["Scale"] = "2";

        var result = new ContentBuilder().Build(manifest, _project);

        Assert.That(result.Cooked, Is.EqualTo(1), result.ToString());
    }

    [Test]
    public void AnAssetFileOfAnotherFormatVersion_IsKeptAsItIs_WithAWarning()
    {
        Write("Models/triangle.obj", Triangle);
        Build("Models/triangle.obj");
        var asset = Path.Combine(_project, "Models", "triangle.obj.aemf");
        var bytes = File.ReadAllBytes(asset);
        bytes[4] = AssetFile.FormatVersion + 1;
        File.WriteAllBytes(asset, bytes);
        List<string> log = [];
        var manifest = Manifest();
        manifest.SetSources(["Models/triangle.obj"]);

        var result = new ContentBuilder().Build(manifest, _project, log.Add);

        Assert.Multiple(() =>
        {
            Assert.That(result.Cooked, Is.EqualTo(0), result.ToString());
            Assert.That(log, Has.Some.Contains("warning ACB009"));
            Assert.That(File.ReadAllBytes(asset), Is.EqualTo(bytes));
            Assert.That(File.ReadAllBytes(Path.Combine(_output, "Models", "triangle.obj.aemf")), Is.EqualTo(bytes));
        });
    }

    [TestCase(0)]
    [TestCase(100)]
    public void AnAssetFileCutShort_IsImportedAgain(int length)
    {
        Write("Models/triangle.obj", Triangle);
        Build("Models/triangle.obj");
        var asset = Path.Combine(_project, "Models", "triangle.obj.aemf");
        File.WriteAllBytes(asset, File.ReadAllBytes(asset)[..length]);

        var result = Build("Models/triangle.obj");

        Assert.Multiple(() =>
        {
            Assert.That(result.Cooked, Is.EqualTo(1), result.ToString());
            Assert.That(AssetFile.Read(asset, AssetFileSection.Body).Body, Is.Not.Empty);
        });
    }

    [Test]
    public void ATextureThatIsNotContent_GoesToTheOutputAgain_AfterTheOutputIsWiped()
    {
        var texture = Path.Combine(_project, "Models", "tex.png");
        Write("Models/tex.png", "png");
        Write("Models/tri.mtl", "newmtl m\nmap_Kd " + texture + "\n");
        Write("Models/tri.obj", "mtllib tri.mtl\nusemtl m\n" + Triangle);
        Build("Models/tri.obj");
        Directory.Delete(_output, true);

        var result = Build("Models/tri.obj");

        Assert.Multiple(() =>
        {
            Assert.That(result.Cooked, Is.EqualTo(0), result.ToString());
            Assert.That(File.Exists(Path.Combine(_output, "Models", "tri.obj.aemf")), Is.True);
            Assert.That(File.Exists(Path.Combine(_output, "Models", "tex.png")), Is.True);
        });
    }

    [Test]
    public void AnAssetFileListedBesideItsSource_GoesToTheOutputOnce()
    {
        Write("Models/triangle.obj", Triangle);
        Build("Models/triangle.obj");

        var result = Build("Models/triangle.obj", "Models/triangle.obj.aemf");

        Assert.Multiple(() =>
        {
            Assert.That(result.Failed, Is.EqualTo(0), result.ToString());
            Assert.That(result.Copied, Is.EqualTo(0));
            Assert.That(File.Exists(Path.Combine(_output, "Models", "triangle.obj.aemf.aemf")), Is.False);
        });
    }

    [Test]
    public void ACookedModel_RecordsTheSourceItWasImportedFrom()
    {
        Write("Models/triangle.obj", Triangle);
        var manifest = Manifest();
        manifest.SetSources(["Models/triangle.obj"]);
        manifest.Assets[0].Parameters["Scale"] = "2";

        new ContentBuilder().Build(manifest, _project);

        var source = AssetFile.Read(Path.Combine(_output, "Models", "triangle.obj.aemf"), AssetFileSection.Source).Source;
        Assert.Multiple(() =>
        {
            Assert.That(source.Path, Is.EqualTo("triangle.obj"));
            Assert.That(source.Hash, Is.EqualTo(AssetSource.ComputeHash(Path.Combine(_project, "Models", "triangle.obj"))));
            Assert.That(source.Importer, Is.EqualTo("Model"));
            Assert.That(source.Parameters, Is.EqualTo(new Dictionary<string, string> { ["Scale"] = "2" }));
            Assert.That(source.EngineVersion, Is.Not.Empty);
        });
    }

    [Test]
    public void ALinkedModel_RecordsItsSourceFromWhereTheLinkPutsIt()
    {
        var shared = Path.Combine(Path.GetDirectoryName(_project), Path.GetFileName(_project) + "-shared");
        Directory.CreateDirectory(shared);
        File.WriteAllText(Path.Combine(shared, "x.obj"), Triangle);
        try
        {
            var manifest = Manifest();
            manifest.Assets.Add(new ContentAsset
            {
                Source = Path.GetRelativePath(_project, Path.Combine(shared, "x.obj")),
                LogicalName = "Models/x.obj"
            });

            new ContentBuilder().Build(manifest, _project);

            var source = AssetFile.Read(Path.Combine(_output, "Models", "x.obj.aemf"), AssetFileSection.Source).Source;
            Assert.Multiple(() =>
            {
                Assert.That(source.Path, Is.EqualTo("../../" + Path.GetFileName(shared) + "/x.obj"));
                Assert.That(File.Exists(Path.Combine(shared, "x.obj.aemf")), Is.False, "nothing written outside the project");
                Assert.That(File.Exists(Path.Combine(_project, "obj", "assets", "Models", "x.obj.aemf")), Is.True);
            });
        }
        finally
        {
            Directory.Delete(shared, true);
        }
    }

    [Test]
    public void ASecondBuild_OfUnchangedFiles_DoesNothing()
    {
        Write("a.txt", "a");
        Build("a.txt");

        var result = Build("a.txt");

        Assert.That(result.Skipped, Is.EqualTo(1));
        Assert.That(result.Copied, Is.EqualTo(0));
    }

    [Test]
    public void AChangedFile_IsCopiedAgain()
    {
        Write("a.txt", "a");
        Build("a.txt");
        Write("a.txt", "changed");

        var result = Build("a.txt");

        Assert.That(result.Copied, Is.EqualTo(1));
        Assert.That(File.ReadAllText(Path.Combine(_output, "a.txt")), Is.EqualTo("changed"));
    }

    [Test]
    public void WhatWasBuiltFromAnAssetNoLongerListed_IsDeleted()
    {
        Write("a.txt", "a");
        Write("b.txt", "b");
        Build("a.txt", "b.txt");

        Build("a.txt");

        Assert.That(File.Exists(Path.Combine(_output, "a.txt")), Is.True);
        Assert.That(File.Exists(Path.Combine(_output, "b.txt")), Is.False);
    }

    [Test]
    public void AFileTheBuildDidNotWrite_IsNotDeleted()
    {
        Write("a.txt", "a");
        Build("a.txt");
        File.WriteAllText(Path.Combine(_output, "mine.txt"), "mine");

        Build();

        Assert.That(File.Exists(Path.Combine(_output, "mine.txt")), Is.True);
        Assert.That(File.Exists(Path.Combine(_output, "a.txt")), Is.False);
    }

    [Test]
    public void ACompanionAnotherAssetStillWrites_IsKept()
    {
        Directory.CreateDirectory(_output);
        File.WriteAllText(Path.Combine(_output, "x.aemf"), "x");
        File.WriteAllText(Path.Combine(_output, "y.aemf"), "y");
        File.WriteAllText(Path.Combine(_output, "shared.png"), "png");
        var cache = new BuildCache();
        cache.Entries["x.obj"] = new CacheEntry { Output = "x.aemf", Companions = ["shared.png"] };
        cache.Entries["y.obj"] = new CacheEntry { Output = "y.aemf", Companions = ["shared.png"] };

        cache.Prune(["y.obj"], _output);

        Assert.That(File.Exists(Path.Combine(_output, "x.aemf")), Is.False);
        Assert.That(File.Exists(Path.Combine(_output, "shared.png")), Is.True);
        Assert.That(cache.Entries.ContainsKey("x.obj"), Is.False);
    }

    [Test]
    public void Prune_NeverDeletesAFileOutsideTheOutput()
    {
        var outside = Path.Combine(_project, "outside.txt");
        File.WriteAllText(outside, "mine");
        var cache = new BuildCache();
        cache.Entries["x.obj"] = new CacheEntry { Output = "../outside.txt" };

        cache.Prune([], _output);

        Assert.That(File.Exists(outside), Is.True);
    }

    [Test]
    public void ABuildByAnotherVersionOfTheCompiler_BuildsEverythingAgain()
    {
        Write("a.txt", "a");
        Build("a.txt");
        var cachePath = Path.Combine(_project, "cache", ".acache");
        var cache = BuildCache.Load(cachePath);
        cache.Compiler = "another";
        cache.Save(cachePath);

        var result = Build("a.txt");

        Assert.That(result.Copied, Is.EqualTo(1));
        Assert.That(result.Skipped, Is.EqualTo(0));
    }

    [Test]
    public void ABuildByAnotherVersionOfTheCompiler_CopiesAModelAgain_WithoutImportingIt()
    {
        Write("Models/triangle.obj", Triangle);
        Build("Models/triangle.obj");
        var cachePath = Path.Combine(_project, "cache", ".acache");
        var cache = BuildCache.Load(cachePath);
        cache.Compiler = "another";
        cache.Save(cachePath);

        var result = Build("Models/triangle.obj");

        Assert.Multiple(() =>
        {
            Assert.That(result.Cooked, Is.EqualTo(0), result.ToString());
            Assert.That(result.Copied, Is.EqualTo(1));
            Assert.That(File.Exists(Path.Combine(_output, "Models", "triangle.obj.aemf")), Is.True);
        });
    }

    [Test]
    public void AnArtifactTheWriterFailedHalfway_IsNotLeftBehind()
    {
        Write("a.companion", Path.Combine(_project, "a.companion") + "\nsame.companion");
        var builder = new ContentBuilder();
        builder.Register(new CompanionImporter(), new BytesWriter(true));
        var manifest = Manifest();
        manifest.SetSources(["a.companion"]);

        var result = builder.Build(manifest, _project);

        Assert.That(result.Failed, Is.EqualTo(1));
        Assert.That(File.Exists(Path.Combine(_output, "a.companion.bytes")), Is.False);
        Assert.That(File.Exists(Path.Combine(_project, "a.companion.bytes")), Is.False);
        Assert.That(File.Exists(Path.Combine(_project, "a.companion.bytes.writing")), Is.False);
    }

    [Test]
    public void ACompanionOutsideTheOutput_IsLeftBehindWithAWarning()
    {
        var texture = Path.Combine(_project, "tex.png");
        File.WriteAllText(texture, "png");
        Write("Models/a.companion", texture + "\n../../escaped.png");
        var builder = new ContentBuilder();
        builder.Register(new CompanionImporter(), new BytesWriter(false));
        var manifest = Manifest();
        manifest.SetSources(["Models/a.companion"]);
        List<string> log = [];

        var result = builder.Build(manifest, _project, log.Add);

        Assert.That(result.Cooked, Is.EqualTo(1));
        Assert.That(File.Exists(Path.Combine(_project, "escaped.png")), Is.False);
        Assert.That(log, Has.Some.Contains("warning ACB003"));
    }

    [Test]
    public void AnAbsoluteTextureInTheModelsFolder_IsReferencedRelativelyAndCarried()
    {
        var texture = Path.Combine(_project, "Models", "tex.png");
        Write("Models/tex.png", "png");
        Write("Models/tri.mtl", "newmtl m\nmap_Kd " + texture + "\n");
        Write("Models/tri.obj", "mtllib tri.mtl\nusemtl m\n" + Triangle);

        var result = Build("Models/tri.obj");

        Assert.That(result.Cooked, Is.EqualTo(1), result.ToString());
        var file = AssetFile.Read(Path.Combine(_output, "Models", "tri.obj.aemf"), AssetFileSection.Body);
        var scene = SceneDataSerializer.Deserialize(file.Body);
        var names = scene.Images.Values.Select(image => image.ImageName).ToList();
        Assert.That(names, Has.Count.EqualTo(1));
        Assert.That(names[0], Is.EqualTo("tex.png"));
        Assert.That(File.Exists(Path.Combine(_output, "Models", "tex.png")), Is.True);
    }

    [Test]
    public void AFileOutsideTheProject_IsNotWrittenOutsideTheOutput()
    {
        var outside = Path.Combine(Path.GetDirectoryName(_project), Path.GetFileName(_project) + "-outside.txt");
        File.WriteAllText(outside, "outside");
        try
        {
            var result = Build(Path.GetRelativePath(_project, outside));

            Assert.That(result.Failed, Is.EqualTo(1));
            Assert.That(Directory.Exists(_output) ? Directory.GetFiles(_output, "*", SearchOption.AllDirectories) : [],
                Is.Empty);
        }
        finally
        {
            File.Delete(outside);
        }
    }

    [Test]
    public void AFileOutsideTheProject_GoesUnderTheNameItIsLinkedAs()
    {
        var outside = Path.Combine(Path.GetDirectoryName(_project), Path.GetFileName(_project) + "-linked.txt");
        File.WriteAllText(outside, "linked");
        try
        {
            var manifest = Manifest();
            manifest.Assets.Add(new ContentAsset { Source = Path.GetRelativePath(_project, outside), LogicalName = "Fonts/linked.txt" });

            var result = new ContentBuilder().Build(manifest, _project);

            Assert.That(result.Copied, Is.EqualTo(1));
            Assert.That(File.ReadAllText(Path.Combine(_output, "Fonts", "linked.txt")), Is.EqualTo("linked"));
        }
        finally
        {
            File.Delete(outside);
        }
    }

    [Test]
    public void AnImporterNamedButUnknown_Fails()
    {
        Write("a.txt", "a");
        var manifest = Manifest();
        manifest.Assets.Add(new ContentAsset { Source = "a.txt", Importer = "Nope" });

        var result = new ContentBuilder().Build(manifest, _project);

        Assert.That(result.Failed, Is.EqualTo(1));
        Assert.That(File.Exists(Path.Combine(_output, "a.txt")), Is.False);
    }

    [Test]
    public void SetSources_KeepsTheSettingsOfAnAssetAlreadyListed()
    {
        var manifest = new ContentManifest();
        manifest.Assets.Add(new ContentAsset { Source = "Models/a.dae", LogicalName = "a" });
        manifest.Assets.Add(new ContentAsset { Source = "gone.txt" });

        manifest.SetSources(["Models\\a.dae", "b.txt"]);

        Assert.That(manifest.Assets, Has.Count.EqualTo(2));
        Assert.That(manifest.Assets[0].LogicalName, Is.EqualTo("a"));
        Assert.That(manifest.Assets[1].Source, Is.EqualTo("b.txt"));
    }

    [Test]
    public void Import_WritesTheAssetFileBesideItsSource_AndNothingToTheOutput()
    {
        Write("Models/triangle.obj", Triangle);
        Write("Notes/readme.txt", "hello");
        var manifest = Manifest();
        manifest.SetSources(["Models/triangle.obj", "Notes/readme.txt"]);

        var result = new ContentBuilder().Import(manifest, _project);

        Assert.Multiple(() =>
        {
            Assert.That(result.Cooked, Is.EqualTo(1), result.ToString());
            Assert.That(result.Copied, Is.EqualTo(0));
            Assert.That(File.Exists(Path.Combine(_project, "Models", "triangle.obj.aemf")), Is.True);
            Assert.That(Directory.Exists(_output), Is.False);
        });
    }

    [Test]
    public void Import_LeavesAnUpToDateAssetFile_AndBuildStillCopiesIt()
    {
        Write("Models/triangle.obj", Triangle);
        var manifest = Manifest();
        manifest.SetSources(["Models/triangle.obj"]);
        new ContentBuilder().Import(manifest, _project);
        var asset = Path.Combine(_project, "Models", "triangle.obj.aemf");
        var written = File.GetLastWriteTimeUtc(asset);

        var again = new ContentBuilder().Import(manifest, _project);
        var built = new ContentBuilder().Build(manifest, _project);

        Assert.Multiple(() =>
        {
            Assert.That(again.Skipped, Is.EqualTo(1), again.ToString());
            Assert.That(File.GetLastWriteTimeUtc(asset), Is.EqualTo(written));
            Assert.That(built.Cooked, Is.EqualTo(0), built.ToString());
            Assert.That(File.Exists(Path.Combine(_output, "Models", "triangle.obj.aemf")), Is.True);
        });
    }

    [Test]
    public void Import_StoppedBeforeItStarts_ImportsNothing()
    {
        Write("Models/triangle.obj", Triangle);
        var manifest = Manifest();
        manifest.SetSources(["Models/triangle.obj"]);

        var result = new ContentBuilder().Import(manifest, _project, cancellation: new CancellationToken(true));

        Assert.That(result.Cooked + result.Skipped + result.Failed, Is.EqualTo(0), result.ToString());
        Assert.That(File.Exists(Path.Combine(_project, "Models", "triangle.obj.aemf")), Is.False);
    }

    [Test]
    public void AnImportAndABuildOfOneProjectAtOnce_BothSucceed()
    {
        for (var index = 0; index < 8; index++)
        {
            Write($"Models/m{index}.obj", Triangle + $"v {index} 1 0\n");
        }

        var sources = Enumerable.Range(0, 8).Select(index => $"Models/m{index}.obj").ToList();
        var importing = Manifest();
        importing.SetSources(sources);
        var building = Manifest();
        building.SetSources(sources);

        var import = Task.Run(() => new ContentBuilder().Import(importing, _project));
        var build = Task.Run(() => new ContentBuilder().Build(building, _project));
        Task.WaitAll(import, build);

        Assert.Multiple(() =>
        {
            Assert.That(import.Result.Failed, Is.EqualTo(0), import.Result.ToString());
            Assert.That(build.Result.Failed, Is.EqualTo(0), build.Result.ToString());
            Assert.That(import.Result.Cooked + build.Result.Cooked, Is.EqualTo(8));
            Assert.That(Directory.EnumerateFiles(Path.Combine(_project, "Models"), "*.writing"), Is.Empty);
        });
    }

    [Test]
    public void ListedFiles_AreTheAssets_ALinkedOneUnderItsLink()
    {
        var manifest = Manifest();
        manifest.Assets.Add(new ContentAsset { Source = "Models/a.obj", LogicalName = "Other/a.obj", Parameters = new() { ["Scale"] = "2" } });
        List<string> log = [];

        manifest.SetListedFiles(_project,
            [(Path.Combine(_project, "Models", "a.obj"), ""), (Path.Combine(_project, "..", "Shared", "b.obj"), @"Models\b.obj")],
            "Content.acontent", log.Add);

        Assert.Multiple(() =>
        {
            Assert.That(manifest.Assets.Select(asset => asset.Source), Is.EqualTo((string[])["Models/a.obj", "../Shared/b.obj"]));
            Assert.That(manifest.Assets.Select(asset => asset.LogicalName), Is.EqualTo((string[])[null, "Models/b.obj"]));
            Assert.That(manifest.Assets[0].Parameters["Scale"], Is.EqualTo("2"));
            Assert.That(log, Has.One.StartsWith("Content.acontent : warning ACB007"));
        });
    }

    private BuildResult Build(params string[] sources)
    {
        var manifest = Manifest();
        manifest.SetSources(sources);
        return new ContentBuilder().Build(manifest, _project);
    }

    private ContentManifest Manifest()
    {
        return new ContentManifest
        {
            OutputDirectory = _output,
            IntermediateDirectory = Path.Combine(_project, "cache")
        };
    }

    private void Write(string relative, string text)
    {
        var full = Path.Combine(_project, relative);
        Directory.CreateDirectory(Path.GetDirectoryName(full));
        File.WriteAllText(full, text);
    }
}
