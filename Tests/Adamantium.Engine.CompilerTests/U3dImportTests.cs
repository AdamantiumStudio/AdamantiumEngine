using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using Adamantium.Engine.Compiler.Converter;
using Adamantium.Engine.Compiler.Converter.Configs;
using Adamantium.Engine.Compiler.Models.Converters;
using Adamantium.Engine.Compiler.Models.U3d;
using Adamantium.Graphics.Core;
using Adamantium.Graphics.Core.Models;
using Adamantium.Mathematics;
using NUnit.Framework;

namespace Adamantium.Engine.CompilerTests;

/// <summary>U3D import (ECMA-363) from files the tests write, compressed and in the no compression profile, and from
/// files MeshLab's exporter wrote. U3D is Z up: the engine's axes take (x, y, z) to (-x, -z, y).</summary>
[TestFixture]
public class U3dImportTests
{
    private const float Tolerance = 1e-4f;

    [Test]
    public void Cube_GivesPositionsNormalsFacesAndMaterials([Values(false, true)] bool compressed)
    {
        var scene = Import(CreateCubeFile(compressed), out var notes);

        var box = scene.Models.Dependencies.Single();
        string[] materials = ["Red", "Glass"];
        int[] triangleCounts = [8, 4];
        Assert.Multiple(() =>
        {
            Assert.That(notes, Is.Empty);
            Assert.That(box.Name, Is.EqualTo("Box"));
            Assert.That(box.Meshes.Select(mesh => mesh.MaterialID), Is.EqualTo(materials));
            Assert.That(box.Meshes.Select(mesh => mesh.Indices.Length / 3), Is.EqualTo(triangleCounts));
        });

        var corners = CreateCubePositions().Select(ToEngine).ToList();
        var axes = CreateCubeNormals().Select(ToEngine).ToList();
        foreach (var mesh in box.Meshes)
        {
            Assert.That(mesh.MeshTopology, Is.EqualTo(PrimitiveType.TriangleList));
            Assert.That(mesh.Normals, Has.Length.EqualTo(mesh.Points.Length));
            foreach (var point in mesh.Points)
            {
                Assert.That(corners.Any(corner => IsNear(corner, point.As<float>())), $"{point} is no corner of the cube");
            }

            for (var index = 0; index < mesh.Indices.Length; index += 3)
            {
                var first = mesh.Points[mesh.Indices[index]].As<float>();
                var face = Vector3F.Cross(mesh.Points[mesh.Indices[index + 1]].As<float>() - first,
                    mesh.Points[mesh.Indices[index + 2]].As<float>() - first);
                face.Normalize();
                for (var corner = 0; corner < 3; corner++)
                {
                    var normal = mesh.Normals[mesh.Indices[index + corner]];
                    Assert.That(axes.Any(axis => IsNear(axis, normal)), $"{normal} is no normal of the cube");
                    Assert.That(Math.Abs(Vector3F.Dot(face, normal)), Is.GreaterThan(0.99f), "the file's normal is the face's");
                }
            }
        }

        var red = scene.Materials["Red"];
        var glass = scene.Materials["Glass"];
        Assert.Multiple(() =>
        {
            Assert.That(red.DiffuseColor, Is.EqualTo(new Vector4F(1, 0, 0, 1)));
            Assert.That(red.SpecularColor, Is.EqualTo(new Vector4F(0.2f, 0.3f, 0.4f, 1)));
            Assert.That(red.AmbientColor, Is.EqualTo(new Vector4F(0.1f, 0.1f, 0.1f, 1)));
            Assert.That(red.Emission, Is.EqualTo(new Vector4F(0.05f, 0, 0, 1)));
            Assert.That(red.Shininess, Is.EqualTo(64));
            Assert.That(red.AlphaMode, Is.EqualTo(AlphaMode.Opaque));
            Assert.That(glass.DiffuseColor, Is.EqualTo(new Vector4F(0, 0, 1, 0.5f)));
            Assert.That(glass.Transparency, Is.EqualTo(0.5f));
            Assert.That(glass.AlphaMode, Is.EqualTo(AlphaMode.Blend));
        });
    }

    [Test]
    public void Compressed_IsSmallerThanPlain()
    {
        Assert.That(CreateCubeFile(true).Length, Is.LessThan(CreateCubeFile(false).Length));
    }

