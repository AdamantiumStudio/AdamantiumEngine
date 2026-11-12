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
    public void ANewModel_IsImportedWithTheDefaultParameters_AndRecordsThem()
    {
        Write("Models/triangle.obj", Triangle);
        var builder = new ContentBuilder();
        builder.SetDefaultParameters("Model", new Dictionary<string, string> { ["Scale"] = "3" });

        var result = Import(builder, "Models/triangle.obj");

        Assert.Multiple(() =>
        {
            Assert.That(result.Cooked, Is.EqualTo(1), result.ToString());
            Assert.That(RecordedParameters("Models/triangle.obj"), Is.EqualTo(new Dictionary<string, string> { ["Scale"] = "3" }));
        });
    }

    [Test]
    public void ChangedDefaults_DoNotImportAModelAgain()
    {
        Write("Models/triangle.obj", Triangle);
        Import(new ContentBuilder(), "Models/triangle.obj");
        var builder = new ContentBuilder();
        builder.SetDefaultParameters("Model", new Dictionary<string, string> { ["Scale"] = "3" });

        var result = Import(builder, "Models/triangle.obj");

        Assert.That(result.Cooked, Is.EqualTo(0), result.ToString());
    }

    [Test]
    public void AChangedSource_IsImportedAgainWithTheParametersItsAssetFileRecords_NotTheDefaults()
    {
        Write("Models/triangle.obj", Triangle);
        var first = new ContentBuilder();
        first.SetDefaultParameters("Model", new Dictionary<string, string> { ["Scale"] = "2" });
        Import(first, "Models/triangle.obj");
        Write("Models/triangle.obj", Triangle + "v 0 0 1\n");
        var builder = new ContentBuilder();
        builder.SetDefaultParameters("Model", new Dictionary<string, string> { ["Scale"] = "5" });

        var result = Import(builder, "Models/triangle.obj");

        Assert.Multiple(() =>
        {
            Assert.That(result.Imported.Single().Reason, Is.EqualTo(ImportReason.SourceChanged), result.ToString());
            Assert.That(RecordedParameters("Models/triangle.obj"), Is.EqualTo(new Dictionary<string, string> { ["Scale"] = "2" }));
        });
    }

    [Test]
    public void ImportAsset_ImportsAnUpToDateAsset_WithTheParametersAsked()
    {
        Write("Models/triangle.obj", Triangle);
        Import(new ContentBuilder(), "Models/triangle.obj");
        var manifest = Manifest();
        manifest.SetSources(["Models/triangle.obj"]);

        var result = new ContentBuilder().ImportAsset(manifest, _project, Path.Combine(_project, "Models", "triangle.obj"),
            new Dictionary<string, string> { ["Scale"] = "4", ["UpAxis"] = "Z" });

        Assert.Multiple(() =>
        {
            Assert.That(result.Imported.Single().Reason, Is.EqualTo(ImportReason.Asked), result.ToString());
            Assert.That(RecordedParameters("Models/triangle.obj"),
                Is.EqualTo(new Dictionary<string, string> { ["Scale"] = "4", ["UpAxis"] = "Z" }));
            Assert.That(Import(new ContentBuilder(), "Models/triangle.obj").Cooked, Is.EqualTo(0));
        });
    }

    [Test]
    public void ImportAsset_TakesTheManifestsParameters_WhenItGivesSome()
    {
        Write("Models/triangle.obj", Triangle);
        var manifest = Manifest();
        manifest.SetSources(["Models/triangle.obj"]);
        manifest.Assets[0].Parameters["Scale"] = "2";

        new ContentBuilder().ImportAsset(manifest, _project, Path.Combine(_project, "Models", "triangle.obj"),
            new Dictionary<string, string> { ["Scale"] = "4" });

        Assert.That(RecordedParameters("Models/triangle.obj"), Is.EqualTo(new Dictionary<string, string> { ["Scale"] = "2" }));
    }

    [Test]
    public void ImportAsset_LeavesAnAssetFileOfANewerFormatAsItIs()
    {
        Write("Models/triangle.obj", Triangle);
        Import(new ContentBuilder(), "Models/triangle.obj");
        var asset = Path.Combine(_project, "Models", "triangle.obj.aemf");
        var bytes = File.ReadAllBytes(asset);
        bytes[4] = AssetFile.FormatVersion + 1;
        File.WriteAllBytes(asset, bytes);
        var manifest = Manifest();
        manifest.SetSources(["Models/triangle.obj"]);
        List<string> log = [];

        var result = new ContentBuilder().ImportAsset(manifest, _project, Path.Combine(_project, "Models", "triangle.obj"),
            new Dictionary<string, string>(), log.Add);

        Assert.Multiple(() =>
        {
            Assert.That(result.Failed, Is.EqualTo(1), result.ToString());
            Assert.That(log, Has.One.Contains("error ACB004"));
            Assert.That(File.ReadAllBytes(asset), Is.EqualTo(bytes));
        });
    }

    [Test]
    public void AnAssetFile_SaysWhichSectionsItHas_WithoutReadingThem()
    {
        Write("Models/triangle.obj", Triangle);
        Import(new ContentBuilder(), "Models/triangle.obj");
        var asset = Path.Combine(_project, "Models", "triangle.obj.aemf");
        ContentBuilder.WriteThumbnail(_project, asset, [1, 2, 3], File.GetLastWriteTimeUtc(asset));

        var file = AssetFile.Read(asset, AssetFileSection.Source);

        Assert.Multiple(() =>
        {
            Assert.That(file.Sections, Is.EqualTo(AssetFileSection.Thumbnail | AssetFileSection.Source | AssetFileSection.Body));
            Assert.That(file.Thumbnail, Is.Null);
        });
    }

    [Test]
    public void ImportAsset_OfAFileTheManifestDoesNotList_Fails()
    {
        Write("Models/triangle.obj", Triangle);
        List<string> log = [];

        var result = new ContentBuilder().ImportAsset(Manifest(), _project, Path.Combine(_project, "Models", "triangle.obj"),
            null, log.Add);

        Assert.Multiple(() =>
        {
            Assert.That(result.Failed, Is.EqualTo(1));
            Assert.That(log, Has.One.Contains("error ACB012"));
            Assert.That(File.Exists(Path.Combine(_project, "Models", "triangle.obj.aemf")), Is.False);
        });
    }

    [Test]
    public void FindSource_GivesTheSourceAnAssetFileWasImportedFrom_BesideItOrLinked()
    {
        var manifest = Manifest();
        manifest.SetSources(["Models/triangle.obj"]);
        manifest.Assets.Add(new ContentAsset { Source = "../Shared/b.obj", LogicalName = "Models/b.obj" });
        var builder = new ContentBuilder();

        Assert.Multiple(() =>
        {
            Assert.That(builder.FindSource(manifest, _project, Path.Combine(_project, "Models", "triangle.obj.aemf")),
                Is.EqualTo(Path.Combine(_project, "Models", "triangle.obj")));
            Assert.That(builder.FindSource(manifest, _project, Path.Combine(_project, "obj", "assets", "Models", "b.obj.aemf")),
                Is.EqualTo(Path.GetFullPath(Path.Combine(_project, "..", "Shared", "b.obj"))));
            Assert.That(builder.FindSource(manifest, _project, Path.Combine(_project, "Models", "other.obj.aemf")), Is.Null);
            Assert.That(builder.ImporterNames, Is.EquivalentTo((string[])["Model"]));
        });
    }

    [Test]
    public void TheModelImporter_DeclaresItsSettings()
    {
        var builder = new ContentBuilder();

        var settings = builder.GetSettings(builder.GetImporterName("ship.glb"));

        Assert.Multiple(() =>
        {
            Assert.That(builder.GetImporterName("ship.glb"), Is.EqualTo("Model"));
            Assert.That(builder.GetImporterName("notes.txt"), Is.Null);
            Assert.That(settings.Select(setting => setting.Name), Is.EqualTo((string[])["Scale", "UpAxis", "Normals", "Animations"]));
            Assert.That(settings.Select(setting => setting.Kind), Is.EqualTo((ImportSettingKind[])
                [ImportSettingKind.Number, ImportSettingKind.Choice, ImportSettingKind.Choice, ImportSettingKind.Toggle]));
            Assert.That(settings[1].Choices.Select(choice => choice.Value), Is.EqualTo((string[])["File", "Y", "Z"]));
            Assert.That(builder.GetSettings("Nothing"), Is.Empty);
        });
    }

    [Test]
    public void AModelWithASettingItCannotRead_IsImportedWithTheDefault_AndAWarning()
    {
        Write("Models/triangle.obj", Triangle);
        var builder = new ContentBuilder();
        builder.SetDefaultParameters("Model", new Dictionary<string, string> { ["Scale"] = "-1", ["UpAxis"] = "W" });
        List<string> log = [];
        var manifest = Manifest();
        manifest.SetSources(["Models/triangle.obj"]);

        var result = builder.Import(manifest, _project, log.Add);

        Assert.Multiple(() =>
        {
            Assert.That(result.Cooked, Is.EqualTo(1), result.ToString());
            Assert.That(log.Count(line => line.Contains("warning ACB011")), Is.EqualTo(2));
        });
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
    public void WriteThumbnail_PutsThePictureIn_AndKeepsTheRestOfTheFile()
    {
        var asset = ImportTriangle();
        var before = AssetFile.Read(asset, AssetFileSection.All);

        var written = ContentBuilder.WriteThumbnail(_project, asset, [1, 2, 3], File.GetLastWriteTimeUtc(asset));

        var after = AssetFile.Read(asset, AssetFileSection.All);
        Assert.Multiple(() =>
        {
            Assert.That(written, Is.True);
            Assert.That(after.Thumbnail, Is.EqualTo(new byte[] { 1, 2, 3 }));
            Assert.That(after.Body, Is.EqualTo(before.Body));
            Assert.That(after.Source.Hash, Is.EqualTo(before.Source.Hash));
        });
    }

    // The picture was taken of the file as it was; written again since - imported anew - it holds another model.
    [Test]
    public void WriteThumbnail_OfAFileWrittenSince_WritesNothing()
    {
        var asset = ImportTriangle();
        var taken = File.GetLastWriteTimeUtc(asset);
        File.SetLastWriteTimeUtc(asset, taken.AddSeconds(5));

        var written = ContentBuilder.WriteThumbnail(_project, asset, [1, 2, 3], taken);

        Assert.Multiple(() =>
        {
            Assert.That(written, Is.False);
            Assert.That(AssetFile.Read(asset, AssetFileSection.Thumbnail).Thumbnail, Is.Null);
        });
    }

    // The new asset file has no thumbnail, as the old one showed what the asset held before; the result says it had one,
    // so the editor that made it can make it again.
    [Test]
    public void AModelImportedAgain_LosesItsThumbnail_AndSaysItHadOne()
    {
        var asset = ImportTriangle();
        ContentBuilder.WriteThumbnail(_project, asset, [1, 2, 3], File.GetLastWriteTimeUtc(asset));
        Write("Models/triangle.obj", Triangle.Replace("v 1 0 0", "v 2 0 0"));
        var manifest = Manifest();
        manifest.SetSources(["Models/triangle.obj"]);

        var result = new ContentBuilder().Import(manifest, _project);

        Assert.Multiple(() =>
        {
            Assert.That(AssetFile.Read(asset, AssetFileSection.Thumbnail).Thumbnail, Is.Null);
            Assert.That(result.Imported, Has.Count.EqualTo(1));
            Assert.That(result.Imported[0].AssetFile, Is.EqualTo(asset));
            Assert.That(result.Imported[0].Reason, Is.EqualTo(ImportReason.SourceChanged));
            Assert.That(result.Imported[0].HadThumbnail, Is.True);
        });
    }

    [Test]
    public void ANewModel_IsImportedAsNew_WithNoThumbnailBefore()
    {
        Write("Models/triangle.obj", Triangle);
        var manifest = Manifest();
        manifest.SetSources(["Models/triangle.obj"]);

        var result = new ContentBuilder().Import(manifest, _project);

        Assert.Multiple(() =>
        {
            Assert.That(result.Imported, Has.Count.EqualTo(1));
            Assert.That(result.Imported[0].Reason, Is.EqualTo(ImportReason.New));
            Assert.That(result.Imported[0].HadThumbnail, Is.False);
        });
    }

    private string ImportTriangle()
    {
        Write("Models/triangle.obj", Triangle);
        var manifest = Manifest();
        manifest.SetSources(["Models/triangle.obj"]);
        new ContentBuilder().Import(manifest, _project);
        return Path.Combine(_project, "Models", "triangle.obj.aemf");
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
    public void ASourceWrittenWithTheSameBytes_IsNotImportedAgain()
    {
        Write("Models/triangle.obj", Triangle);
        Build("Models/triangle.obj");
        var source = Path.Combine(_project, "Models", "triangle.obj");
        File.SetLastWriteTimeUtc(source, File.GetLastWriteTimeUtc(source).AddMinutes(1));

        var result = Build("Models/triangle.obj");

        Assert.That(result.Cooked, Is.EqualTo(0), result.ToString());
    }

    [Test]
    public void TheAssetFile_RecordsWhenItsSourceWasWrittenAndItsLength()
    {
        Write("Models/triangle.obj", Triangle);
        var source = Path.Combine(_project, "Models", "triangle.obj");

        Build("Models/triangle.obj");

        var recorded = AssetFile.Read(source + ".aemf", AssetFileSection.Source).Source;
        Assert.That(recorded.Written, Is.EqualTo(File.GetLastWriteTimeUtc(source)));
        Assert.That(recorded.Length, Is.EqualTo(new FileInfo(source).Length));
    }

    [Test]
    public void AModelThatCannotBeRead_FailsAndWritesNoAssetFile()
    {
        Write("Models/broken.dae", "this is not a Collada document");
        List<string> log = [];
        var manifest = Manifest();
        manifest.SetSources(["Models/broken.dae"]);

        var result = new ContentBuilder().Import(manifest, _project, log.Add);

        Assert.Multiple(() =>
        {
            Assert.That(result.Failed, Is.EqualTo(1), result.ToString());
            Assert.That(log, Has.One.Contains("error ACB004"));
            Assert.That(File.Exists(Path.Combine(_project, "Models", "broken.dae.aemf")), Is.False);
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

    private BuildResult Import(ContentBuilder builder, params string[] sources)
    {
        var manifest = Manifest();
        manifest.SetSources(sources);
        return builder.Import(manifest, _project);
    }

    private Dictionary<string, string> RecordedParameters(string source) =>
        AssetFile.Read(Path.Combine(_project, source + ".aemf"), AssetFileSection.Source).Source.Parameters;

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
