using System;
using System.IO;
using System.Linq;
using System.Text.Json.Nodes;
using Adamantium.Engine.Compiler.Models;
using Adamantium.Engine.Compiler.Models.Converters;
using Adamantium.Graphics.Core.Extensions;
using Adamantium.Graphics.Core.Models;
using Adamantium.Mathematics;
using NUnit.Framework;

namespace Adamantium.Engine.CompilerTests;

/// <summary>What a glTF file says is all kept - what the engine uses and what it does not yet - and comes back the same
/// from a baked model.</summary>
[TestFixture]
public class GltfFullReadTests
{
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

    [TestCase(false)]
    [TestCase(true)]
    public void EveryObjectsOwnDataAndExtensions_AreKept_AsTheFileHasThem(bool baked)
    {
        var asset = new GltfTestAsset();
        var positions = asset.Floats("VEC3", 0, 0, 0, 1, 0, 0, 0, 1, 0);
        var primitive = GltfTestAsset.Primitive(positions, material: 0);
        primitive["extras"] = new JsonObject { ["part"] = "blade" };
        primitive["extensions"] = new JsonObject { ["EXT_part"] = new JsonObject { ["n"] = 1 } };
        var mesh = asset.Mesh(primitive);
        asset.Array("meshes")[mesh]!["extras"] = new JsonObject { ["lod"] = 0 };
        asset.Array("materials").Add(new JsonObject
        {
            ["name"] = "Steel",
            ["extras"] = new JsonObject { ["shader"] = "metal" },
            ["extensions"] = new JsonObject { ["EXT_paint"] = new JsonObject { ["coat"] = 2 } }
        });
        var node = asset.Node(new JsonObject
        {
            ["mesh"] = mesh,
            ["camera"] = 0,
            ["extras"] = new JsonObject { ["tag"] = "sword" },
            ["extensions"] = new JsonObject { ["EXT_node"] = new JsonObject { ["x"] = true } }
        });
        asset.Array("cameras").Add(new JsonObject
        {
            ["type"] = "orthographic",
            ["orthographic"] = new JsonObject { ["xmag"] = 4f, ["ymag"] = 2f, ["znear"] = 0.1f, ["zfar"] = 50f },
            ["extras"] = new JsonObject { ["lens"] = "flat" }
        });
        asset.Scene(node);
        asset.Array("scenes")[0]!["extras"] = new JsonObject { ["level"] = 3 };
        asset.Json["asset"]!["generator"] = "Hand";
        asset.Json["asset"]!["copyright"] = "Someone";
        asset.Json["asset"]!["extras"] = new JsonObject { ["build"] = 9 };
        asset.Json["extras"] = new JsonObject { ["project"] = "demo" };
        asset.Json["extensionsUsed"] = new JsonArray("EXT_part", "EXT_paint", "EXT_node");

        var scene = Read(asset.WriteGltf(folder), baked);
        var model = scene.GetModelByID(GltfConverter.NodeId(node));
        var part = model.Meshes.Single();
        var material = scene.Materials[GltfConverter.MaterialId(0)];
        var camera = scene.CameraData[GltfConverter.NodeId(node)];

        Assert.Multiple(() =>
        {
            Assert.That(Json(scene.Extra.Data), Is.EqualTo(Json("""{"project":"demo"}""")));
            Assert.That(scene.Asset.Generator, Is.EqualTo("Hand"));
            Assert.That(scene.Asset.Copyright, Is.EqualTo("Someone"));
            Assert.That(Json(scene.Asset.Extra.Data), Is.EqualTo(Json("""{"build":9}""")));
            Assert.That(Json(scene.Scenes[0].Extra.Data), Is.EqualTo(Json("""{"level":3}""")));
            Assert.That(Json(model.Extra.Data), Is.EqualTo(Json("""{"tag":"sword"}""")));
            Assert.That(Json(model.Extra.Extensions["EXT_node"]), Is.EqualTo(Json("""{"x":true}""")));
            Assert.That(Json(model.MeshExtra.Data), Is.EqualTo(Json("""{"lod":0}""")));
            Assert.That(Json(part.Extra.Data), Is.EqualTo(Json("""{"part":"blade"}""")));
            Assert.That(Json(part.Extra.Extensions["EXT_part"]), Is.EqualTo(Json("""{"n":1}""")));
            Assert.That(material.Name, Is.EqualTo("Steel"));
            Assert.That(Json(material.Extra.Data), Is.EqualTo(Json("""{"shader":"metal"}""")));
            Assert.That(Json(material.Extra.Extensions["EXT_paint"]), Is.EqualTo(Json("""{"coat":2}""")));
            Assert.That(camera.XMag, Is.EqualTo(4));
            Assert.That(camera.YMag, Is.EqualTo(2));
            Assert.That(Json(camera.Extra.Data), Is.EqualTo(Json("""{"lens":"flat"}""")));
        });
    }