    [Test]
    public void Nodes_BecomeTheModelTreeWithTheirTransforms([Values(false, true)] bool compressed)
    {
        var file = new U3dTestFile(compressed);
        var metaData = U3dTestFile.CreateMetaData([
            new U3dMetaDataEntry { Key = "Author", Value = "Tests" },
            new U3dMetaDataEntry { Attributes = 0x1, Key = "Blob", BinaryValue = [1, 2, 3] }
        ]);
        U3dTestBlocks.WriteGroupNode(file, "Car", [CreateParent(string.Empty, new Vector3F(1, 2, 3))], metaData);
        file.DeclareBlock(U3dTestBlocks.CreateModelNode(file, "Wheel",
            [CreateParent("Car", new Vector3F(0, 0, 1)), CreateParent(string.Empty, new Vector3F(5, 0, 0))], "WheelMesh"));
        U3dTestBlocks.WriteGroupNode(file, "Lonely", []);
        U3dTestBlocks.WriteGroupNode(file, "LoopA", [CreateParent("LoopB", Vector3F.Zero)]);
        U3dTestBlocks.WriteGroupNode(file, "LoopB", [CreateParent("LoopA", Vector3F.Zero)]);
        U3dTestBlocks.WriteMesh(file, CreateTriangle("WheelMesh"));

        var scene = Import(file.ToArray(), out var notes);

        var root = scene.Models;
        var car = root.Dependencies.Single(model => model.Name == "Car");
        var underCar = car.Dependencies.Single();
        var underWorld = root.Dependencies.Single(model => model.Name == "Wheel");
        string[] rootIds = ["Car", "Wheel (2)"];
        string[] otherNodes = ["Lonely"];
        Assert.Multiple(() =>
        {
            Assert.That(root.Dependencies.Select(model => model.ID), Is.EqualTo(rootIds));
            Assert.That(underCar.ID, Is.EqualTo("Wheel"));
            Assert.That(IsNear(car.Position, new Vector3F(-1, -3, 2)), $"the car stands at {car.Position}");
            Assert.That(IsNear(underCar.Position, new Vector3F(0, -1, 0)), $"the wheel stands at {underCar.Position}");
            Assert.That(IsNear(underWorld.Position, new Vector3F(-5, 0, 0)), $"the other wheel stands at {underWorld.Position}");
            Assert.That(underCar.Meshes, Has.Count.EqualTo(1));
            Assert.That(underWorld.Meshes, Has.Count.EqualTo(1));
            Assert.That(underCar.Meshes[0], Is.Not.SameAs(underWorld.Meshes[0]), "each copy has meshes of its own");
            Assert.That(scene.OtherNodes.Dependencies.Select(model => model.Name), Is.EqualTo(otherNodes));
            Assert.That(car.Extra.Data, Does.Contain("\"Author\":\"Tests\"").And.Contain("\"Blob\":\"AQID\""));
            Assert.That(notes, Has.Some.Contains("LoopA").And.Contains("cycle"));
            Assert.That(notes, Has.Some.Contains("LoopB").And.Contains("cycle"));
        });
    }

    [Test]
    public void ANodeShownTwice_NeverTakesTheIdOfANodeNamedLikeItsCopy()
    {
        var file = new U3dTestFile(true);
        U3dTestBlocks.WriteGroupNode(file, "Wheel", [CreateParent(string.Empty, Vector3F.Zero), CreateParent(string.Empty, Vector3F.One)]);
        U3dTestBlocks.WriteGroupNode(file, "Wheel (2)", [CreateParent(string.Empty, Vector3F.Zero)]);

        var scene = Import(file.ToArray(), out _);

        var ids = scene.Models.Dependencies.Select(model => model.ID).ToList();
        Assert.That(ids, Is.Unique);
        Assert.That(ids, Has.Count.EqualTo(3));
    }

