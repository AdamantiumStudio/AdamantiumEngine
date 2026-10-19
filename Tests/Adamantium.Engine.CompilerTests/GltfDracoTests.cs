using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Security.Cryptography;
using System.Text.Json;
using System.Text.Json.Nodes;
using Adamantium.Engine.Compiler.Converter.Configs;
using Adamantium.Engine.Compiler.Models;
using Adamantium.Engine.Compiler.Models.Converters;
using Adamantium.Engine.Compiler.Models.Gltf;
using Adamantium.Engine.Compiler.Models.Gltf.Draco;
using Adamantium.Graphics.Core.Models;
using Adamantium.Mathematics;
using NUnit.Framework;

namespace Adamantium.Engine.CompilerTests;

/// <summary>Draco meshes decode as the reference decoder decodes them: their triangles, and each point's every
/// attribute.</summary>
[TestFixture]
public class GltfDracoTests
{
    private static readonly string Data = Path.Combine(AppContext.BaseDirectory, "CompilerGltf");

    private string folder;

    [SetUp]
    public void SetUp()
    {
        folder = Path.Combine(Path.GetTempPath(), "adamantium-draco-" + Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(folder);
    }

    [TearDown]
    public void TearDown()
    {
        if (Directory.Exists(folder))
        {
            Directory.Delete(folder, true);
        }
    }

    [TestCaseSource(nameof(Vectors))]
    public void Decodes_AsTheReferenceDecoder(byte[] data, JsonElement expected)
    {
        var mesh = DracoDecoder.Decode(data);

        Assert.That(mesh.Indices.Length / 3, Is.EqualTo(expected.GetProperty("faces").GetInt32()));
        Assert.That(mesh.PointCount, Is.EqualTo(expected.GetProperty("points").GetInt32()));
        var indices = new byte[mesh.Indices.Length * 4];
        Buffer.BlockCopy(mesh.Indices, 0, indices, 0, indices.Length);
        Assert.That(Sha(indices), Is.EqualTo(expected.GetProperty("indices").GetString()), "indices");
        var attributes = expected.GetProperty("attributes").EnumerateArray().ToList();
        Assert.That(mesh.Attributes.Count, Is.EqualTo(attributes.Count));
        for (int i = 0; i < attributes.Count; i++)
        {
            var attribute = mesh.Attributes[i];
            var what = $"attribute {i}";
            Assert.That(attribute.UniqueId, Is.EqualTo(attributes[i].GetProperty("id").GetUInt32()), what);
            Assert.That((int)attribute.DataType, Is.EqualTo(attributes[i].GetProperty("type").GetInt32()), what);
            Assert.That(attribute.Components, Is.EqualTo(attributes[i].GetProperty("components").GetInt32()), what);
            var values = attribute.PointValues(mesh.PointCount);
            Assert.That(values.Length, Is.EqualTo(attributes[i].GetProperty("length").GetInt32()), what);
            Assert.That(Sha(values), Is.EqualTo(attributes[i].GetProperty("sha").GetString()), what);
        }
    }

    [Test]
    public void ABrokenMesh_IsRefused()
    {
        var data = (byte[])Vectors().First().Arguments[0];
        var otherVersion = data.ToArray();
        otherVersion[6] = 1;

        Assert.Throws<InvalidDataException>(() => DracoDecoder.Decode(data[..^40]));
        Assert.Throws<InvalidDataException>(() => DracoDecoder.Decode(otherVersion));
        Assert.Throws<InvalidDataException>(() => DracoDecoder.Decode([.. "GLTF"u8, .. data[4..]]));
    }

    [TestCase("eb-hole-flags.drc")]
    [TestCase("eb-huge-faces.drc")]
    [TestCase("seq-huge-points.drc")]
    [TestCase("seq-huge-values.drc")]
    [TestCase("seq-int32-overflow.drc")]
    public void AMeshOverWhatTheAssetSays_IsRefusedBeforeItIsAllocated(string name)
    {
        var data = File.ReadAllBytes(Path.Combine(Data, "DracoBroken", name));

        Assert.Throws<InvalidDataException>(() => DracoDecoder.Decode(data, 1000, 1000));
    }

    [TestCase("tagged-bits-2k.drc")]
    [TestCase("texcoord-abs.drc")]
    public void ADataTheReferenceRefuses_IsRefused(string name)
    {
        var data = File.ReadAllBytes(Path.Combine(Data, "DracoBroken", name));

        Assert.Throws<InvalidDataException>(() => DracoDecoder.Decode(data));
    }

    [TestCase("no indices")]
    [TestCase("points")]
    [TestCase("an attribute Draco has not")]
    [TestCase("a buffer view past the asset")]
    [TestCase("a buffer view as text")]
    [TestCase("no attributes")]
    [TestCase("an accessor past the asset")]
    [TestCase("signed indices")]
    [TestCase("fewer indices")]
    public void ABrokenDracoPrimitive_IsRefused(string breakage)
    {
        var json = JsonNode.Parse(File.ReadAllText(Path.Combine(Data, "RiggedFigureDraco", "RiggedFigure.gltf")));
        var primitive = json["meshes"][0]["primitives"][0].AsObject();
        var draco = primitive["extensions"]["KHR_draco_mesh_compression"].AsObject();
        var indices = json["accessors"][primitive["indices"].GetValue<int>()].AsObject();
        switch (breakage)
        {
            case "no indices":
                primitive.Remove("indices");
                break;
            case "points":
                primitive["mode"] = 0;
                break;
            case "an attribute Draco has not":
                draco["attributes"]["POSITION"] = 99;
                break;
            case "a buffer view past the asset":
                draco["bufferView"] = 99;
                break;
            case "a buffer view as text":
                draco["bufferView"] = "4";
                break;
            case "no attributes":
                draco["attributes"] = null;
                break;
            case "an accessor past the asset":
                primitive["attributes"]["NORMAL"] = 999;
                break;
            case "signed indices":
                indices["componentType"] = 5122;
                break;
            case "fewer indices":
                indices["count"] = indices["count"].GetValue<int>() - 3;
                break;
        }

        File.Copy(Path.Combine(Data, "RiggedFigureDraco", "RiggedFigure0.bin"), Path.Combine(folder, "RiggedFigure0.bin"));
        var path = Path.Combine(folder, "RiggedFigure.gltf");
        File.WriteAllText(path, json.ToJsonString());

        Assert.Throws<InvalidDataException>(() => GltfFile.Read(path));
    }

    [Test]
    public void ADracoAsset_ImportsAsItsUncompressedSelf_AndDracoIsNotToldUnused()
    {
        var path = Path.Combine(Data, "RiggedFigureDraco", "RiggedFigure.gltf");
        var draco = Meshes(new ModelConverter().ImportFile(path));
        var plain = Meshes(new ModelConverter().ImportFile(Path.Combine(Data, "RiggedFigure", "RiggedFigure.gltf")));
        var told = new GltfConverter(path, new ConversionConfig(true));
        told.StartConversion();

        Assert.That(told.UnsupportedFeatures, Has.None.Contains("KHR_draco_mesh_compression"));
        Assert.That(draco.Count, Is.EqualTo(plain.Count));
        for (int m = 0; m < draco.Count; m++)
        {
            var mesh = draco[m];
            var original = plain[m];
            Assert.That(mesh.Indices.Length, Is.EqualTo(original.Indices.Length));
            var taken = new bool[original.Indices.Length / 3];
            for (int t = 0; t < mesh.Indices.Length; t += 3)
            {
                var match = Nearest(original, Centroid(mesh, t), taken);
                taken[match / 3] = true;
                var turn = Enumerable.Range(0, 3).FirstOrDefault(r => Enumerable.Range(0, 3).All(j =>
                    Vector3.Distance(original.Points[original.Indices[match + (r + j) % 3]], mesh.Points[mesh.Indices[t + j]]) < 0.01), -1);
                Assert.That(turn, Is.Not.EqualTo(-1), $"mesh {m} triangle {t / 3} has the corners of no original one, in its order");
                for (int j = 0; j < 3; j++)
                {
                    var corner = mesh.Indices[t + j];
                    var given = original.Indices[match + (turn + j) % 3];
                    var what = $"mesh {m} triangle {t / 3} corner {j}";
                    Assert.That(Vector3F.Distance(original.Normals[given], mesh.Normals[corner]), Is.LessThan(0.05f), what);
                    Assert.That(mesh.JointIndices[corner], Is.EqualTo(original.JointIndices[given]), what);
                    Assert.That(Vector4F.Distance(original.JointWeights[given], mesh.JointWeights[corner]), Is.LessThan(0.02f), what);
                }
            }
        }
    }

    private static Vector3 Centroid(Mesh mesh, int first) =>
        (mesh.Points[mesh.Indices[first]] + mesh.Points[mesh.Indices[first + 1]] + mesh.Points[mesh.Indices[first + 2]]) / 3;

    private static int Nearest(Mesh mesh, Vector3 centroid, bool[] taken) =>
        Enumerable.Range(0, mesh.Indices.Length / 3).Where(t => !taken[t]).Select(t => t * 3)
            .MinBy(t => Vector3.Distance(Centroid(mesh, t), centroid));

    private static List<Mesh> Meshes(SceneData scene)
    {
        var meshes = new List<Mesh>();
        var stack = new Stack<SceneData.Model>();
        stack.Push(scene.Models);
        while (stack.Count > 0)
        {
            var model = stack.Pop();
            meshes.AddRange(model.Meshes);
            foreach (var child in model.Dependencies)
            {
                stack.Push(child);
            }
        }

        return meshes;
    }

    private static IEnumerable<TestCaseData> Vectors()
    {
        using var json = JsonDocument.Parse(File.ReadAllText(Path.Combine(Data, "DracoVectors.json")));
        foreach (var vector in json.RootElement.EnumerateArray())
        {
            yield return new TestCaseData(vector.GetProperty("data").GetBytesFromBase64(), vector.Clone())
                .SetName($"Decodes_AsTheReferenceDecoder({vector.GetProperty("name").GetString()})");
        }
    }

    private static string Sha(byte[] bytes) => Convert.ToHexStringLower(SHA256.HashData(bytes));
}