    // The second scene is shown; the first's node and one in no scene are kept apart, moved into the engine's axes too.
    [TestCase(false)]
    [TestCase(true)]
    public void EveryScene_IsKept_AndNodesOutsideTheShownOne_AreKeptApart(bool baked)
    {
        var asset = new GltfTestAsset();
        var first = asset.Node(new JsonObject { ["name"] = "First", ["translation"] = new JsonArray(1f, 2f, 3f) });
        var second = asset.Node(new JsonObject { ["name"] = "Second" });
        var loose = asset.Node(new JsonObject { ["name"] = "Loose" });
        asset.Json["scenes"] = new JsonArray(
            new JsonObject { ["name"] = "Day", ["nodes"] = new JsonArray(first) },
            new JsonObject { ["name"] = "Night", ["nodes"] = new JsonArray(second) });
        asset.Json["scene"] = 1;

        var scene = Read(asset.WriteGltf(folder), baked);

        var toEngine = AxisConversion.Between(UpAxis.Y_UP_RH_FRONT_Z, UpAxis.Y_DOWN_RH);
        Assert.Multiple(() =>
        {
            Assert.That(scene.Scenes.Select(s => s.Name), Is.EqualTo(new[] { "Day", "Night" }));
            Assert.That(scene.Scenes.Select(s => s.Roots.Single()),
                Is.EqualTo(new[] { GltfConverter.NodeId(first), GltfConverter.NodeId(second) }));
            Assert.That(scene.DefaultScene, Is.EqualTo(1));
            Assert.That(scene.Models.Dependencies.Select(m => m.Name), Is.EqualTo(new[] { "Second" }));
            Assert.That(scene.OtherNodes.Dependencies.Select(m => m.Name), Is.EquivalentTo(new[] { "First", "Loose" }));
            Assert.That(scene.OtherNodes.Dependencies.Single(m => m.Name == "First").Position,
                Is.EqualTo(Vector3F.TransformCoordinate(new Vector3F(1, 2, 3), toEngine)));
            Assert.That(scene.GetModelByID(GltfConverter.NodeId(loose)), Is.Not.Null);
        });
    }

    // Each channel as the file has it: a cubic spline's three numbers a key, a morph target's weights, a pointer.
    [TestCase(false)]
    [TestCase(true)]
    public void AnAnimation_IsKept_AsTheFileHasIt(bool baked)
    {
        var asset = new GltfTestAsset();
        var node = asset.Node(new JsonObject { ["name"] = "Door" });
        asset.Scene(node);
        var times = asset.Floats("SCALAR", 0, 1);
        var moves = asset.Floats("VEC3", 0, 0, 0, 1, 2, 3);
        var weights = asset.Floats("SCALAR", 0, 0.5f, 1, 0, 1, 0.5f, 0, 0, 1, 0.5f, 0, 1);
        var colors = asset.Floats("VEC4", 1, 0, 0, 1, 0, 0, 1, 1);
        asset.Array("animations").Add(new JsonObject
        {
            ["name"] = "Open",
            ["extras"] = new JsonObject { ["loop"] = false },
            ["channels"] = new JsonArray(
                new JsonObject { ["sampler"] = 0, ["target"] = new JsonObject { ["node"] = node, ["path"] = "translation" } },
                new JsonObject { ["sampler"] = 1, ["target"] = new JsonObject { ["node"] = node, ["path"] = "weights" } },
                new JsonObject
                {
                    ["sampler"] = 2,
                    ["target"] = new JsonObject
                    {
                        ["path"] = "pointer",
                        ["extensions"] = new JsonObject
                        {
                            ["KHR_animation_pointer"] = new JsonObject { ["pointer"] = "/materials/0/pbrMetallicRoughness/baseColorFactor" }
                        }
                    }
                }),
            ["samplers"] = new JsonArray(
                new JsonObject { ["input"] = times, ["output"] = moves, ["interpolation"] = "LINEAR" },
                new JsonObject { ["input"] = times, ["output"] = weights, ["interpolation"] = "CUBICSPLINE" },
                new JsonObject { ["input"] = times, ["output"] = colors, ["interpolation"] = "STEP" })
        });

        var scene = Read(asset.WriteGltf(folder), baked);
        var source = scene.AnimationSources.Single();

        Assert.Multiple(() =>
        {
            Assert.That(source.Name, Is.EqualTo("Open"));
            Assert.That(source.Clip, Is.EqualTo("Open"));
            Assert.That(Json(source.Extra.Data), Is.EqualTo(Json("""{"loop":false}""")));
            Assert.That(source.Channels.Select(c => c.Path), Is.EqualTo(new[] { "translation", "weights", "pointer" }));
            Assert.That(source.Channels[0].Node, Is.EqualTo(GltfConverter.NodeId(node)));
            Assert.That(source.Channels[0].Values, Is.EqualTo(new[] { 0f, 0, 0, 1, 2, 3 }), "in the file's axes");
            Assert.That(source.Channels[0].Components, Is.EqualTo(3));
            Assert.That(source.Channels[1].Interpolation, Is.EqualTo("CUBICSPLINE"));
            Assert.That(source.Channels[1].Times, Is.EqualTo(new[] { 0f, 1 }));
            Assert.That(source.Channels[1].Components, Is.EqualTo(2), "two morph targets");
            Assert.That(source.Channels[2].Node, Is.Null);
            Assert.That(source.Channels[2].Pointer, Is.EqualTo("/materials/0/pbrMetallicRoughness/baseColorFactor"));
            Assert.That(source.Channels[2].Components, Is.EqualTo(4));
        });
    }

