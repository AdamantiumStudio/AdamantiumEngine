using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using Adamantium.Content.Pipeline;
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
    public void AModel_IsCookedUnderItsSourceNamePlusTheArtifactExtension()
    {
        Write("Models/triangle.obj", Triangle);

        var result = Build("Models/triangle.obj");

        Assert.That(result.Cooked, Is.EqualTo(1), result.ToString());
        Assert.That(File.Exists(Path.Combine(_output, "Models", "triangle.obj.aemf")), Is.True);
        Assert.That(File.Exists(Path.Combine(_output, "Models", "triangle.obj")), Is.False);
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
        var scene = SceneDataSerializer.Deserialize(File.ReadAllBytes(Path.Combine(_output, "Models", "tri.obj.aemf")));
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