    [Test]
    public void PointSet_GivesItsPoints([Values(false, true)] bool compressed)
    {
        var random = new Random(7);
        var positions = Enumerable.Range(0, 40).Select(point => CreateRandomVector(random, 5)).ToList();
        var normals = Enumerable.Range(0, 40).Select(point => Vector3F.Normalize(CreateRandomVector(random, 1))).ToList();
        var colors = Enumerable.Range(0, 40)
            .Select(point => new Vector4F(random.NextSingle(), random.NextSingle(), random.NextSingle(), 1)).ToList();
        var file = new U3dTestFile(compressed);
        file.DeclareBlock(U3dTestBlocks.CreateModelNode(file, "Cloud", [CreateParent(string.Empty, Vector3F.Zero)], "Points"));
        var written = U3dTestBlocks.WritePointSet(file, "Points", positions, normals, colors, 0.001f);

        var scene = Import(file.ToArray(), out _);

        var mesh = scene.Models.Dependencies.Single().Meshes.Single();
        Assert.That(mesh.MeshTopology, Is.EqualTo(PrimitiveType.PointList));
        Assert.That(mesh.Colors, Has.Length.EqualTo(mesh.Points.Length));
        var points = mesh.Indices.Select(index => mesh.Points[index].As<float>()).ToList();
        Assert.That(points, Has.Count.EqualTo(positions.Count));
        for (var index = 0; index < positions.Count; index++)
        {
            Assert.That(IsNear(points[index], ToEngine(written[index])), $"point {index}: {points[index]}");
            Assert.That(IsNear(points[index], ToEngine(positions[index]), 0.002f), $"point {index} is where it was put");
        }
    }

    [Test]
    public void LineSet_GivesItsLines([Values(false, true)] bool compressed)
    {
        var positions = Enumerable.Range(0, 12).Select(index => new Vector3F(index, MathF.Sin(index), index * 0.5f)).ToList();
        var normals = positions.Select(position => new Vector3F(0, 0, 1)).ToList();
        var file = new U3dTestFile(compressed);
        file.DeclareBlock(U3dTestBlocks.CreateModelNode(file, "Polyline", [CreateParent(string.Empty, Vector3F.Zero)], "Lines"));
        var written = U3dTestBlocks.WriteLineSet(file, "Lines", positions, normals, 0.0005f);

        var scene = Import(file.ToArray(), out _);

        var mesh = scene.Models.Dependencies.Single().Meshes.Single();
        Assert.That(mesh.MeshTopology, Is.EqualTo(PrimitiveType.LineList));
        Assert.That(mesh.Indices, Has.Length.EqualTo((positions.Count - 1) * 2));
        for (var line = 0; line < positions.Count - 1; line++)
        {
            Assert.That(IsNear(mesh.Points[mesh.Indices[line * 2]].As<float>(), ToEngine(written[line])), $"line {line} start");
            Assert.That(IsNear(mesh.Points[mesh.Indices[line * 2 + 1]].As<float>(), ToEngine(written[line + 1])), $"line {line} end");
        }
    }

    [Test]
    public void LightAndView_BecomeALightAndACamera([Values(false, true)] bool compressed)
    {
        var file = new U3dTestFile(compressed);
        U3dTestBlocks.WriteLight(file, "Sun", 3, new Vector3F(1, 0.5f, 0.25f), 30, 2);
        U3dTestBlocks.WriteView(file, "Look");
        U3dTestBlocks.WriteLightNode(file, "Lamp", [CreateParent(string.Empty, new Vector3F(0, 0, 5))], "Sun");
        U3dTestBlocks.WriteViewNode(file, "Camera", [CreateParent(string.Empty, new Vector3F(0, -10, 0))], "Look", 45, 0.5f, 500, 640, 480);

        var scene = Import(file.ToArray(), out var notes);

        var light = scene.LightData["Lamp"];
        var camera = scene.CameraData["Camera"];
        Assert.Multiple(() =>
        {
            Assert.That(notes, Is.Empty);
            Assert.That(light.LightType, Is.EqualTo(LightType.Spot));
            Assert.That(light.Color, Is.EqualTo(new Vector3F(1, 0.5f, 0.25f)));
            Assert.That(light.FalloffAngle, Is.EqualTo(30));
            Assert.That(light.Intensity, Is.EqualTo(2));
            Assert.That(light.ConstantAttenuation, Is.EqualTo(1));
            Assert.That(light.LinearAttenuation, Is.EqualTo(0.5f));
            Assert.That(light.QuadraticAttenuation, Is.EqualTo(0.25f));
            Assert.That(IsNear(light.Position, new Vector3F(0, -5, 0)), $"the light stands at {light.Position}");
            Assert.That(camera.ProjectionType, Is.EqualTo(CameraProjectionType.Perspective));
            Assert.That(camera.Fov, Is.EqualTo(45));
            Assert.That(camera.ZNear, Is.EqualTo(0.5f));
            Assert.That(camera.ZFar, Is.EqualTo(500));
            Assert.That(camera.AspectRatio, Is.EqualTo(640f / 480f));
            Assert.That(IsNear(camera.Translation, new Vector3F(0, 0, -10)), $"the camera stands at {camera.Translation}");
        });
    }