    [TestCase(false)]
    [TestCase(true)]
    public void ASkinsSkeletonRoot_IsKept(bool baked)
    {
        var asset = GltfImportTests.SkinnedTriangle(out var meshNode, out var hip, out _);
        asset.Array("skins")[0]!["skeleton"] = hip;

        var scene = Read(asset.WriteGltf(folder), baked);

        Assert.That(scene.Controllers[GltfConverter.NodeId(meshNode)].SkeletonRoot, Is.EqualTo(GltfConverter.NodeId(hip)));
    }

    // The file's tangents are kept, turned as the normals are; the handedness stays, the mirror and the turned-over v
    // undoing each other.
    [TestCase(false)]
    [TestCase(true)]
    public void TheFilesTangents_AreKept_InTheEnginesAxes(bool baked)
    {
        var asset = new GltfTestAsset();
        var positions = asset.Floats("VEC3", 0, 0, 0, 1, 0, 0, 0, 1, 0);
        var uvs = asset.Floats("VEC2", 0, 0, 1, 0, 0, 1);
        var tangents = asset.Floats("VEC4", 1, 0, 0, 1, 1, 0, 0, 1, 1, 0, 0, -1);
        asset.Scene(asset.Node(new JsonObject
        {
            ["mesh"] = asset.Mesh(GltfTestAsset.Primitive(positions, attributes: [("TEXCOORD_0", uvs), ("TANGENT", tangents)]))
        }));

        var mesh = OnlyMesh(Read(asset.WriteGltf(folder), baked));

        var along = Vector3F.TransformNormal(Vector3F.UnitX, AxisConversion.Between(UpAxis.Y_UP_RH_FRONT_Z, UpAxis.Y_DOWN_RH));
        Assert.That(mesh.Tangents.Select(t => (t.X, t.Y, t.Z, t.W)).OrderBy(t => t.W),
            Is.EqualTo(new[] { (along.X, along.Y, along.Z, -1f), (along.X, along.Y, along.Z, 1f), (along.X, along.Y, along.Z, 1f) }));
    }

    // Data a mesh has no array for is kept by its name, and stays with its vertex when like vertices are joined: the
    // last vertex repeats the first but for its _ID, so it is kept apart.
    [TestCase(false)]
    [TestCase(true)]
    public void EveryOtherAttribute_IsKeptByItsName_WithItsVertex(bool baked)
    {
        var asset = new GltfTestAsset();
        var positions = asset.Floats("VEC3", 0, 0, 0, 1, 0, 0, 0, 1, 0, 0, 0, 0);
        var ids = asset.Floats("SCALAR", 10, 11, 12, 13);
        var uv4 = asset.Floats("VEC2", 0, 0, 1, 0, 0, 1, 0, 0);
        var color1 = asset.Floats("VEC4", 1, 0, 0, 1, 0, 1, 0, 1, 0, 0, 1, 1, 1, 0, 0, 1);
        var indices = asset.UShorts(0, 1, 2, 3, 2, 1);
        asset.Scene(asset.Node(new JsonObject
        {
            ["mesh"] = asset.Mesh(GltfTestAsset.Primitive(positions, indices,
                attributes: [("_ID", ids), ("TEXCOORD_4", uv4), ("COLOR_1", color1)]))
        }));

        var mesh = OnlyMesh(Read(asset.WriteGltf(folder), baked));
        var id = mesh.Attributes["_ID"];

        Assert.Multiple(() =>
        {
            Assert.That(mesh.Points.Length, Is.EqualTo(4));
            Assert.That(id.Components, Is.EqualTo(1));
            Assert.That(mesh.Attributes["TEXCOORD_4"].Components, Is.EqualTo(2));
            Assert.That(mesh.Attributes["COLOR_1"].Components, Is.EqualTo(4));
            Assert.That(id.Values.OrderBy(v => v), Is.EqualTo(new[] { 10f, 11, 12, 13 }));
            var engineOrigin = Vector3<double>.Zero;
            var atOrigin = Enumerable.Range(0, 4).Where(i => mesh.Points[i] == engineOrigin).Select(i => id.Values[i]);
            Assert.That(atOrigin, Is.EquivalentTo(new[] { 10f, 13 }), "each _ID with its own vertex");
        });
    }

