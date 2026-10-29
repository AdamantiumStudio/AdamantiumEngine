using System.IO;
using Adamantium.ECS;
using Adamantium.Engine.Templates;
using Adamantium.Graphics.Core.Assets;
using Adamantium.Graphics.Core.Models;
using Adamantium.Mathematics;
using Adamantium.Multiverse;
using NUnit.Framework;

namespace Adamantium.EngineTests;

/// <summary>A model is named by its file, and keeps the path it was asked for beside the name: a long path in the name
/// filled the hierarchy's row.</summary>
[TestFixture]
public class SceneDataAssetReaderTests
{
    [Test]
    public void AModel_IsNamedByItsFile_AndKeepsWhereItWasLoadedFrom()
    {
        var folder = Path.Combine(Path.GetTempPath(), Path.GetRandomFileName());
        Directory.CreateDirectory(folder);
        try
        {
            var file = Path.Combine(folder, "box.obj");
            File.WriteAllText(file, "v 0 0 0\nv 1 0 0\nv 0 1 0\nf 1 2 3\n");

            var scene = (SceneData)new SceneDataAssetReader().ReadAssetAsync(null,
                new AssetReaderParameters { AssetName = "Models/Box/box.obj", AssetPath = file }).GetAwaiter().GetResult();

            Assert.Multiple(() =>
            {
                Assert.That(scene.Name, Is.EqualTo("box.obj"));
                Assert.That(scene.Source, Is.EqualTo("Models/Box/box.obj"));
            });
        }
        finally
        {
            Directory.Delete(folder, true);
        }
    }

    [Test]
    public void AnImportedModel_CarriesItsSource()
    {
        var scene = new SceneData { Name = "box.obj", Source = "Models/Box/box.obj" };
        scene.Models = scene.CreateMesh(null, "", "Model");

        var model = new EntityImportTemplate(scene, null, Vector3<double>.Zero).BuildEntity(new Entity()).Result;

        Assert.Multiple(() =>
        {
            Assert.That(model.Name, Is.EqualTo("box.obj"));
            Assert.That(model.Source, Is.EqualTo("Models/Box/box.obj"));
        });
    }
}