    [Test]
    public void Texture_ItsPngBecomesTheDiffuseImage([Values(false, true)] bool compressed)
    {
        byte[] png = [0x89, 0x50, 0x4E, 0x47, 0x0D, 0x0A, 0x1A, 0x0A, 1, 2, 3, 4, 5, 6, 7];
        var mesh = CreateTriangle("Plane");
        mesh.TextureCoordinates.AddRange([new Vector4F(0, 0, 0, 0), new Vector4F(1, 0, 0, 0), new Vector4F(0, 0.25f, 0, 0)]);
        mesh.ShadingLayers[0] = 1;
        mesh.Faces[0] = new U3dTestFace
        {
            ShadingId = 0, Positions = [0, 1, 2], Normals = [0, 0, 0], TextureCoordinates = [0, 1, 2]
        };
        var file = new U3dTestFile(compressed);
        file.DeclareBlock(U3dTestFile.CreateModifierChain("Floor", 0,
            U3dTestBlocks.CreateModelNode(file, "Floor", [CreateParent(string.Empty, Vector3F.Zero)], "Plane"),
            U3dTestBlocks.CreateShadingModifier(file, "Floor", "Textured")));
        U3dTestBlocks.WriteMaterial(file, "Paint", new Vector3F(1, 1, 1), 1, 0);
        U3dTestBlocks.WriteShader(file, "Textured", "Paint", "Checker");
        U3dTestBlocks.WriteTexture(file, "Checker", png);
        U3dTestBlocks.WriteMesh(file, mesh);

        var scene = Import(file.ToArray(), out var notes);

        var drawn = scene.Models.Dependencies.Single().Meshes.Single();
        float[] vs = [0f, 0f, 0.25f];
        Assert.Multiple(() =>
        {
            Assert.That(notes, Is.Empty);
            Assert.That(scene.Images["Checker"].Data, Is.EqualTo(png));
            Assert.That(scene.Images["Checker"].MimeType, Is.EqualTo("image/png"));
            Assert.That(scene.Materials["Textured"].DiffuseMap, Is.EqualTo("Checker"));
            Assert.That(drawn.UV0.Select(uv => uv.Y).OrderBy(value => value), Is.EqualTo(vs),
                "v turned over on reading is turned back by the axis change");
        });
    }

    [Test]
    public void HeaderUnits_BecomeTheScenesUnits()
    {
        var file = new U3dTestFile(true, 0.001);
        U3dTestBlocks.WriteGroupNode(file, "Part", [CreateParent(string.Empty, Vector3F.Zero)]);

        var scene = Import(file.ToArray(), out _);

        Assert.That(scene.Units.UnitType, Is.EqualTo(UnitType.Meter));
        Assert.That(scene.Units.Value, Is.EqualTo(0.001f));
    }

    [Test]
    public void Blocks_ArePaddedToFourBytesAndCarryTheirMetaData()
    {
        var metaData = U3dTestFile.CreateMetaData([
            new U3dMetaDataEntry { Key = "Name", Value = "Value" },
            new U3dMetaDataEntry { Attributes = 0x3, Key = "Hidden", BinaryValue = [9, 8, 7, 6, 5] }
        ]);
        List<byte> bytes = [];
        for (var size = 1; size <= 5; size++)
        {
            bytes.AddRange(U3dTestFile.CreateBlock(0x100u + (uint)size, Enumerable.Repeat((byte)size, size).ToArray(),
                size == 3 ? metaData : null));
        }

        var blocks = U3dBlockReader.ReadBlocks(bytes.ToArray(), 0, bytes.Count);

        uint[] types = [0x101, 0x102, 0x103, 0x104, 0x105];
        int[] sizes = [1, 2, 3, 4, 5];
        byte[] contents = [1, 2, 3, 4, 5];
        string[] keys = ["Name", "Hidden"];
        byte[] binary = [9, 8, 7, 6, 5];
        Assert.Multiple(() =>
        {
            Assert.That(blocks.Select(block => block.Type), Is.EqualTo(types));
            Assert.That(blocks.Select(block => block.DataSize), Is.EqualTo(sizes));
            Assert.That(blocks.Select(block => block.Offset % 4), Is.All.EqualTo(0));
            Assert.That(blocks.Select(block => block.Data.ToArray().Distinct().Single()), Is.EqualTo(contents));
            Assert.That(blocks[2].MetaData.Select(entry => entry.Key), Is.EqualTo(keys));
            Assert.That(blocks[2].MetaData[0].Value, Is.EqualTo("Value"));
            Assert.That(blocks[2].MetaData[1].IsBinary, Is.True);
            Assert.That(blocks[2].MetaData[1].BinaryValue, Is.EqualTo(binary));
            Assert.That(blocks[^1].End, Is.EqualTo(bytes.Count));
        });
    }