    [TestCase(false)]
    [TestCase(true)]
    public void MorphTargets_AreKept_WithTheirNamesAndWeights(bool baked)
    {
        var asset = new GltfTestAsset();
        var positions = asset.Floats("VEC3", 0, 0, 0, 1, 0, 0, 0, 1, 0);
        var raise = asset.Floats("VEC3", 0, 1, 0, 0, 1, 0, 0, 1, 0);
        var push = asset.Floats("VEC3", 0, 0, 1, 0, 0, 0, 0, 0, 0);
        var bend = asset.Floats("VEC3", 1, 0, 0, 1, 0, 0, 1, 0, 0);
        var fade = asset.Floats("SCALAR", 0.1f, 0.2f, 0.3f);
        var primitive = GltfTestAsset.Primitive(positions);
        primitive["targets"] = new JsonArray(
            new JsonObject { ["POSITION"] = raise, ["NORMAL"] = bend },
            new JsonObject { ["POSITION"] = push, ["_FADE"] = fade });
        var mesh = asset.Mesh(primitive);
        asset.Array("meshes")[mesh]!["weights"] = new JsonArray(0.5f, 0f);
        asset.Array("meshes")[mesh]!["extras"] = new JsonObject { ["targetNames"] = new JsonArray("Raise", "Push") };
        var node = asset.Node(new JsonObject { ["mesh"] = mesh, ["weights"] = new JsonArray(1f, 0.25f) });
        asset.Scene(node);

        var scene = Read(asset.WriteGltf(folder), baked);
        var part = OnlyMesh(scene);
        var toEngine = AxisConversion.Between(UpAxis.Y_UP_RH_FRONT_Z, UpAxis.Y_DOWN_RH);

        Assert.Multiple(() =>
        {
            Assert.That(part.MorphTargets.Select(t => t.Name), Is.EqualTo(new[] { "Raise", "Push" }));
            Assert.That(part.MorphTargets[0].Positions.Distinct(), Is.EqualTo(new[] { Vector3F.TransformNormal(Vector3F.UnitY, toEngine) }));
            Assert.That(part.MorphTargets[0].Normals.Distinct(), Is.EqualTo(new[] { Vector3F.TransformNormal(Vector3F.UnitX, toEngine) }));
            Assert.That(part.MorphTargets[1].Positions, Has.Some.EqualTo(Vector3F.TransformNormal(Vector3F.UnitZ, toEngine)));
            Assert.That(part.MorphTargets[1].Attributes["_FADE"].Values.OrderBy(v => v), Is.EqualTo(new[] { 0.1f, 0.2f, 0.3f }));
            Assert.That(part.MorphWeights, Is.EqualTo(new[] { 0.5f, 0 }));
            Assert.That(scene.GetModelByID(GltfConverter.NodeId(node)).MorphWeights, Is.EqualTo(new[] { 1f, 0.25f }));
        });
    }

    [TestCase(false)]
    [TestCase(true)]
    public void SamplersAndTextures_AreKept_WithEveryImageATextureHas(bool baked)
    {
        var asset = Triangle();
        asset.Array("images").Add(new JsonObject { ["uri"] = "data:image/png;base64,AAAA", ["name"] = "Bark" });
        asset.Array("images").Add(new JsonObject { ["uri"] = "data:image/webp;base64,AAAA" });
        asset.Array("samplers").Add(new JsonObject
        {
            ["magFilter"] = 9728, ["minFilter"] = 9986, ["wrapS"] = 33071, ["wrapT"] = 33648, ["name"] = "Crisp"
        });
        asset.Array("textures").Add(new JsonObject
        {
            ["source"] = 0,
            ["sampler"] = 0,
            ["name"] = "Bark texture",
            ["extensions"] = new JsonObject { ["EXT_texture_webp"] = new JsonObject { ["source"] = 1 } }
        });

        var scene = Read(asset.WriteGltf(folder), baked);
        var sampler = scene.Samplers[GltfConverter.SamplerId(0)];
        var texture = scene.Textures[GltfConverter.TextureId(0)];

        Assert.Multiple(() =>
        {
            Assert.That(sampler.Name, Is.EqualTo("Crisp"));
            Assert.That(sampler.MagFilter, Is.EqualTo(TextureFilter.Nearest));
            Assert.That(sampler.MinFilter, Is.EqualTo(TextureFilter.Nearest));
            Assert.That(sampler.MipFilter, Is.EqualTo(TextureFilter.Linear));
            Assert.That(sampler.WrapS, Is.EqualTo(TextureWrap.ClampToEdge));
            Assert.That(sampler.WrapT, Is.EqualTo(TextureWrap.MirroredRepeat));
            Assert.That(texture.Name, Is.EqualTo("Bark texture"));
            Assert.That(texture.Image, Is.EqualTo(GltfConverter.ImageId(0)));
            Assert.That(texture.Sampler, Is.EqualTo(GltfConverter.SamplerId(0)));
            Assert.That(texture.Sources["EXT_texture_webp"], Is.EqualTo(GltfConverter.ImageId(1)));
            Assert.That(scene.Images[GltfConverter.ImageId(0)].Name, Is.EqualTo("Bark"));
            Assert.That(scene.Images[GltfConverter.ImageId(1)].MimeType, Is.EqualTo("image/webp"));
        });
    }

