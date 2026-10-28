using System;
using System.IO;
using System.Linq;
using System.Text.Json.Nodes;
using Adamantium.Engine.Compiler.Models;
using Adamantium.Engine.Compiler.Models.Converters;
using Adamantium.Graphics.Core;
using Adamantium.Graphics.Core.Models;
using Adamantium.Mathematics;
using NUnit.Framework;

namespace Adamantium.Engine.CompilerTests;

/// <summary>glTF 2.0 import over assets put together right here, each about one thing the format can say. A place is
/// checked in the engine's axes: what the file says, moved by the axis change.</summary>
[TestFixture]
public class GltfImportTests
{
    private const int Float = 5126;
    private const int UnsignedByte = 5121;
    private const int UnsignedShort = 5123;
    private static readonly Matrix4x4F ToEngine = AxisConversion.Between(UpAxis.Y_UP_RH_FRONT_Z, UpAxis.Y_DOWN_RH);
    private string folder;

    [SetUp]
    public void SetUp()
    {
        folder = Path.Combine(Path.GetTempPath(), "adamantium-gltf-" + Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(folder);
    }

    [TearDown]
    public void TearDown()
    {
        if (Directory.Exists(folder)) Directory.Delete(folder, true);
    }

    [TestCase("gltf")]
    [TestCase("glb")]
    [TestCase("bin")]
    public void ATriangle_IsImported_WhereverItsBufferIs(string kind)
    {
        var asset = Triangle();
        var path = kind switch
        {
            "glb" => asset.WriteGlb(folder),
            "bin" => asset.WriteGltfWithBin(folder),
            _ => asset.WriteGltf(folder)
        };

        var mesh = SingleMesh(Import(path));

        Assert.Multiple(() =>
        {
            Assert.That(mesh.Points.Length, Is.EqualTo(3));
            Assert.That(mesh.Indices.Length, Is.EqualTo(3));
            Assert.That(mesh.Points.Select(p => p.As<float>()), Is.EquivalentTo(new[]
            {
                InEngine(0, 0, 0), InEngine(1, 0, 0), InEngine(0, 1, 0)
            }).Using<Vector3F>((a, b) => (a - b).Length() < 1e-4f ? 0 : 1));
        });
    }

    // glTF's front is counter-clockwise, the engine's clockwise: the triangle's front, seen as the engine takes it, is
    // the side the file meant - and its derived normal points out of it.
    [Test]
    public void ATriangle_KeepsTheFrontTheFileMeant()
    {
        var mesh = SingleMesh(Import(Triangle().WriteGltf(folder)));

        var a = mesh.Points[mesh.Indices[0]].As<float>();
        var b = mesh.Points[mesh.Indices[1]].As<float>();
        var c = mesh.Points[mesh.Indices[2]].As<float>();
        var engineFront = Vector3F.Normalize(Vector3F.Cross(c - a, b - a));
        var fileFront = Vector3F.Normalize(Vector3F.TransformNormal(Vector3F.UnitZ, ToEngine));

        Assert.Multiple(() =>
        {
            Assert.That(Vector3F.Dot(engineFront, fileFront), Is.GreaterThan(0.99f), "the front the file meant");
            Assert.That(Vector3F.Dot(Vector3F.Normalize(mesh.Normals[0]), fileFront), Is.GreaterThan(0.99f), "its normal");
        });
    }

    // A glTF model's front faces +Z; in the engine it faces as the engine's models do, -Z.
    [Test]
    public void AModel_FacesAsTheEnginesModelsFace()
    {
        var mesh = SingleMesh(Import(Triangle().WriteGltf(folder)));

        var a = mesh.Points[mesh.Indices[0]].As<float>();
        var b = mesh.Points[mesh.Indices[1]].As<float>();
        var c = mesh.Points[mesh.Indices[2]].As<float>();
        var front = Vector3F.Normalize(Vector3F.Cross(c - a, b - a));

        Assert.That(Vector3F.Dot(front, Matrix4x4F.Identity.Forward), Is.GreaterThan(0.99f));
    }

    [Test]
    public void InterleavedVertices_AreReadByTheirStride()
    {
        var asset = new GltfTestAsset();
        // position (3 floats) then normal (3 floats), one vertex after another
        var interleaved = new float[] { 0, 0, 0, 0, 0, 1, 2, 0, 0, 0, 0, 1, 0, 3, 0, 0, 0, 1 };
        var bytes = new byte[interleaved.Length * 4];
        Buffer.BlockCopy(interleaved, 0, bytes, 0, bytes.Length);
        var view = asset.View(bytes, stride: 24);
        var positions = asset.Accessor(view, Float, 3, "VEC3");
        var normals = asset.Accessor(view, Float, 3, "VEC3", byteOffset: 12);
        asset.Scene(asset.Node(new JsonObject { ["mesh"] = asset.Mesh(GltfTestAsset.Primitive(positions, attributes: ("NORMAL", normals))) }));

        var mesh = SingleMesh(Import(asset.WriteGltf(folder)));

        Assert.Multiple(() =>
        {
            Assert.That(mesh.Points.Select(p => p.As<float>()), Does.Contain(InEngine(2, 0, 0)).Using<Vector3F>(Near));
            Assert.That(mesh.Points.Select(p => p.As<float>()), Does.Contain(InEngine(0, 3, 0)).Using<Vector3F>(Near));
            Assert.That(mesh.Normals.Select(n => Vector3F.Normalize(n)),
                Is.All.EqualTo(Vector3F.Normalize(Vector3F.TransformNormal(Vector3F.UnitZ, ToEngine))).Using<Vector3F>(Near));
        });
    }

    [Test]
    public void ASparseAccessor_LaysItsValuesOverItsBase()
    {
        var asset = new GltfTestAsset();
        var basePositions = asset.Floats("VEC3", 0, 0, 0, 1, 0, 0, 0, 1, 0);
        var sparseIndices = asset.View(BitConverter.GetBytes((ushort)2).Concat(new byte[2]).ToArray());
        var sparseValues = asset.View(new[] { 0f, 5f, 0f }.SelectMany(BitConverter.GetBytes).ToArray());
        var baseView = (int)asset.Json["accessors"]![basePositions]!["bufferView"]!;
        var positions = asset.Accessor(baseView, Float, 3, "VEC3", sparse: new JsonObject
        {
            ["count"] = 1,
            ["indices"] = new JsonObject { ["bufferView"] = sparseIndices, ["componentType"] = UnsignedShort },
            ["values"] = new JsonObject { ["bufferView"] = sparseValues }
        });
        asset.Scene(asset.Node(new JsonObject { ["mesh"] = asset.Mesh(GltfTestAsset.Primitive(positions)) }));

        var mesh = SingleMesh(Import(asset.WriteGltf(folder)));

        Assert.That(mesh.Points.Select(p => p.As<float>()), Does.Contain(InEngine(0, 5, 0)).Using<Vector3F>(Near));
    }

    [Test]
    public void NormalizedTextureCoordinates_AreReadAsFractions()
    {
        var asset = new GltfTestAsset();
        var positions = asset.Floats("VEC3", 0, 0, 0, 1, 0, 0, 0, 1, 0);
        var uvs = asset.Accessor(asset.View([0, 0, 255, 0, 0, 255, 0, 0]), UnsignedByte, 3, "VEC2", normalized: true);
        asset.Scene(asset.Node(new JsonObject { ["mesh"] = asset.Mesh(GltfTestAsset.Primitive(positions, attributes: ("TEXCOORD_0", uvs))) }));

        var mesh = SingleMesh(Import(asset.WriteGltf(folder)));

        Assert.That(mesh.UV0, Is.EquivalentTo(new[] { new Vector2F(0, 0), new Vector2F(1, 0), new Vector2F(0, 1) }));
    }

    // A strip zigzags across the quad, a fan goes round it from its first corner.
    [TestCase(5, 2, new float[] { 0, 0, 0, 1, 0, 0, 0, 1, 0, 1, 1, 0 })]
    [TestCase(6, 2, new float[] { 0, 0, 0, 1, 0, 0, 1, 1, 0, 0, 1, 0 })]
    public void AStripOrAFan_BecomesAListOfTriangles(int mode, int triangles, float[] corners)
    {
        var asset = new GltfTestAsset();
        var positions = asset.Floats("VEC3", corners);
        asset.Scene(asset.Node(new JsonObject { ["mesh"] = asset.Mesh(GltfTestAsset.Primitive(positions, mode: mode)) }));

        var mesh = SingleMesh(Import(asset.WriteGltf(folder)));

        Assert.Multiple(() =>
        {
            Assert.That(mesh.MeshTopology, Is.EqualTo(PrimitiveType.TriangleList));
            Assert.That(mesh.Indices.Length, Is.EqualTo(triangles * 3));
            var fileFront = Vector3F.TransformNormal(Vector3F.UnitZ, ToEngine);
            for (int i = 0; i < mesh.Indices.Length; i += 3)
            {
                var a = mesh.Points[mesh.Indices[i]].As<float>();
                var front = Vector3F.Cross(mesh.Points[mesh.Indices[i + 2]].As<float>() - a, mesh.Points[mesh.Indices[i + 1]].As<float>() - a);
                Assert.That(Vector3F.Dot(front, fileFront), Is.GreaterThan(0), $"triangle {i / 3} faces as the file meant");
            }
        });
    }

    [Test]
    public void NodesKeepTheirTreeNamesAndPlaces()
    {
        var asset = new GltfTestAsset();
        var positions = asset.Floats("VEC3", 0, 0, 0, 1, 0, 0, 0, 1, 0);
        var mesh = asset.Mesh(GltfTestAsset.Primitive(positions));
        var child = asset.Node(new JsonObject
        {
            ["name"] = "Wheel",
            ["mesh"] = mesh,
            ["translation"] = new JsonArray(1f, 2f, 3f)
        });
        var parent = asset.Node(new JsonObject
        {
            ["name"] = "Car",
            ["children"] = new JsonArray(child),
            ["matrix"] = new JsonArray(2f, 0f, 0f, 0f, 0f, 2f, 0f, 0f, 0f, 0f, 2f, 0f, 5f, 0f, 0f, 1f)
        });
        asset.Scene(parent);

        var scene = Import(asset.WriteGltf(folder));

        var car = scene.Models.Dependencies.Single();
        var wheel = car.Dependencies.Single();
        Assert.Multiple(() =>
        {
            Assert.That(scene.Models.Name, Is.EqualTo("asset"));
            Assert.That(car.Name, Is.EqualTo("Car"));
            Assert.That(wheel.Name, Is.EqualTo("Wheel"));
            Assert.That(car.Scale.X, Is.EqualTo(2).Within(1e-4), "the matrix's scale");
            Assert.That(car.Position, Is.EqualTo(InEngine(5, 0, 0)).Using<Vector3F>(Near), "the matrix's translation");
            Assert.That(wheel.Position, Is.EqualTo(InEngine(1, 2, 3)).Using<Vector3F>(Near));
            Assert.That(wheel.Meshes, Has.Count.EqualTo(1));
        });
    }

    [Test]
    public void AMaterial_KeepsEveryPbrValue()
    {
        var asset = Triangle(material: 0);
        asset.Array("images").Add(new JsonObject { ["uri"] = "base color.png" });
        asset.Array("images").Add(new JsonObject { ["uri"] = "normal.png" });
        asset.Array("textures").Add(new JsonObject { ["source"] = 0 });
        asset.Array("textures").Add(new JsonObject { ["source"] = 1 });
        asset.Array("materials").Add(new JsonObject
        {
            ["pbrMetallicRoughness"] = new JsonObject
            {
                ["baseColorFactor"] = new JsonArray(0.5f, 0.25f, 1f, 0.75f),
                ["baseColorTexture"] = new JsonObject { ["index"] = 0 },
                ["metallicFactor"] = 0.3f,
                ["roughnessFactor"] = 0.6f
            },
            ["normalTexture"] = new JsonObject { ["index"] = 1, ["scale"] = 0.8f },
            ["emissiveFactor"] = new JsonArray(1f, 0.5f, 0f),
            ["alphaMode"] = "BLEND",
            ["doubleSided"] = true
        });
        File.WriteAllBytes(Path.Combine(folder, "base color.png"), [1]);
        File.WriteAllBytes(Path.Combine(folder, "normal.png"), [1]);

        var scene = Import(asset.WriteGltf(folder));

        var mesh = SingleMesh(scene);
        var material = scene.Materials[mesh.MaterialID];
        Assert.Multiple(() =>
        {
            Assert.That(material.DiffuseColor, Is.EqualTo(new Vector4F(0.5f, 0.25f, 1f, 0.75f)));
            Assert.That(scene.Images[material.DiffuseMap].ImageName, Is.EqualTo("base color.png"));
            Assert.That(scene.Images[material.DiffuseMap].FilePath, Is.EqualTo(Path.Combine(folder, "base color.png")));
            Assert.That(scene.Images[material.NormalMap].ImageName, Is.EqualTo("normal.png"));
            Assert.That(material.NormalScale, Is.EqualTo(0.8f));
            Assert.That(material.MetallicFactor, Is.EqualTo(0.3f));
            Assert.That(material.RoughnessFactor, Is.EqualTo(0.6f));
            Assert.That(material.Emission, Is.EqualTo(new Vector4F(1, 0.5f, 0, 1)));
            Assert.That(material.AlphaMode, Is.EqualTo(AlphaMode.Blend));
            Assert.That(material.Transparency, Is.EqualTo(0.75f));
            Assert.That(material.DoubleSided, Is.True);
        });
    }

    [Test]
    public void AnImageInsideAGlb_IsCarriedAsBytes()
    {
        var asset = Triangle(material: 0);
        var picture = new byte[] { 0x89, 0x50, 0x4E, 0x47, 1, 2, 3 };
        var view = asset.View(picture);
        asset.Array("images").Add(new JsonObject { ["bufferView"] = view, ["mimeType"] = "image/png" });
        asset.Array("textures").Add(new JsonObject { ["source"] = 0 });
        asset.Array("materials").Add(new JsonObject
        {
            ["pbrMetallicRoughness"] = new JsonObject { ["baseColorTexture"] = new JsonObject { ["index"] = 0 } }
        });

        var scene = Import(asset.WriteGlb(folder));

        var image = scene.Images[scene.Materials[SingleMesh(scene).MaterialID].DiffuseMap];
        Assert.Multiple(() =>
        {
            Assert.That(image.Data, Is.EqualTo(picture));
            Assert.That(image.MimeType, Is.EqualTo("image/png"));
            Assert.That(image.FilePath, Is.Null);
        });
    }

    [Test]
    public void ARequiredExtensionNotKnown_StopsTheImport_AndOneUsedIsTold()
    {
        var asset = Triangle();
        asset.Json["extensionsUsed"] = new JsonArray("EXT_unknown_thing");
        var told = new GltfConverter(asset.WriteGltf(folder, "used.gltf"), new Compiler.Converter.Configs.ConversionConfig(true));
        told.StartConversion();

        asset.Json["extensionsRequired"] = new JsonArray("EXT_unknown_thing");
        var stopped = asset.WriteGltf(folder, "required.gltf");

        Assert.Multiple(() =>
        {
            Assert.That(told.UnsupportedFeatures, Has.Some.Contains("EXT_unknown_thing"));
            Assert.That(() => new ModelConverter().ImportFile(stopped), Throws.TypeOf<NotSupportedException>()
                .With.Message.Contains("EXT_unknown_thing"));
        });
    }

    [Test]
    public void AnIndexPastTheVertices_IsAnError_NotACrashLater()
    {
        var asset = new GltfTestAsset();
        var positions = asset.Floats("VEC3", 0, 0, 0, 1, 0, 0, 0, 1, 0);
        var indices = asset.UShorts(0, 1, 7);
        asset.Scene(asset.Node(new JsonObject { ["mesh"] = asset.Mesh(GltfTestAsset.Primitive(positions, indices)) }));

        Assert.That(() => new ModelConverter().ImportFile(asset.WriteGltf(folder)),
            Throws.TypeOf<InvalidDataException>().With.Message.Contains("index 7"));
    }

    [Test]
    public void ASkin_IsItsJointNodes_TheirInverseBinds_AndEachVertexsJointsAndWeights()
    {
        var asset = SkinnedTriangle(out var meshNode, out var hip, out var knee);

        var scene = Import(asset.WriteGltf(folder));

        var skin = scene.Controllers[GltfConverter.NodeId(meshNode)];
        var mesh = SingleMesh(scene);
        var inverseBindOfKnee = AxisConversion.Convert(Matrix4x4.CreateTranslation<float>(0, -1, 0), ToEngine);
        Assert.Multiple(() =>
        {
            Assert.That(skin.JointsAreNodes, Is.True);
            Assert.That(skin.JointNames, Is.EqualTo(new[] { GltfConverter.NodeId(hip), GltfConverter.NodeId(knee) }));
            Assert.That(skin.JointMatrices[0], Is.EqualTo(Matrix4x4F.Identity));
            Assert.That(skin.JointMatrices[1], Is.EqualTo(inverseBindOfKnee).Using<Matrix4x4F>(NearMatrix));
            Assert.That(mesh.JointIndices, Has.All.EqualTo(new Vector4F(0, 1, 0, 0)));
            Assert.That(mesh.JointWeights, Has.All.EqualTo(new Vector4F(0.75f, 0.25f, 0, 0)));
        });
    }

    [Test]
    public void EveryClip_IsKept_ByName_EachNodesChannelsOnOneTimeline()
    {
        var asset = Triangle();
        var turned = asset.Node(new JsonObject { ["name"] = "Wheel" });
        asset.Json["scenes"]![0]!["nodes"]!.AsArray().Add(turned);
        var times = asset.Floats("SCALAR", 0, 1);
        var halfTimes = asset.Floats("SCALAR", 0.5f);
        var turns = asset.Floats("VEC4", 0, 0, 0, 1, 0, 0, 1, 0);
        var moves = asset.Floats("VEC3", 0, 2, 0);
        asset.Array("animations").Add(Animation("Spin", turned,
            ("rotation", times, turns, "LINEAR"), ("translation", halfTimes, moves, "STEP")));
        asset.Array("animations").Add(Animation("Spin", turned, ("rotation", times, turns, "STEP")));

        var scene = Import(asset.WriteGltf(folder));

        var spin = scene.Clips["Spin"][GltfConverter.NodeId(turned)];
        var stepped = scene.Clips["Spin 2"][GltfConverter.NodeId(turned)];
        Assert.Multiple(() =>
        {
            Assert.That(scene.Clips.Keys, Is.EquivalentTo(new[] { "Spin", "Spin 2" }), "both clips, the second renamed");
            Assert.That(spin.Select(k => k.TimeStamp), Is.EqualTo(new[] { 0.0, 0.5, 1.0 }), "a key wherever a channel has one");
            Assert.That(spin[1].Position, Is.EqualTo(InEngine(0, 2, 0)).Using<Vector3F>(Near), "the translation held from 0.5");
            Assert.That(spin[0].Position, Is.EqualTo(InEngine(0, 2, 0)).Using<Vector3F>(Near), "its only key, held before it");
            Assert.That(spin[0].Interpolation, Is.EqualTo(InterpolationType.Linear), "a blended channel blends the node");
            Assert.That(stepped[0].Interpolation, Is.EqualTo(InterpolationType.Step));
        });
    }

    [Test]
    public void ACubicSplineChannel_IsSampledThirtyTimesASecond_ThroughItsKeys()
    {
        var asset = Triangle();
        var moved = asset.Node(new JsonObject());
        asset.Json["scenes"]![0]!["nodes"]!.AsArray().Add(moved);
        var times = asset.Floats("SCALAR", 0, 1);
        // in-tangent, value, out-tangent per key
        var values = asset.Floats("VEC3", 0, 0, 0, 0, 0, 0, 3, 0, 0, 0, 0, 0, 1, 0, 0, 0, 0, 0);
        asset.Array("animations").Add(Animation("Ease", moved, ("translation", times, values, "CUBICSPLINE")));

        var frames = Import(asset.WriteGltf(folder)).Clips["Ease"][GltfConverter.NodeId(moved)];

        Assert.Multiple(() =>
        {
            Assert.That(frames.Count, Is.InRange(30, 32));
            Assert.That(frames[^1].Position, Is.EqualTo(InEngine(1, 0, 0)).Using<Vector3F>(Near), "through the last key");
            var middle = frames.MinBy(k => Math.Abs(k.TimeStamp - 0.5));
            Assert.That(middle.Position.Length(), Is.GreaterThan(0.5f), "pushed past halfway by the out-tangent");
        });
    }

    // Keys blend straight to the next, so the stepped translation needs a key of its own just before it steps.
    [Test]
    public void ASteppedChannel_HoldsUntilItsStep_BesideABlendedOne()
    {
        var asset = Triangle();
        var moved = asset.Node(new JsonObject());
        asset.Json["scenes"]![0]!["nodes"]!.AsArray().Add(moved);
        var times = asset.Floats("SCALAR", 0, 1);
        var moves = asset.Floats("VEC3", 0, 0, 0, 10, 0, 0);
        var turns = asset.Floats("VEC4", 0, 0, 0, 1, 0, 0, 1, 0);
        asset.Array("animations").Add(Animation("Hop", moved, ("translation", times, moves, "STEP"), ("rotation", times, turns, "LINEAR")));

        var frames = Import(asset.WriteGltf(folder)).Clips["Hop"][GltfConverter.NodeId(moved)];

        Assert.Multiple(() =>
        {
            Assert.That(frames.Select(k => k.Interpolation), Has.All.EqualTo(InterpolationType.Linear));
            Assert.That(frames[^2].TimeStamp, Is.LessThan(1).And.GreaterThan(0.999));
            Assert.That(frames[^2].Position, Is.EqualTo(InEngine(0, 0, 0)).Using<Vector3F>(Near), "held up to the step");
            Assert.That(frames[^1].Position, Is.EqualTo(InEngine(10, 0, 0)).Using<Vector3F>(Near), "stepped at it");
        });
    }

    // Hermite tangents are per second: over a two-second gap the out-tangent pushes twice as far as over one.
    [Test]
    public void ACubicSplineTangent_IsScaledByTheGapBetweenItsKeys()
    {
        var asset = Triangle();
        var moved = asset.Node(new JsonObject());
        asset.Json["scenes"]![0]!["nodes"]!.AsArray().Add(moved);
        var times = asset.Floats("SCALAR", 0, 2);
        var values = asset.Floats("VEC3", 0, 0, 0, 0, 0, 0, 1, 0, 0, 0, 0, 0, 0, 0, 0, 0, 0, 0);
        asset.Array("animations").Add(Animation("Ease", moved, ("translation", times, values, "CUBICSPLINE")));

        var frames = Import(asset.WriteGltf(folder)).Clips["Ease"][GltfConverter.NodeId(moved)];

        var middle = frames.Single(k => Math.Abs(k.TimeStamp - 1) < 1e-6);
        Assert.That(middle.Position, Is.EqualTo(InEngine(0.25f, 0, 0)).Using<Vector3F>(Near), "h10(0.5) x gap x tangent");
    }

    // Past 2^20 seconds a thirtieth of a second no longer moves a float on, which once spun the sampling forever.
    [Test]
    public void ACubicSplineFarAlongTheTimeline_IsSampled_AndTheImportEnds()
    {
        var asset = Triangle();
        var moved = asset.Node(new JsonObject());
        asset.Json["scenes"]![0]!["nodes"]!.AsArray().Add(moved);
        var times = asset.Floats("SCALAR", 2097152, 2097160);
        var values = asset.Floats("VEC3", 0, 0, 0, 0, 0, 0, 0, 0, 0, 0, 0, 0, 1, 0, 0, 0, 0, 0);
        asset.Array("animations").Add(Animation("Far", moved, ("translation", times, values, "CUBICSPLINE")));

        var frames = Import(asset.WriteGltf(folder)).Clips["Far"][GltfConverter.NodeId(moved)];

        Assert.Multiple(() =>
        {
            Assert.That(frames.Count, Is.InRange(3, 241));
            Assert.That(frames[^1].Position, Is.EqualTo(InEngine(1, 0, 0)).Using<Vector3F>(Near));
        });
    }

    // glTF's transform rotates, scales and offsets the coordinates about the image's top left: here a quarter turn, half
    // the size, half the width along.
    [Test]
    public void ATextureTransform_IsLaidOnTheCoordinatesTheBaseColorReads()
    {
        var asset = new GltfTestAsset();
        var positions = asset.Floats("VEC3", 0, 0, 0, 1, 0, 0, 0, 1, 0);
        var uvs = asset.Floats("VEC2", 0, 0, 1, 0, 0, 1);
        asset.Scene(asset.Node(new JsonObject
        {
            ["mesh"] = asset.Mesh(GltfTestAsset.Primitive(positions, material: 0, attributes: ("TEXCOORD_0", uvs)))
        }));
        asset.Json["extensionsUsed"] = new JsonArray("KHR_texture_transform");
        asset.Json["extensionsRequired"] = new JsonArray("KHR_texture_transform");
        asset.Array("images").Add(new JsonObject { ["uri"] = "picture.png" });
        asset.Array("textures").Add(new JsonObject { ["source"] = 0 });
        asset.Array("materials").Add(new JsonObject
        {
            ["pbrMetallicRoughness"] = new JsonObject
            {
                ["baseColorTexture"] = new JsonObject
                {
                    ["index"] = 0,
                    ["extensions"] = new JsonObject
                    {
                        ["KHR_texture_transform"] = new JsonObject
                        {
                            ["offset"] = new JsonArray(0.5f, 0f),
                            ["rotation"] = MathF.PI / 2,
                            ["scale"] = new JsonArray(0.5f, 0.5f)
                        }
                    }
                }
            }
        });
        File.WriteAllBytes(Path.Combine(folder, "picture.png"), [1]);

        var mesh = SingleMesh(Import(asset.WriteGltf(folder)));

        Assert.That(mesh.UV0, Is.EqualTo(new[] { new Vector2F(0.5f, 0), new Vector2F(0.5f, -0.5f), new Vector2F(1, 0) })
            .Using<Vector2F>((a, b) => (a - b).Length() < 1e-4f ? 0 : 1));
    }

    // A stack overflow cannot be caught: it would take the whole editor down with it.
    [Test]
    public void ANodeChainAsDeepAsAFileLikes_IsImported()
    {
        const int depth = 50_000;
        var asset = new GltfTestAsset();
        var nodes = asset.Array("nodes");
        for (int i = 0; i < depth; i++)
        {
            nodes.Add(i + 1 < depth ? new JsonObject { ["children"] = new JsonArray(i + 1) } : new JsonObject());
        }

        asset.Scene(0);

        var scene = Import(asset.WriteGltf(folder));

        var levels = 0;
        for (var model = scene.Models; model.Dependencies.Count > 0; model = model.Dependencies[0]) levels++;
        Assert.That(levels, Is.EqualTo(depth));
    }

    [Test]
    public void ANodeReachedAgain_IsTold_AndTheImportEnds()
    {
        var asset = new GltfTestAsset();
        asset.Array("nodes").Add(new JsonObject { ["children"] = new JsonArray(1) });
        asset.Array("nodes").Add(new JsonObject { ["children"] = new JsonArray(0) });
        asset.Scene(0);

        var converter = new GltfConverter(asset.WriteGltf(folder), new Compiler.Converter.Configs.ConversionConfig(true));
        converter.StartConversion();

        Assert.That(converter.UnsupportedFeatures, Has.Some.Contains("reached twice"));
    }

    [Test]
    public void ASkinOfManyJoints_KeepsThemAll_AndTellsNothing()
    {
        var asset = SkinnedTriangle(out var meshNode, out var hip, out _);
        var joints = asset.Json["skins"]![0]!["joints"]!.AsArray();
        asset.Json["skins"]![0]!.AsObject().Remove("inverseBindMatrices");
        while (joints.Count < 300)
        {
            var joint = asset.Node(new JsonObject());
            joints.Add(joint);
            asset.Json["nodes"]![hip]!["children"]!.AsArray().Add(joint);
        }

        var converter = new GltfConverter(asset.WriteGltf(folder), new Compiler.Converter.Configs.ConversionConfig(true));
        var scene = converter.StartConversion();

        Assert.Multiple(() =>
        {
            Assert.That(scene.Controllers[GltfConverter.NodeId(meshNode)].JointNames, Has.Count.EqualTo(300));
            Assert.That(converter.UnsupportedFeatures, Has.None.Contains("joints"));
        });
    }

    [Test]
    public void ACameraAndALight_AreKept_WhereTheirNodesStand()
    {
        var asset = Triangle();
        asset.Array("cameras").Add(new JsonObject
        {
            ["type"] = "perspective",
            ["perspective"] = new JsonObject { ["yfov"] = MathF.PI / 3, ["znear"] = 0.1f, ["zfar"] = 100f }
        });
        asset.Json["extensionsUsed"] = new JsonArray("KHR_lights_punctual");
        asset.Json["extensions"] = new JsonObject
        {
            ["KHR_lights_punctual"] = new JsonObject
            {
                ["lights"] = new JsonArray(new JsonObject
                {
                    ["type"] = "spot",
                    ["color"] = new JsonArray(1f, 0.5f, 0f),
                    ["intensity"] = 2f,
                    ["range"] = 7f,
                    ["spot"] = new JsonObject { ["innerConeAngle"] = MathF.PI / 6, ["outerConeAngle"] = MathF.PI / 4 }
                })
            }
        };
        var cameraNode = asset.Node(new JsonObject { ["camera"] = 0, ["translation"] = new JsonArray(0f, 0f, 5f) });
        var lightNode = asset.Node(new JsonObject
        {
            ["translation"] = new JsonArray(0f, 3f, 0f),
            ["extensions"] = new JsonObject { ["KHR_lights_punctual"] = new JsonObject { ["light"] = 0 } }
        });
        asset.Json["scenes"]![0]!["nodes"]!.AsArray().Add(cameraNode);
        asset.Json["scenes"]![0]!["nodes"]!.AsArray().Add(lightNode);

        var scene = Import(asset.WriteGltf(folder));

        var camera = scene.CameraData[GltfConverter.NodeId(cameraNode)];
        var light = scene.LightData[GltfConverter.NodeId(lightNode)];
        Assert.Multiple(() =>
        {
            Assert.That(camera.Fov, Is.EqualTo(60).Within(1e-3), "degrees, as Collada's");
            Assert.That(camera.FovType, Is.EqualTo(FovType.YFov));
            Assert.That(camera.Translation, Is.EqualTo(InEngine(0, 0, 5)).Using<Vector3F>(Near));
            Assert.That(Vector3F.Dot(Vector3F.TransformNormal(Vector3F.UnitZ, Matrix4x4.CreateFromQuaternion<float>(camera.Rotation)),
                Vector3F.Normalize(-camera.Translation)), Is.GreaterThan(0.99f), "an engine camera looks along its +Z: at the model, as in the file");
            Assert.That(light.LightType, Is.EqualTo(LightType.Spot));
            Assert.That(light.Color, Is.EqualTo(new Vector3F(1, 0.5f, 0)));
            Assert.That(light.Intensity, Is.EqualTo(2));
            Assert.That(light.Range, Is.EqualTo(7));
            Assert.That(light.FalloffAngle, Is.EqualTo(45).Within(1e-3));
            Assert.That(light.InnerConeAngle, Is.EqualTo(30).Within(1e-3));
            Assert.That(light.Position, Is.EqualTo(InEngine(0, 3, 0)).Using<Vector3F>(Near));
        });
    }

    /// <summary>A triangle bound to a hip at the origin and a knee one unit up, each vertex weighed 3:1 to them.</summary>
    internal static GltfTestAsset SkinnedTriangle(out int meshNode, out int hip, out int knee)
    {
        var asset = new GltfTestAsset();
        var positions = asset.Floats("VEC3", 0, 0, 0, 1, 0, 0, 0, 1, 0);
        var indices = asset.UShorts(0, 1, 2);
        var joints = asset.Accessor(asset.View([0, 1, 0, 0, 0, 1, 0, 0, 0, 1, 0, 0]), UnsignedByte, 3, "VEC4");
        var weights = asset.Floats("VEC4", 0.75f, 0.25f, 0, 0, 0.75f, 0.25f, 0, 0, 0.75f, 0.25f, 0, 0);
        var inverseBinds = asset.Floats("MAT4",
            1, 0, 0, 0, 0, 1, 0, 0, 0, 0, 1, 0, 0, 0, 0, 1,
            1, 0, 0, 0, 0, 1, 0, 0, 0, 0, 1, 0, 0, -1, 0, 1);
        var mesh = asset.Mesh(GltfTestAsset.Primitive(positions, indices, attributes: [("JOINTS_0", joints), ("WEIGHTS_0", weights)]));
        knee = asset.Node(new JsonObject { ["name"] = "Knee", ["translation"] = new JsonArray(0f, 1f, 0f) });
        hip = asset.Node(new JsonObject { ["name"] = "Hip", ["children"] = new JsonArray(knee) });
        asset.Array("skins").Add(new JsonObject { ["joints"] = new JsonArray(hip, knee), ["inverseBindMatrices"] = inverseBinds });
        meshNode = asset.Node(new JsonObject { ["name"] = "Leg", ["mesh"] = mesh, ["skin"] = 0 });
        asset.Scene(meshNode, hip);
        return asset;
    }

    private static JsonObject Animation(string name, int node, params (string Path, int Times, int Values, string Interpolation)[] channels)
    {
        var samplers = new JsonArray();
        var list = new JsonArray();
        foreach (var (path, times, values, interpolation) in channels)
        {
            list.Add(new JsonObject
            {
                ["sampler"] = samplers.Count,
                ["target"] = new JsonObject { ["node"] = node, ["path"] = path }
            });
            samplers.Add(new JsonObject { ["input"] = times, ["output"] = values, ["interpolation"] = interpolation });
        }

        return new JsonObject { ["name"] = name, ["channels"] = list, ["samplers"] = samplers };
    }

    private static int NearMatrix(Matrix4x4F a, Matrix4x4F b)
    {
        for (int i = 0; i < 16; i++)
        {
            if (Math.Abs(a[i] - b[i]) > 1e-4f) return 1;
        }

        return 0;
    }

    private static GltfTestAsset Triangle(int? material = null)
    {
        var asset = new GltfTestAsset();
        var positions = asset.Floats("VEC3", 0, 0, 0, 1, 0, 0, 0, 1, 0);
        var indices = asset.UShorts(0, 1, 2);
        asset.Scene(asset.Node(new JsonObject { ["mesh"] = asset.Mesh(GltfTestAsset.Primitive(positions, indices, material)) }));
        return asset;
    }

    private static SceneData Import(string path) => new ModelConverter().ImportFile(path);

    private static Mesh SingleMesh(SceneData scene)
    {
        var stack = new System.Collections.Generic.Stack<SceneData.Model>();
        stack.Push(scene.Models);
        Mesh found = null;
        while (stack.Count > 0)
        {
            var model = stack.Pop();
            foreach (var mesh in model.Meshes)
            {
                Assert.That(found, Is.Null, "one mesh only");
                found = mesh;
            }

            foreach (var child in model.Dependencies) stack.Push(child);
        }

        Assert.That(found, Is.Not.Null, "a mesh");
        return found;
    }

    private static Vector3F InEngine(float x, float y, float z) => Vector3F.TransformCoordinate(new Vector3F(x, y, z), ToEngine);

    private static int Near(Vector3F a, Vector3F b) => (a - b).Length() < 1e-4f ? 0 : 1;
}