    [Test]
    public void UnknownAndUnreadBlocks_AreSkippedAndTold()
    {
        var file = new U3dTestFile(true);
        file.DeclareBlock(U3dTestFile.CreateBlock(0x00000777, [1, 2, 3, 4, 5, 6]));
        file.DeclareBlock(U3dTestFile.CreateBlock(U3dBlockTypes.MotionResource, [0, 0]));
        U3dTestBlocks.WriteGroupNode(file, "After", [CreateParent(string.Empty, Vector3F.Zero)]);

        var scene = Import(file.ToArray(), out var notes);

        string[] names = ["After"];
        Assert.Multiple(() =>
        {
            Assert.That(scene.Models.Dependencies.Select(model => model.Name), Is.EqualTo(names));
            Assert.That(notes, Has.Some.Contains("0x00000777"));
            Assert.That(notes, Has.Some.Contains("motion"));
        });
    }

    [Test, Timeout(120000)]
    public void CutShort_ThrowsAtEveryLength()
    {
        var bytes = CreateCubeFile(true);
        for (var length = 0; length < bytes.Length; length += 3)
        {
            Assert.That(() => Import(bytes[..length], out _), Throws.TypeOf<InvalidDataException>(), $"cut at {length}");
        }
    }

    [Test, Timeout(60000)]
    public void BlockDataCutShort_Throws([Values(false, true)] bool compressed)
    {
        var whole = new U3dTestFile(compressed);
        U3dTestBlocks.WriteMesh(whole, CreateCubeMesh());
        var bytes = whole.ToArray();
        var blocks = U3dBlockReader.ReadBlocks(bytes, 0, bytes.Length);
        var baseMesh = blocks.Single(block => block.Type == U3dBlockTypes.MeshBaseContinuation);
        var cut = new U3dTestFile(compressed);
        cut.DeclareBlock(U3dTestFile.CreateBlock(U3dBlockTypes.MeshDeclaration,
            blocks.Single(block => block.Type == U3dBlockTypes.MeshDeclaration).Data.ToArray()));
        cut.AddContinuationBlock(U3dTestFile.CreateBlock(U3dBlockTypes.MeshBaseContinuation, baseMesh.Data[..(baseMesh.DataSize * 2 / 3)].ToArray()));

        Assert.That(() => Import(cut.ToArray(), out _),
            Throws.TypeOf<InvalidDataException>().With.Message.Contains("0xFFFFFF3B"));
    }

    [Test, Timeout(60000)]
    public void CountPastTheData_Throws([Values(false, true)] bool compressed)
    {
        var file = new U3dTestFile(compressed);
        var mesh = CreateTriangle("Huge");
        U3dTestBlocks.WriteMesh(file, mesh);
        var bytes = file.ToArray();
        var blocks = U3dBlockReader.ReadBlocks(bytes, 0, bytes.Length);
        var baseMesh = blocks.Single(block => block.Type == U3dBlockTypes.MeshBaseContinuation);
        var faceCountAt = baseMesh.DataOffset + 2 + "Huge".Length + 4;
        BitConverter.GetBytes(0x7FFFFFFFu).CopyTo(bytes, faceCountAt);

        Assert.That(() => Import(bytes, out _), Throws.TypeOf<InvalidDataException>().With.Message.Contains("faces"));
    }