    [TestCase(false)]
    [TestCase(true)]
    public void EveryLayerOfAMaterial_IsKept_WithItsTextures(bool baked)
    {
        var asset = Triangle(material: 0);
        asset.Array("images").Add(new JsonObject { ["uri"] = "data:image/png;base64,AAAA" });
        asset.Array("textures").Add(new JsonObject { ["source"] = 0 });
        asset.Array("textures").Add(new JsonObject { ["source"] = 0 });
        asset.Array("materials").Add(new JsonObject
        {
            ["emissiveFactor"] = new JsonArray(1f, 0.5f, 0f),
            ["normalTexture"] = new JsonObject
            {
                ["index"] = 1,
                ["texCoord"] = 1,
                ["scale"] = 2f,
                ["extensions"] = new JsonObject
                {
                    ["KHR_texture_transform"] = new JsonObject { ["offset"] = new JsonArray(0.5f, 0f), ["rotation"] = 1f, ["scale"] = new JsonArray(2f, 2f) }
                }
            },
            ["extensions"] = new JsonObject
            {
                ["KHR_materials_emissive_strength"] = new JsonObject { ["emissiveStrength"] = 4f },
                ["KHR_materials_clearcoat"] = new JsonObject
                {
                    ["clearcoatFactor"] = 0.8f,
                    ["clearcoatNormalTexture"] = new JsonObject { ["index"] = 0, ["scale"] = 0.5f }
                },
                ["KHR_materials_sheen"] = new JsonObject { ["sheenColorFactor"] = new JsonArray(1f, 0f, 0f), ["sheenRoughnessFactor"] = 0.3f },
                ["KHR_materials_ior"] = new JsonObject { ["ior"] = 1.33f },
                ["KHR_materials_volume"] = new JsonObject { ["thicknessFactor"] = 0.2f },
                ["KHR_materials_specular"] = new JsonObject { ["specularColorFactor"] = new JsonArray(0.5f, 0.5f, 1f) },
                ["KHR_materials_unlit"] = new JsonObject(),
                ["EXT_custom"] = new JsonObject { ["v"] = 1 }
            }
        });

        var material = Read(asset.WriteGltf(folder), baked).Materials[GltfConverter.MaterialId(0)];

        Assert.Multiple(() =>
        {
            Assert.That(material.EmissiveFactor, Is.EqualTo(new Vector3F(1, 0.5f, 0)));
            Assert.That(material.EmissiveStrength, Is.EqualTo(4));
            Assert.That(material.NormalTexture.Texture, Is.EqualTo(GltfConverter.TextureId(1)));
            Assert.That(material.NormalTexture.TexCoord, Is.EqualTo(1));
            Assert.That(material.NormalScale, Is.EqualTo(2));
            Assert.That(material.NormalTexture.Transform.Offset, Is.EqualTo(new Vector2F(0.5f, 0)));
            Assert.That(material.NormalTexture.Transform.Rotation, Is.EqualTo(1));
            Assert.That(material.NormalTexture.Transform.Scale, Is.EqualTo(new Vector2F(2, 2)));
            Assert.That(material.Clearcoat.Factor, Is.EqualTo(0.8f));
            Assert.That(material.Clearcoat.NormalTexture.Texture, Is.EqualTo(GltfConverter.TextureId(0)));
            Assert.That(material.Clearcoat.NormalScale, Is.EqualTo(0.5f));
            Assert.That(material.Sheen.ColorFactor, Is.EqualTo(new Vector3F(1, 0, 0)));
            Assert.That(material.Sheen.RoughnessFactor, Is.EqualTo(0.3f));
            Assert.That(material.Ior, Is.EqualTo(1.33f));
            Assert.That(material.Volume.ThicknessFactor, Is.EqualTo(0.2f));
            Assert.That(material.Volume.AttenuationDistance, Is.EqualTo(float.PositiveInfinity));
            Assert.That(material.Specular.Factor, Is.EqualTo(1));
            Assert.That(material.Specular.ColorFactor, Is.EqualTo(new Vector3F(0.5f, 0.5f, 1)));
            Assert.That(material.Unlit, Is.True);
            Assert.That(material.Transmission, Is.Null);
            Assert.That(Json(material.Extra.Extensions["EXT_custom"]), Is.EqualTo(Json("""{"v":1}""")));
        });
    }

    [TestCase(false)]
    [TestCase(true)]
    public void MaterialVariants_AreKept_WithEachMeshsMaterialInThem(bool baked)
    {
        var asset = new GltfTestAsset();
        var positions = asset.Floats("VEC3", 0, 0, 0, 1, 0, 0, 0, 1, 0);
        var primitive = GltfTestAsset.Primitive(positions, material: 0);
        primitive["extensions"] = new JsonObject
        {
            ["KHR_materials_variants"] = new JsonObject
            {
                ["mappings"] = new JsonArray(
                    new JsonObject { ["material"] = 1, ["variants"] = new JsonArray(0, 2) },
                    new JsonObject { ["material"] = 2, ["variants"] = new JsonArray(1) })
            }
        };
        asset.Scene(asset.Node(new JsonObject { ["mesh"] = asset.Mesh(primitive) }));
        asset.Array("materials").Add(new JsonObject());
        asset.Array("materials").Add(new JsonObject());
        asset.Array("materials").Add(new JsonObject());
        asset.Json["extensions"] = new JsonObject
        {
            ["KHR_materials_variants"] = new JsonObject
            {
                ["variants"] = new JsonArray(new JsonObject { ["name"] = "Red" }, new JsonObject { ["name"] = "Blue" },
                    new JsonObject { ["name"] = "Gold" })
            }
        };

        var scene = Read(asset.WriteGltf(folder), baked);
        var mesh = OnlyMesh(scene);

        Assert.Multiple(() =>
        {
            Assert.That(scene.MaterialVariants, Is.EqualTo(new[] { "Red", "Blue", "Gold" }));
            Assert.That(mesh.VariantMaterials[0], Is.EqualTo(GltfConverter.MaterialId(1)));
            Assert.That(mesh.VariantMaterials[1], Is.EqualTo(GltfConverter.MaterialId(2)));
            Assert.That(mesh.VariantMaterials[2], Is.EqualTo(GltfConverter.MaterialId(1)));
        });
    }

