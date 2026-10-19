using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Text.Json;
using System.Text.Json.Nodes;
using Adamantium.Engine.Compiler.Models;
using Adamantium.Engine.Compiler.Models.Gltf;
using Adamantium.Graphics.Core.Models;
using Adamantium.Mathematics;
using NUnit.Framework;

namespace Adamantium.Engine.CompilerTests;

/// <summary>Buffer views compressed by EXT_meshopt_compression and KHR_meshopt_compression unpack as the reference
/// decoder unpacks them, and a compressed asset imports as its uncompressed self.</summary>
[TestFixture]
public class GltfMeshoptTests
{
    private static readonly string Data = Path.Combine(AppContext.BaseDirectory, "CompilerGltf");

    private string folder;

    [SetUp]
    public void SetUp()
    {
        folder = Path.Combine(Path.GetTempPath(), "adamantium-meshopt-" + Guid.NewGuid().ToString("N"));
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
    public void Unpacks_AsTheReferenceDecoder(GltfMeshoptCompression compression, byte[] encoded, byte[] expected)
    {
        var decoded = GltfMeshopt.Decode(compression, encoded);

        Assert.That(decoded.Length, Is.EqualTo(expected.Length));
        if (compression.Filter is "OCTAHEDRAL" or "QUATERNION" or "COLOR")
        {
            var size = compression.ByteStride / 4;
            var signed = compression.Filter != "COLOR";
            for (int at = 0; at < decoded.Length; at += size)
            {
                var passedThrough = compression.Filter == "OCTAHEDRAL" && at % compression.ByteStride == 3 * size;
                Assert.That(Component(decoded, at, size, signed),
                    Is.EqualTo(Component(expected, at, size, signed)).Within(passedThrough ? 0 : 1), $"component at byte {at}");
            }
        }
        else
        {
            Assert.That(decoded, Is.EqualTo(expected));
        }
    }

    [TestCase("ATTRIBUTES")]
    [TestCase("TRIANGLES")]
    [TestCase("INDICES")]
    public void ABrokenStream_IsRefused(string mode)
    {
        var vector = Vectors().First(v => ((GltfMeshoptCompression)v.Arguments[0]).Mode == mode);
        var compression = (GltfMeshoptCompression)vector.Arguments[0];
        var encoded = (byte[])vector.Arguments[1];
        var otherHeader = encoded.ToArray();
        otherHeader[0] ^= 0x40;

        Assert.Throws<InvalidDataException>(() => GltfMeshopt.Decode(compression, encoded[..^1]));
        Assert.Throws<InvalidDataException>(() => GltfMeshopt.Decode(compression, otherHeader));
    }

    [TestCase("ATTRIBUTES", 1_000_000_000, 4, "NONE")]
    [TestCase("TRIANGLES", 300_000_000, 4, "NONE")]
    [TestCase("INDICES", 1_000_000_000, 2, "NONE")]
    [TestCase("INDICES", 3, 2, "OCTAHEDRAL")]
    public void WhatTheBytesCannotHold_IsRefusedBeforeAnythingIsAllocated(string mode, int count, int stride, string filter)
    {
        var compression = new GltfMeshoptCompression { Mode = mode, Count = count, ByteStride = stride, Filter = filter };

        Assert.Throws<InvalidDataException>(() => GltfMeshopt.Decode(compression, new byte[40]));
    }

    [Test]
    public void EveryCompressedView_UnpacksIntoItsFallback_WhichIsNotLoaded()
    {
        var file = GltfFile.Read(Path.Combine(Data, "RiggedFigureExt.gltf"));
        var fallback = File.ReadAllBytes(Path.Combine(Data, "RiggedFigureExt.fallback.bin"));

        var compressed = 0;
        for (int i = 0; i < file.Root.BufferViews.Count; i++)
        {
            var view = file.Root.BufferViews[i];
            if (view.Extensions?.ContainsKey("EXT_meshopt_compression") != true)
            {
                continue;
            }

            compressed++;
            var unpacked = file.View(i).ToArray();
            var original = fallback.AsSpan(view.ByteOffset, view.ByteLength).ToArray();
            if (view.Extensions["EXT_meshopt_compression"].GetProperty("mode").GetString() == "TRIANGLES")
            {
                AssertSameTriangles(Shorts(unpacked), Shorts(original));
            }
            else
            {
                Assert.That(unpacked, Is.EqualTo(original), $"buffer view {i}");
            }

            Assert.That(file.Buffers[view.Buffer], Is.Null);
        }

        Assert.That(compressed, Is.EqualTo(8));
    }

    [Test]
    public void ACompressedAsset_ImportsAsItsUncompressedSelf()
    {
        var plain = new ModelConverter().ImportFile(WithoutCompression("RiggedFigureExt.gltf"));
        var exact = new ModelConverter().ImportFile(Path.Combine(Data, "RiggedFigureExt.gltf"));
        var filtered = new ModelConverter().ImportFile(Path.Combine(Data, "RiggedFigureKhr.glb"));

        AssertSame(exact, plain, 0);
        AssertSame(filtered, plain, 0.02f);
    }

    private static IEnumerable<TestCaseData> Vectors()
    {
        using var json = JsonDocument.Parse(File.ReadAllText(Path.Combine(Data, "MeshoptVectors.json")));
        foreach (var vector in json.RootElement.EnumerateArray())
        {
            var compression = new GltfMeshoptCompression
            {
                Mode = vector.GetProperty("mode").GetString(),
                Filter = vector.GetProperty("filter").GetString(),
                Count = vector.GetProperty("count").GetInt32(),
                ByteStride = vector.GetProperty("stride").GetInt32()
            };
            yield return new TestCaseData(compression, vector.GetProperty("encoded").GetBytesFromBase64(),
                    vector.GetProperty("decoded").GetBytesFromBase64())
                .SetName($"Unpacks_AsTheReferenceDecoder({vector.GetProperty("name").GetString()})");
        }
    }

    private static int Component(byte[] bytes, int at, int size, bool signed) => (size, signed) switch
    {
        (1, true) => (sbyte)bytes[at],
        (1, false) => bytes[at],
        (_, true) => BitConverter.ToInt16(bytes, at),
        _ => BitConverter.ToUInt16(bytes, at)
    };

    private string WithoutCompression(string name)
    {
        var json = JsonNode.Parse(File.ReadAllText(Path.Combine(Data, name)));
        foreach (var view in json["bufferViews"].AsArray())
        {
            view.AsObject().Remove("extensions");
        }

        foreach (var bin in Directory.GetFiles(Data, Path.GetFileNameWithoutExtension(name) + "*.bin"))
        {
            File.Copy(bin, Path.Combine(folder, Path.GetFileName(bin)));
        }

        var path = Path.Combine(folder, name);
        File.WriteAllText(path, json.ToJsonString());
        return path;
    }

    private static void AssertSame(SceneData actual, SceneData expected, float tolerance)
    {
        var actualMeshes = Meshes(actual);
        var expectedMeshes = Meshes(expected);
        Assert.That(actualMeshes.Count, Is.EqualTo(expectedMeshes.Count));
        for (int i = 0; i < actualMeshes.Count; i++)
        {
            AssertSameCorners(actualMeshes[i], expectedMeshes[i], tolerance);
        }

        var channels = actual.AnimationSources.SelectMany(a => a.Channels).ToList();
        var expectedChannels = expected.AnimationSources.SelectMany(a => a.Channels).ToList();
        Assert.That(channels.Count, Is.EqualTo(expectedChannels.Count));
        for (int i = 0; i < channels.Count; i++)
        {
            AssertClose(channels[i].Times, expectedChannels[i].Times, 0, $"channel {i} times");
            var values = channels[i].Path == "rotation"
                ? SignedAs(channels[i].Values, expectedChannels[i].Values)
                : channels[i].Values;
            AssertClose(values, expectedChannels[i].Values, tolerance, $"channel {i} values");
        }
    }

    private static void AssertSameTriangles(int[] actual, int[] expected)
    {
        Assert.That(actual.Length, Is.EqualTo(expected.Length));
        for (int t = 0; t < actual.Length; t += 3)
        {
            var turned = Enumerable.Range(0, 3).Any(r =>
                actual[t + r] == expected[t] && actual[t + (r + 1) % 3] == expected[t + 1]
                                             && actual[t + (r + 2) % 3] == expected[t + 2]);
            Assert.That(turned, $"triangle {t / 3}: {actual[t]} {actual[t + 1]} {actual[t + 2]}, " +
                                $"{expected[t]} {expected[t + 1]} {expected[t + 2]} given");
        }
    }

    private static void AssertSameCorners(Mesh mesh, Mesh original, float tolerance)
    {
        Assert.That(mesh.Points.Length, Is.EqualTo(original.Points.Length));
        Assert.That(mesh.Indices.Length, Is.EqualTo(original.Indices.Length));
        var givenTriangles = new Dictionary<string, Queue<int[]>>();
        for (int t = 0; t < original.Indices.Length; t += 3)
        {
            var corners = Corners(original, t);
            var key = Key(original, corners);
            if (!givenTriangles.TryGetValue(key, out var same))
            {
                givenTriangles[key] = same = new Queue<int[]>();
            }

            same.Enqueue(corners);
        }

        for (int t = 0; t < mesh.Indices.Length; t += 3)
        {
            var corners = Corners(mesh, t);
            Assert.That(givenTriangles.TryGetValue(Key(mesh, corners), out var same) && same.Count > 0,
                $"triangle {t / 3} is not in the original");
            var match = same.Dequeue();
            for (int j = 0; j < 3; j++)
            {
                var corner = corners[j];
                var given = match[j];
                var what = $"triangle {t / 3} corner {j}";
                var normal = mesh.Normals[corner];
                var givenNormal = original.Normals[given];
                AssertClose([normal.X, normal.Y, normal.Z], [givenNormal.X, givenNormal.Y, givenNormal.Z], tolerance, what);
                Assert.That(mesh.JointIndices[corner], Is.EqualTo(original.JointIndices[given]), what);
                Assert.That(mesh.JointWeights[corner], Is.EqualTo(original.JointWeights[given]), what);
            }
        }
    }

    private static float[] SignedAs(float[] quaternions, float[] expected)
    {
        var signed = quaternions.ToArray();
        for (int at = 0; at + 4 <= signed.Length && at + 4 <= expected.Length; at += 4)
        {
            var dot = 0f;
            for (int c = 0; c < 4; c++)
            {
                dot += signed[at + c] * expected[at + c];
            }

            if (dot < 0)
            {
                for (int c = 0; c < 4; c++)
                {
                    signed[at + c] = -signed[at + c];
                }
            }
        }

        return signed;
    }

    private static int[] Corners(Mesh mesh, int first)
    {
        var turn = Enumerable.Range(0, 3)
            .MinBy(r => mesh.Points[mesh.Indices[first + r]].ToString(), StringComparer.Ordinal);
        return [.. Enumerable.Range(0, 3).Select(j => mesh.Indices[first + (turn + j) % 3])];
    }

    private static string Key(Mesh mesh, int[] corners) => string.Join(" | ", corners.Select(c => mesh.Points[c]));

    private static int[] Shorts(byte[] bytes) =>
        [.. Enumerable.Range(0, bytes.Length / 2).Select(i => (int)BitConverter.ToUInt16(bytes, i * 2))];

    private static void AssertClose(float[] actual, float[] expected, float tolerance, string what)
    {
        Assert.That(actual.Length, Is.EqualTo(expected.Length), what);
        for (int i = 0; i < actual.Length; i++)
        {
            Assert.That(actual[i], Is.EqualTo(expected[i]).Within(tolerance), $"{what} [{i}]");
        }
    }

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
}