    [Test]
    public void IndexPastItsPool_Throws()
    {
        var mesh = CreateTriangle("Broken");
        mesh.Faces[0] = new U3dTestFace { ShadingId = 0, Positions = [0, 1, 7], Normals = [0, 0, 0] };
        var file = new U3dTestFile(false);
        U3dTestBlocks.WriteMesh(file, mesh);

        Assert.That(() => Import(file.ToArray(), out _),
            Throws.TypeOf<InvalidDataException>().With.Message.Contains("position 7"));
    }

    [Test]
    public void NotU3d_Throws()
    {
        Assert.That(() => Import("glTF and more"u8.ToArray(), out _),
            Throws.TypeOf<InvalidDataException>().With.Message.Contains("not a U3D file"));
    }

    [Test]
    public void Factory_ReadsAU3dFromBytes()
    {
        var bytes = CreateCubeFile(true);
        var converter = ModelConverterFactory.GetConverter("artwork.u3d", bytes, new ConversionConfig(true));

        Assert.That(converter, Is.TypeOf<U3dConverter>());
        Assert.That(converter.StartConversion().Models.Dependencies.Single().Name, Is.EqualTo("Box"));
        Assert.That(() => ModelConverterFactory.GetConverter("artwork.obj", bytes, new ConversionConfig(true)),
            Throws.TypeOf<NotSupportedException>());
    }

    [Test]
    public void MeshLabSphere_DecodesItsResolutionUpdatesOntoTheUnitSphere()
    {
        var bytes = File.ReadAllBytes(GetRealFilePath("sphere-colored.u3d"));
        AssertMeshLabLayout(bytes);
        var declared = ReadDeclared(bytes);
        Assert.Multiple(() =>
        {
            Assert.That(declared.Attributes, Is.EqualTo(1), "exported with normals excluded");
            Assert.That(declared.PositionCount, Is.EqualTo(2562));
            Assert.That(declared.FaceCount, Is.EqualTo(2 * declared.PositionCount - 4), "a closed surface of genus 0");
            Assert.That(declared.MinimumResolution, Is.EqualTo(0), "no base mesh: all of it is resolution updates");
            Assert.That(declared.FinalMaximumResolution, Is.EqualTo(declared.PositionCount));
        });

        var scene = Import(bytes, out var notes);

        var meshes = GetAllMeshes(scene).ToList();
        Assert.That(notes, Is.Empty);
        Assert.That(meshes.Sum(mesh => mesh.Indices.Length / 3), Is.EqualTo(declared.FaceCount));
        var radii = meshes.SelectMany(mesh => mesh.Points).Select(point => point.As<float>().Length()).ToList();
        Assert.That(radii, Has.Count.GreaterThanOrEqualTo(declared.PositionCount));
        Assert.That(radii, Is.All.InRange(0.99f, 1.01f), "every position lies on the unit sphere");
    }

    [Test]
    public void MeshLabTorus_HeaderBlocksAndDeclaration()
    {
        var bytes = File.ReadAllBytes(GetRealFilePath("torus.u3d"));
        AssertMeshLabLayout(bytes);
        var declared = ReadDeclared(bytes);

        Assert.Multiple(() =>
        {
            Assert.That(declared.Attributes, Is.EqualTo(1));
            Assert.That(declared.PositionCount, Is.EqualTo(288));
            Assert.That(declared.FaceCount, Is.EqualTo(2 * declared.PositionCount), "a closed surface of genus 1");
            Assert.That(declared.MinimumResolution, Is.EqualTo(0));
            Assert.That(declared.FinalMaximumResolution, Is.EqualTo(288));
        });
    }

    [TestCase("tetrahedron")]
    [TestCase("dodecahedron")]
    [TestCase("grid")]
    [TestCase("sphere-cap")]
    [TestCase("sphere3")]
    [TestCase("torus")]
    public void MeshLabFile_DecodesToTheMeshItWasWrittenFrom(string name)
    {
        var truth = U3dTestIdtf.Read(GetRealFilePath(name + ".idtf"));

        var scene = Import(File.ReadAllBytes(GetRealFilePath(name + ".u3d")), out var notes);

        List<string> decoded = [];
        var seen = new HashSet<int>();
        foreach (var mesh in GetAllMeshes(scene))
        {
            for (var index = 0; index < mesh.Indices.Length; index += 3)
            {
                var corners = new int[3];
                for (var corner = 0; corner < 3; corner++)
                {
                    var point = mesh.Points[mesh.Indices[index + corner]].As<float>();
                    corners[corner] = FindNearest(truth.Positions, new Vector3F(-point.X, point.Z, -point.Y));
                    seen.Add(corners[corner]);
                }

                decoded.Add(GetFaceKey(corners));
            }
        }

        Assert.Multiple(() =>
        {
            Assert.That(notes, Is.Empty);
            Assert.That(seen, Has.Count.EqualTo(truth.Positions.Count), "every position of the mesh is decoded");
            Assert.That(decoded.OrderBy(key => key, StringComparer.Ordinal),
                Is.EqualTo(truth.Faces.Select(GetFaceKey).OrderBy(key => key, StringComparer.Ordinal)),
                "the faces, each wound as the file was written, are the mesh's");
        });
    }