    // KHR_mesh_quantization: positions as shorts and normals as normalized bytes, each vertex on four bytes.
    [Test]
    public void AQuantizedMesh_IsRead()
    {
        var asset = new GltfTestAsset();
        var positions = asset.Accessor(asset.View(Shorts(0, 0, 0, 0, 2, 0, 0, 0, 0, 3, 0, 0), stride: 8), 5122, 3, "VEC3");
        var normals = asset.Accessor(asset.View([0, 0, 127, 0, 0, 0, 127, 0, 0, 0, 127, 0], stride: 4), 5120, 3, "VEC3",
            normalized: true);
        asset.Scene(asset.Node(new JsonObject
        {
            ["mesh"] = asset.Mesh(GltfTestAsset.Primitive(positions, attributes: [("NORMAL", normals)]))
        }));
        asset.Json["extensionsUsed"] = new JsonArray("KHR_mesh_quantization");
        asset.Json["extensionsRequired"] = new JsonArray("KHR_mesh_quantization");

        var mesh = OnlyMesh(Read(asset.WriteGltf(folder), false));

        var toEngine = AxisConversion.Between(UpAxis.Y_UP_RH_FRONT_Z, UpAxis.Y_DOWN_RH);
        Assert.Multiple(() =>
        {
            Assert.That(mesh.Points.Select(p => p.As<float>()), Is.EquivalentTo(new[]
            {
                Vector3F.Zero, Vector3F.TransformCoordinate(new Vector3F(2, 0, 0), toEngine),
                Vector3F.TransformCoordinate(new Vector3F(0, 3, 0), toEngine)
            }));
            Assert.That(mesh.Normals.Distinct(), Is.EqualTo(new[] { Vector3F.TransformNormal(Vector3F.UnitZ, toEngine) }));
        });
    }

    [TestCase(false)]
    [TestCase(true)]
    public void CopiesOfAMesh_AreKept_InTheEnginesAxes(bool baked)
    {
        var asset = new GltfTestAsset();
        var positions = asset.Floats("VEC3", 0, 0, 0, 1, 0, 0, 0, 1, 0);
        var places = asset.Floats("VEC3", 0, 0, 0, 5, 0, 0);
        var ids = asset.Floats("SCALAR", 7, 8);
        var node = asset.Node(new JsonObject
        {
            ["mesh"] = asset.Mesh(GltfTestAsset.Primitive(positions)),
            ["extensions"] = new JsonObject
            {
                ["EXT_mesh_gpu_instancing"] = new JsonObject
                {
                    ["attributes"] = new JsonObject { ["TRANSLATION"] = places, ["_ID"] = ids }
                }
            }
        });
        asset.Scene(node);

        var model = Read(asset.WriteGltf(folder), baked).GetModelByID(GltfConverter.NodeId(node));

        var toEngine = AxisConversion.Between(UpAxis.Y_UP_RH_FRONT_Z, UpAxis.Y_DOWN_RH);
        Assert.Multiple(() =>
        {
            Assert.That(model.Instances.Select(i => i.Position),
                Is.EqualTo(new[] { Vector3F.Zero, Vector3F.TransformCoordinate(new Vector3F(5, 0, 0), toEngine) }));
            Assert.That(model.Instances.Select(i => i.Scale), Is.All.EqualTo(Vector3F.One));
            Assert.That(model.InstanceAttributes["_ID"].Values, Is.EqualTo(new[] { 7f, 8 }));
        });
    }

    [TestCase(false)]
    [TestCase(true)]
    public void MetadataPackets_AreKept(bool baked)
    {
        var asset = Triangle();
        asset.Json["extensionsUsed"] = new JsonArray("KHR_xmp_json_ld");
        asset.Json["extensions"] = new JsonObject
        {
            ["KHR_xmp_json_ld"] = new JsonObject
            {
                ["packets"] = new JsonArray(new JsonObject { ["dc:title"] = "Tree" })
            }
        };
        asset.Json["asset"]!["extensions"] = new JsonObject { ["KHR_xmp_json_ld"] = new JsonObject { ["packet"] = 0 } };

        var scene = Read(asset.WriteGltf(folder), baked);

        Assert.Multiple(() =>
        {
            Assert.That(scene.XmpPackets.Select(Json), Is.EqualTo(new[] { Json("""{"dc:title":"Tree"}""") }));
            Assert.That(Json(scene.Asset.Extra.Extensions["KHR_xmp_json_ld"]), Is.EqualTo(Json("""{"packet":0}""")));
        });
    }

