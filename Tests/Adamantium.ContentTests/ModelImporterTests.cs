using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using Adamantium.Content.Pipeline;
using Adamantium.Content.Pipeline.Importers;
using Adamantium.Graphics.Core.Models;
using Adamantium.Mathematics;
using NUnit.Framework;
using Vector3F = Adamantium.Mathematics.Vector3<float>;

namespace Adamantium.ContentTests;

[TestFixture]
public class ModelImporterTests
{
    // Upright in Y, two units tall; its normal lies along X, across the face, so a computed one tells itself apart.
    private const string Upright = "v 0 0 0\nv 1 0 0\nv 0 2 0\nvn 1 0 0\nf 1//1 2//1 3//1\n";

    private string _directory;

    [SetUp]
    public void SetUp()
    {
        _directory = Path.Combine(Path.GetTempPath(), "adamantium-model-importer-" + Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(_directory);
    }

    [TearDown]
    public void TearDown()
    {
        Directory.Delete(_directory, true);
    }

    [Test]
    public void Scale_MakesTheRootsUnitsLarger()
    {
        var plain = ImportObj(Upright, []);
        var scaled = ImportObj(Upright, new() { [ModelImporter.ScaleSetting] = "2.5" });

        Assert.That(scaled.Units.Value, Is.EqualTo(plain.Units.Value * 2.5f).Within(1e-6f));
    }

    [Test]
    public void UpAxisZ_StandsTheModelAlongAnotherAxis()
    {
        var fromFile = Extent(SingleMesh(ImportObj(Upright, [])));
        var z = Extent(SingleMesh(ImportObj(Upright, new() { [ModelImporter.UpAxisSetting] = "Z" })));

        Assert.Multiple(() =>
        {
            Assert.That(fromFile.Y, Is.EqualTo(2f).Within(1e-5f));
            Assert.That(z.Y, Is.EqualTo(0f).Within(1e-5f));
            Assert.That(z.Z, Is.EqualTo(2f).Within(1e-5f));
        });
    }

    [Test]
    public void NormalsAlways_ReplacesTheFilesNormals()
    {
        var kept = SingleMesh(ImportObj(Upright, []));
        var computed = SingleMesh(ImportObj(Upright, new() { [ModelImporter.NormalsSetting] = ModelImporter.AlwaysComputed }));

        Assert.Multiple(() =>
        {
            Assert.That(Math.Abs(kept.Normals[0].X), Is.GreaterThan(0.9f), "the file's normal, along X");
            Assert.That(Math.Abs(computed.Normals[0].X), Is.LessThan(0.1f), "one out of the face, across X");
        });
    }

    [Test]
    public void AnimationsOff_LeavesTheClipsOut()
    {
        var figure = Path.Combine(TestContext.CurrentContext.TestDirectory, "Models", "RiggedFigure", "RiggedFigure.gltf");

        var with = Import(figure, []);
        var without = Import(figure, new() { [ModelImporter.AnimationsSetting] = "false" });

        Assert.Multiple(() =>
        {
            Assert.That(with.Clips, Is.Not.Empty);
            Assert.That(without.Clips, Is.Empty);
            Assert.That(without.Controllers, Is.Not.Empty, "the skin stays, in its bind pose");
        });
    }

    private SceneData ImportObj(string text, Dictionary<string, string> parameters)
    {
        var path = Path.Combine(_directory, Guid.NewGuid().ToString("N") + ".obj");
        File.WriteAllText(path, text);
        return Import(path, parameters);
    }

    private static SceneData Import(string path, Dictionary<string, string> parameters) =>
        (SceneData)new ModelImporter().Import(path, new ContentBuildContext { Parameters = parameters });

    private static Mesh SingleMesh(SceneData scene)
    {
        var stack = new Stack<SceneData.Model>([scene.Models]);
        List<Mesh> meshes = [];
        while (stack.Count > 0)
        {
            var model = stack.Pop();
            meshes.AddRange(model.Meshes);
            foreach (var child in model.Dependencies)
            {
                stack.Push(child);
            }
        }

        return meshes.Single();
    }

    private static Vector3F Extent(Mesh mesh)
    {
        var points = mesh.Points.Select(point => point.As<float>()).ToArray();
        return new Vector3F(
            points.Max(point => point.X) - points.Min(point => point.X),
            points.Max(point => point.Y) - points.Min(point => point.Y),
            points.Max(point => point.Z) - points.Min(point => point.Z));
    }
}