    private static string GetRealFilePath(string name) =>
        Path.Combine(TestContext.CurrentContext.TestDirectory, "CompilerU3d", name);

    private static int FindNearest(List<Vector3F> positions, Vector3F point)
    {
        var nearest = -1;
        var nearestDistance = float.MaxValue;
        for (var index = 0; index < positions.Count; index++)
        {
            var distance = (positions[index] - point).Length();
            if (distance < nearestDistance)
            {
                nearestDistance = distance;
                nearest = index;
            }
        }

        Assert.That(nearestDistance, Is.LessThan(0.002f), $"{point} is no position of the mesh");
        return nearest;
    }

    private static string GetFaceKey(int[] face)
    {
        var first = Array.IndexOf(face, face.Min());
        return $"{face[first]} {face[(first + 1) % 3]} {face[(first + 2) % 3]}";
    }

    private static void AssertMeshLabLayout(byte[] bytes)
    {
        var blocks = U3dBlockReader.ReadBlocks(bytes, 0, bytes.Length);
        uint[] layout =
        [
            U3dBlockTypes.FileHeader, U3dBlockTypes.PriorityUpdate, U3dBlockTypes.ModifierChain, U3dBlockTypes.ModifierChain,
            U3dBlockTypes.PriorityUpdate, U3dBlockTypes.MeshProgressiveContinuation
        ];
        Assert.That(blocks.Select(block => block.Type), Is.EqualTo(layout));
        var header = blocks[0].OpenData(true);
        Assert.Multiple(() =>
        {
            Assert.That(header.ReadI16(), Is.LessThanOrEqualTo(0), "major version");
            header.ReadI16();
            Assert.That(header.ReadU32() & 0x4, Is.EqualTo(0), "compressed, not the no compression profile");
            Assert.That(header.ReadU32(), Is.InRange(blocks[1].Offset, bytes.Length), "the declaration size");
            Assert.That(header.ReadU64(), Is.EqualTo((ulong)bytes.Length));
            Assert.That(header.ReadU32(), Is.EqualTo(106), "UTF-8");
        });
    }

    private static U3dTestDeclaredMesh ReadDeclared(byte[] bytes)
    {
        foreach (var chain in U3dBlockReader.ReadBlocks(bytes, 0, bytes.Length).Where(block => block.Type == U3dBlockTypes.ModifierChain))
        {
            var reader = chain.OpenData(true);
            reader.ReadString();
            reader.ReadU32();
            reader.ReadU32();
            var modifiers = chain.DataOffset + (int)((reader.PlainBytesRead + 3) & ~3L) + 4;
            foreach (var block in U3dBlockReader.ReadBlocks(bytes, modifiers, chain.DataOffset + chain.DataSize))
            {
                if (block.Type != U3dBlockTypes.MeshDeclaration)
                {
                    continue;
                }

                var data = block.OpenData(false);
                data.ReadString();
                data.ReadU32();
                var declared = new U3dTestDeclaredMesh { Attributes = data.ReadU32(), FaceCount = data.ReadU32() };
                declared.PositionCount = data.ReadU32();
                for (var count = 0; count < 4; count++)
                {
                    data.ReadU32();
                }

                var shadings = data.ReadU32();
                for (var shading = 0; shading < shadings; shading++)
                {
                    data.ReadU32();
                    var layers = data.ReadU32();
                    for (var layer = 0; layer <= layers; layer++)
                    {
                        data.ReadU32();
                    }
                }

                declared.MinimumResolution = data.ReadU32();
                declared.FinalMaximumResolution = data.ReadU32();
                return declared;
            }
        }

        throw new AssertionException("the file declares no CLOD mesh");
    }