    // A mesh with the file's tangents has bitangents to go with them, and its vertices are made for the GPU.
    [TestCase(false)]
    [TestCase(true)]
    public void AMeshWithTheFilesTangents_GivesItsVertices(bool baked)
    {
        var mesh = OnlyMesh(Read(TexturedQuad(withTangents: true).WriteGltf(folder), baked));

        var vertices = mesh.ToMeshVertices();

        Assert.Multiple(() =>
        {
            Assert.That(vertices.Length, Is.EqualTo(mesh.Points.Length));
            for (int i = 0; i < mesh.Points.Length; i++)
            {
                var tangent = mesh.Tangents[i];
                Assert.That(mesh.BiTangents[i], Is.EqualTo(Vector3F.Cross(mesh.Normals[i], (Vector3F)tangent) * tangent.W));
            }
        });
    }

    // Tangents the engine works out run as the file's do: along u, with the same handedness.
    [Test]
    public void WorkedOutTangents_RunAsTheFilesDo()
    {
        var given = OnlyMesh(Read(TexturedQuad(withTangents: true).WriteGltf(folder, "given.gltf"), false));
        var worked = OnlyMesh(Read(TexturedQuad(withTangents: false).WriteGltf(folder, "worked.gltf"), false));

        Assert.Multiple(() =>
        {
            for (int i = 0; i < given.Points.Length; i++)
            {
                var at = Array.IndexOf(worked.Points, given.Points[i]);
                var a = given.Tangents[i];
                var b = worked.Tangents[at];
                Assert.That(Vector3F.Dot((Vector3F)a, (Vector3F)b), Is.GreaterThan(0.99f), $"vertex {i}");
                Assert.That(b.W, Is.EqualTo(a.W), $"vertex {i}");
            }
        });
    }

    // Like vertices are joined in time when a morph target is there and no tangent tells them apart.
    [Test]
    public void JoiningVertices_WithAMorphTarget_TakesLittleTime()
    {
        const int count = 60000;
        var points = new Vector3<double>[count];
        var indices = new int[count];
        var moves = new Vector3F[count];
        for (int i = 0; i < count; i++)
        {
            points[i] = new Vector3<double>(i % 300, i / 300, 0);
            indices[i] = i;
            moves[i] = new Vector3F(0, 0, i % 7);
        }

        var mesh = new Mesh().SetPoints(points).SetIndices(indices).SetMorphTargets([new MorphTarget { Positions = moves }]);
        var clock = System.Diagnostics.Stopwatch.StartNew();
        mesh.Optimize(recalculateNormals: false, recalculateTangents: false);

        Assert.That(clock.Elapsed, Is.LessThan(TimeSpan.FromSeconds(5)));
        Assert.That(mesh.MorphTargets[0].Positions.Length, Is.EqualTo(mesh.Points.Length));
    }

    // Meshes joined into one keep no per-vertex data that only some of them had, and can be joined further.
    [Test]
    public void MergedMeshes_DropWhatOnlySomeHad()
    {
        var withData = new Mesh().SetPoints([new Vector3<double>(0, 0, 0), new Vector3<double>(1, 0, 0), new Vector3<double>(0, 1, 0)]).SetIndices([0, 1, 2])
            .SetAttribute("_ID", new VertexAttribute { Components = 1, Values = [1, 2, 3] })
            .SetMorphTargets([new MorphTarget { Positions = [Vector3F.UnitX, Vector3F.UnitX, Vector3F.UnitX] }]);
        var plain = new Mesh().SetPoints([new Vector3<double>(5, 0, 0), new Vector3<double>(6, 0, 0), new Vector3<double>(5, 1, 0)]).SetIndices([0, 1, 2]);

        var merged = withData.Merge(plain);
        merged.Optimize();

        Assert.Multiple(() =>
        {
            Assert.That(merged.Attributes, Is.Empty);
            Assert.That(merged.MorphTargets, Is.Empty);
        });
    }

    // An animation that moves only nodes of a scene not shown makes no clip; it is kept as the file has it.
    [Test]
    public void AnAnimationOfAnotherScene_MakesNoClip()
    {
        var asset = new GltfTestAsset();
        var shown = asset.Node(new JsonObject { ["name"] = "Shown" });
        var hidden = asset.Node(new JsonObject { ["name"] = "Hidden" });
        asset.Json["scenes"] = new JsonArray(
            new JsonObject { ["nodes"] = new JsonArray(shown) },
            new JsonObject { ["nodes"] = new JsonArray(hidden) });
        asset.Json["scene"] = 0;
        var times = asset.Floats("SCALAR", 0, 1);
        var moves = asset.Floats("VEC3", 0, 0, 0, 1, 0, 0);
        asset.Array("animations").Add(new JsonObject
        {
            ["name"] = "Elsewhere",
            ["channels"] = new JsonArray(new JsonObject { ["sampler"] = 0, ["target"] = new JsonObject { ["node"] = hidden, ["path"] = "translation" } }),
            ["samplers"] = new JsonArray(new JsonObject { ["input"] = times, ["output"] = moves })
        });

        var scene = Read(asset.WriteGltf(folder), false);

        Assert.Multiple(() =>
        {
            Assert.That(scene.Clips, Is.Empty);
            Assert.That(scene.AnimationSources.Single().Clip, Is.Null);
            Assert.That(scene.AnimationSources.Single().Channels.Single().Node, Is.EqualTo(GltfConverter.NodeId(hidden)));
        });
    }

    // Meshes, cameras and skins no node uses are kept; a file named as the root of the rest keeps them apart still.
    [TestCase(false)]
    [TestCase(true)]
    public void WhatNoNodeUses_IsKept(bool baked)
    {
        var asset = GltfImportTests.SkinnedTriangle(out _, out _, out _);
        var positions = asset.Floats("VEC3", 0, 0, 0, 2, 0, 0, 0, 2, 0);
        asset.Array("meshes").Add(new JsonObject { ["name"] = "Spare", ["primitives"] = new JsonArray(GltfTestAsset.Primitive(positions)) });
        asset.Array("skins").Add(new JsonObject { ["joints"] = new JsonArray(0) });
        asset.Array("cameras").Add(new JsonObject
        {
            ["type"] = "perspective", ["perspective"] = new JsonObject { ["yfov"] = 1f, ["znear"] = 0.1f }
        });
        asset.Json["extensionsUsed"] = new JsonArray("EXT_whatever");
        asset.Array("materials").Add(new JsonObject
        {
            ["pbrMetallicRoughness"] = new JsonObject { ["extras"] = new JsonObject { ["note"] = "pbr" } }
        });

        var scene = Read(asset.WriteGltf(folder, "Other nodes.gltf"), baked);
        var spare = scene.GetModelByID(GltfConverter.MeshId(1));

        Assert.Multiple(() =>
        {
            Assert.That(scene.OtherNodes, Is.Not.SameAs(scene.Models));
            Assert.That(spare.Name, Is.EqualTo("Spare"));
            Assert.That(spare.Meshes.Single().Points.Length, Is.EqualTo(3));
            Assert.That(scene.Models.Dependencies.Select(m => m.Name), Has.None.EqualTo("Spare"));
            Assert.That(scene.Controllers.ContainsKey("skin1"), Is.True);
            Assert.That(scene.CameraData.ContainsKey("camera0"), Is.True);
            Assert.That(scene.ExtensionsUsed, Is.EqualTo(new[] { "EXT_whatever" }));
            Assert.That(Json(scene.Materials[GltfConverter.MaterialId(0)].PbrExtra.Data), Is.EqualTo(Json("""{"note":"pbr"}""")));
        });
    }

    // A quad whose texture runs as it is laid in the file, with its own tangents along u or none.
    private static GltfTestAsset TexturedQuad(bool withTangents)
    {
        var asset = new GltfTestAsset();
        var positions = asset.Floats("VEC3", 0, 0, 0, 1, 0, 0, 1, 1, 0, 0, 1, 0);
        var normals = asset.Floats("VEC3", 0, 0, 1, 0, 0, 1, 0, 0, 1, 0, 0, 1);
        var uvs = asset.Floats("VEC2", 0, 1, 1, 1, 1, 0, 0, 0);
        var indices = asset.UShorts(0, 1, 2, 0, 2, 3);
        (string, int)[] attributes = withTangents
            ? [("NORMAL", normals), ("TEXCOORD_0", uvs), ("TANGENT", asset.Floats("VEC4", 1, 0, 0, 1, 1, 0, 0, 1, 1, 0, 0, 1, 1, 0, 0, 1))]
            : [("NORMAL", normals), ("TEXCOORD_0", uvs)];
        asset.Scene(asset.Node(new JsonObject { ["mesh"] = asset.Mesh(GltfTestAsset.Primitive(positions, indices, attributes: attributes)) }));
        return asset;
    }

    private static byte[] Shorts(params short[] values)
    {
        var bytes = new byte[values.Length * 2];
        Buffer.BlockCopy(values, 0, bytes, 0, bytes.Length);
        return bytes;
    }

    private static GltfTestAsset Triangle(int? material = null)
    {
        var asset = new GltfTestAsset();
        var positions = asset.Floats("VEC3", 0, 0, 0, 1, 0, 0, 0, 1, 0);
        asset.Scene(asset.Node(new JsonObject { ["mesh"] = asset.Mesh(GltfTestAsset.Primitive(positions, material: material)) }));
        return asset;
    }

    private static Mesh OnlyMesh(SceneData scene)
    {
        var stack = new System.Collections.Generic.Stack<SceneData.Model>();
        stack.Push(scene.Models);
        var meshes = new System.Collections.Generic.List<Mesh>();
        while (stack.Count > 0)
        {
            var model = stack.Pop();
            meshes.AddRange(model.Meshes);
            foreach (var child in model.Dependencies) stack.Push(child);
        }

        return meshes.Single();
    }

    private static SceneData Read(string path, bool baked)
    {
        var scene = new ModelConverter().ImportFile(path);
        return baked ? SceneDataSerializer.Deserialize(SceneDataSerializer.Serialize(scene)) : scene;
    }

    private static string Json(string json) => JsonNode.Parse(json)!.ToJsonString();
}