    private static SceneData Import(byte[] bytes, out List<string> notes)
    {
        var converter = new U3dConverter("test.u3d", bytes, new ConversionConfig(true));
        var scene = converter.StartConversion();
        notes = converter.UnsupportedFeatures;
        return scene;
    }

    private static IEnumerable<Mesh> GetAllMeshes(SceneData scene) => TestScenes.GetAllMeshes(scene);

    private static byte[] CreateCubeFile(bool compressed)
    {
        var file = new U3dTestFile(compressed);
        file.DeclareBlock(U3dTestFile.CreateModifierChain("Box", 0,
            U3dTestBlocks.CreateModelNode(file, "Box", [CreateParent(string.Empty, Vector3F.Zero)], "BoxMesh"),
            U3dTestBlocks.CreateShadingModifier(file, "Box", "Red", "Glass")));
        U3dTestBlocks.WriteMaterial(file, "RedMaterial", new Vector3F(1, 0, 0), 1, 0.5f);
        U3dTestBlocks.WriteMaterial(file, "GlassMaterial", new Vector3F(0, 0, 1), 0.5f, 0);
        U3dTestBlocks.WriteShader(file, "Red", "RedMaterial");
        U3dTestBlocks.WriteShader(file, "Glass", "GlassMaterial");
        U3dTestBlocks.WriteMesh(file, CreateCubeMesh());
        return file.ToArray();
    }

    private static U3dTestMesh CreateCubeMesh()
    {
        int[][] sides =
        [
            [1, 3, 7, 1, 7, 5], [0, 4, 6, 0, 6, 2], [2, 6, 7, 2, 7, 3],
            [0, 1, 5, 0, 5, 4], [4, 5, 7, 4, 7, 6], [0, 2, 3, 0, 3, 1]
        ];
        List<U3dTestFace> faces = [];
        for (var side = 0; side < sides.Length; side++)
        {
            for (var triangle = 0; triangle < 2; triangle++)
            {
                faces.Add(new U3dTestFace
                {
                    ShadingId = side >= 4 ? 1 : 0,
                    Positions = sides[side][(triangle * 3)..(triangle * 3 + 3)],
                    Normals = [side, side, side]
                });
            }
        }

        return new U3dTestMesh
        {
            Name = "BoxMesh",
            Positions = CreateCubePositions(),
            Normals = CreateCubeNormals(),
            Faces = faces,
            ShadingAttributes = [0, 0],
            ShadingLayers = [0, 0]
        };
    }

    private static List<Vector3F> CreateCubePositions() => Enumerable.Range(0, 8)
        .Select(index => new Vector3F((index & 1) != 0 ? 1 : -1, (index & 2) != 0 ? 1 : -1, (index & 4) != 0 ? 1 : -1))
        .ToList();

    private static List<Vector3F> CreateCubeNormals() =>
    [
        new(1, 0, 0), new(-1, 0, 0), new(0, 1, 0), new(0, -1, 0), new(0, 0, 1), new(0, 0, -1)
    ];

    private static U3dTestMesh CreateTriangle(string name) => new()
    {
        Name = name,
        Positions = [new(0, 0, 0), new(1, 0, 0), new(0, 1, 0)],
        Normals = [new(0, 0, 1)],
        Faces = [new U3dTestFace { ShadingId = 0, Positions = [0, 1, 2], Normals = [0, 0, 0] }]
    };

    private static U3dTestParent CreateParent(string name, Vector3F translation) =>
        new() { Name = name, Transform = Matrix4x4.CreateTranslation<float>(translation) };

    private static Vector3F CreateRandomVector(Random random, float size) =>
        new((random.NextSingle() * 2 - 1) * size, (random.NextSingle() * 2 - 1) * size, (random.NextSingle() * 2 - 1) * size);

    private static Vector3F ToEngine(Vector3F u3d) => new(-u3d.X, -u3d.Z, u3d.Y);

    private static bool IsNear(Vector3F expected, Vector3F actual, float tolerance = Tolerance) =>
        MathF.Abs(expected.X - actual.X) <= tolerance && MathF.Abs(expected.Y - actual.Y) <= tolerance &&
        MathF.Abs(expected.Z - actual.Z) <= tolerance;
}
