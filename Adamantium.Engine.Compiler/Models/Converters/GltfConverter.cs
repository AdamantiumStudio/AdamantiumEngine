using System;
using System.Collections.Generic;
using System.IO;
using System.Text.Json;
using Adamantium.Engine.Compiler.Converter.Configs;
using Adamantium.Engine.Compiler.Converter.Converters;
using Adamantium.Engine.Compiler.Models.Gltf;
using Adamantium.Graphics.Core;
using Adamantium.Graphics.Core.Models;
using Adamantium.Mathematics;

namespace Adamantium.Engine.Compiler.Models.Converters;

/// <summary>Imports a glTF 2.0 asset, <c>.gltf</c> or <c>.glb</c>: its nodes into the model tree, each primitive of a
/// mesh into a mesh of its own with its material, its materials with every PBR value kept, and its images - in files
/// beside it or carried inside. glTF is Y up, right-handed, in metres; the axes are changed to the engine's after.
/// Its texture coordinates start at the image's top left, so v is read turned over: the axis change turns it back, as
/// it turns those of formats starting at the bottom left.</summary>
public class GltfConverter : ConverterBase
{
    private const int CubicSamplesPerSecond = 30;
    private const int MaxCubicSamplesPerKey = 30 * 60;

    private GltfFile file;
    private readonly HashSet<string> told = [];

    public GltfConverter(string filePath, ConversionConfig config) : base(filePath, config, UpAxis.Y_UP_RH_FRONT_Z)
    {
    }

    protected override void Convert()
    {
        file = GltfFile.Read(FilePath);
        var root = file.Root;
        foreach (var used in root.ExtensionsUsed)
        {
            if (!GltfFile.SupportedExtensions.Contains(used))
            {
                Tell($"the glTF extension {used} is not read");
            }
        }

        ReadImages();
        ReadMaterials();

        SceneDataContainer.Models = SceneDataContainer.CreateMesh(null, "", Path.GetFileNameWithoutExtension(FileName));
        var visited = new HashSet<int>();
        foreach (var node in RootNodes())
        {
            CancellationToken.ThrowIfCancellationRequested();
            AddNode(node, SceneDataContainer.Models, Matrix4x4F.Identity, visited);
        }

        ReadSkins(visited);
        ReadAnimations(visited);
    }

    /// <summary>The nodes of the scene the asset names, or of its first; with no scene at all, every node no other has
    /// as a child.</summary>
    private IEnumerable<int> RootNodes()
    {
        var root = file.Root;
        if (root.Scenes.Count > 0)
        {
            var scene = root.Scene is { } chosen && chosen < root.Scenes.Count ? chosen : 0;
            if (root.Scenes.Count > 1)
            {
                Tell($"only scene {scene} of the {root.Scenes.Count} the asset has is imported");
            }

            return root.Scenes[scene].Nodes;
        }

        var children = new HashSet<int>();
        foreach (var node in root.Nodes)
        {
            children.UnionWith(node.Children);
        }

        var roots = new List<int>();
        for (int i = 0; i < root.Nodes.Count; i++)
        {
            if (!children.Contains(i)) roots.Add(i);
        }

        return roots;
    }

    private void AddNode(int index, SceneData.Model parent, Matrix4x4F parentWorld, HashSet<int> visited)
    {
        // A loop, not recursion: a node chain as deep as a file likes cannot overflow the stack.
        var pending = new Stack<(int Index, SceneData.Model Parent, Matrix4x4F ParentWorld)>();
        pending.Push((index, parent, parentWorld));
        while (pending.Count > 0)
        {
            var (at, under, underWorld) = pending.Pop();
            if ((uint)at >= (uint)file.Root.Nodes.Count || !visited.Add(at))
            {
                Tell($"node {at} is out of range or reached twice - the node tree is not a tree");
                continue;
            }

            CancellationToken.ThrowIfCancellationRequested();
            var node = file.Root.Nodes[at];
            var model = SceneDataContainer.CreateMesh(under, NodeId(at), node.Name ?? $"Node {at}");
            Place(model, node);
            var world = Matrix4x4F.Scaling(model.Scale) * Matrix4x4F.RotationQuaternion(model.Rotation)
                        * Matrix4x4F.Translation(model.Position) * underWorld;
            ReadCamera(at, node, world);
            ReadLight(at, node, world);

            if (node.Mesh is { } meshIndex && meshIndex < file.Root.Meshes.Count)
            {
                var mesh = file.Root.Meshes[meshIndex];
                if (node.Weights != null || mesh.Weights != null)
                {
                    Tell("morph target weights are not read");
                }

                for (int i = 0; i < mesh.Primitives.Count; i++)
                {
                    if (BuildPrimitive(mesh, i) is { } built)
                    {
                        model.Meshes.Add(built);
                    }
                }
            }

            for (int c = node.Children.Count - 1; c >= 0; c--)
            {
                pending.Push((node.Children[c], model, world));
            }
        }
    }

    private void ReadSkins(HashSet<int> imported)
    {
        var nodes = file.Root.Nodes;
        for (int index = 0; index < nodes.Count; index++)
        {
            if (nodes[index].Skin is not { } skinIndex || !imported.Contains(index)) continue;

            if ((uint)skinIndex >= (uint)file.Root.Skins.Count)
            {
                Tell($"node {index} points at skin {skinIndex}, which there is not");
                continue;
            }

            var skin = file.Root.Skins[skinIndex];
            var controller = new SceneData.Controller
            {
                ControllerId = NodeId(index),
                Name = skin.Name ?? $"Skin {skinIndex}",
                MeshId = NodeId(index),
                JointsAreNodes = true
            };

            if (skin.Joints.Count > SceneData.Controller.MaxDrawnJoints)
            {
                Tell($"skin {skinIndex} has {skin.Joints.Count} joints; only the first {SceneData.Controller.MaxDrawnJoints} move its vertices");
            }

            var inverseBinds = skin.InverseBindMatrices is { } accessor ? Floats(accessor, 16) : null;
            for (int j = 0; j < skin.Joints.Count; j++)
            {
                var joint = skin.Joints[j];
                if (!imported.Contains(joint))
                {
                    Tell($"a joint of skin {skinIndex}, node {joint}, is outside the scene imported");
                }

                controller.JointNames.Add(NodeId(joint));
                controller.JointMatrices.Add(inverseBinds != null && (j + 1) * 16 <= inverseBinds.Length
                    ? Matrix(inverseBinds, j * 16)
                    : Matrix4x4F.Identity);
            }

            SceneDataContainer.Controllers[controller.ControllerId] = controller;
        }
    }

    private void ReadAnimations(HashSet<int> imported)
    {
        var animations = file.Root.Animations;
        for (int a = 0; a < animations.Count; a++)
        {
            CancellationToken.ThrowIfCancellationRequested();
            var animation = animations[a];
            var clip = new SceneData.AnimationCollection();
            var channelsOf = new Dictionary<int, List<GltfAnimationChannel>>();
            foreach (var channel in animation.Channels)
            {
                if (channel.Target?.Node is not { } node || !imported.Contains(node)) continue;

                if (channel.Target.Path == "weights")
                {
                    Tell("morph target weights are not animated");
                    continue;
                }

                if ((uint)channel.Sampler >= (uint)animation.Samplers.Count) continue;

                if (!channelsOf.TryGetValue(node, out var list))
                {
                    channelsOf[node] = list = [];
                }

                list.Add(channel);
            }

            foreach (var (node, channels) in channelsOf)
            {
                var frames = NodeFrames(animation, node, channels);
                if (frames.Count > 0)
                {
                    clip[NodeId(node)] = frames;
                }
            }

            if (clip.Count == 0) continue;

            var name = animation.Name ?? $"Animation {a}";
            var unique = name;
            for (int n = 2; SceneDataContainer.Clips.ContainsKey(unique); n++)
            {
                unique = $"{name} {n}";
            }

            SceneDataContainer.Clips[unique] = clip;
        }
    }

    /// <summary>A node's translation, rotation and scale channels brought to one list of keys: a key wherever any of
    /// them has one - and, along a cubic spline, thirty a second between - each channel read there, the node's own pose
    /// where it has none. Keys blend straight to the next, so a stepped channel beside a blended one also gets a key just
    /// before each of its steps, holding its value up to it.</summary>
    private SceneData.FrameCollection NodeFrames(GltfAnimation animation, int node, List<GltfAnimationChannel> channels)
    {
        var rest = new SceneData.Model();
        Place(rest, file.Root.Nodes[node]);
        var curves = new List<(string Path, GltfCurve Curve)>();
        var times = new SortedSet<float>();
        foreach (var channel in channels)
        {
            var sampler = animation.Samplers[channel.Sampler];
            var curve = GltfCurve.Read(file, sampler, channel.Target.Path == "rotation" ? 4 : 3);
            if (curve.Times.Length == 0) continue;

            curves.Add((channel.Target.Path, curve));
            times.UnionWith(curve.Times);
        }

        var stepped = curves.TrueForAll(c => c.Curve.Interpolation == GltfCurve.Step);
        foreach (var (_, curve) in curves)
        {
            for (int k = 0; k + 1 < curve.Times.Length; k++)
            {
                CancellationToken.ThrowIfCancellationRequested();
                if (curve.Interpolation == GltfCurve.Step && !stepped)
                {
                    times.Add(MathF.BitDecrement(curve.Times[k + 1]));
                }
                else if (curve.Interpolation == GltfCurve.CubicSpline)
                {
                    double from = curve.Times[k], span = curve.Times[k + 1] - from;
                    var samples = (int)Math.Min(Math.Ceiling(span * CubicSamplesPerSecond), MaxCubicSamplesPerKey);
                    for (int i = 1; i < samples; i++)
                    {
                        times.Add((float)(from + span * i / samples));
                    }
                }
            }
        }

        var frames = new SceneData.FrameCollection { JointId = NodeId(node) };
        foreach (var time in times)
        {
            var key = new SceneData.KeyFrame(rest.Scale, rest.Position, rest.Rotation,
                stepped ? InterpolationType.Step : InterpolationType.Linear) { TimeStamp = time };
            foreach (var (path, curve) in curves)
            {
                var value = curve.At(time);
                switch (path)
                {
                    case "translation":
                        key.Position = new Vector3F(value[0], value[1], value[2]);
                        break;
                    case "rotation":
                        key.Rotation = QuaternionF.Normalize(new QuaternionF(value[0], value[1], value[2], value[3]));
                        break;
                    case "scale":
                        key.Scale = new Vector3F(value[0], value[1], value[2]);
                        break;
                }
            }

            frames.Add(key);
        }

        return frames;
    }

    private void ReadCamera(int index, GltfNode node, Matrix4x4F world)
    {
        if (node.Camera is not { } cameraIndex || (uint)cameraIndex >= (uint)file.Root.Cameras.Count) return;

        var gltfCamera = file.Root.Cameras[cameraIndex];
        world.Decompose(out _, out var rotation, out var translation);
        var camera = new SceneData.Camera
        {
            ID = NodeId(index),
            Name = gltfCamera.Name ?? node.Name ?? $"Camera {cameraIndex}",
            Rotation = rotation,
            Translation = translation
        };

        if (gltfCamera.Type == "orthographic" && gltfCamera.Orthographic is { } ortho)
        {
            camera.ProjectionType = CameraProjectionType.Orthogonal;
            camera.ZNear = ortho.ZNear;
            camera.ZFar = ortho.ZFar;
            camera.AspectRatio = ortho.YMag != 0 ? ortho.XMag / ortho.YMag : 1;
        }
        else if (gltfCamera.Perspective is { } perspective)
        {
            camera.ProjectionType = CameraProjectionType.Perspective;
            camera.FovType = FovType.YFov;
            camera.Fov = MathHelper.RadiansToDegrees(perspective.YFov);
            camera.AspectRatio = perspective.AspectRatio ?? 0;
            camera.ZNear = perspective.ZNear;
            camera.ZFar = perspective.ZFar ?? 0;
        }

        SceneDataContainer.CameraData[camera.ID] = camera;
    }

    private void ReadLight(int index, GltfNode node, Matrix4x4F world)
    {
        if (node.Extensions == null || !node.Extensions.TryGetValue("KHR_lights_punctual", out var reference)
            || !reference.TryGetProperty("light", out var lightIndexElement) || !lightIndexElement.TryGetInt32(out var lightIndex)
            || file.Root.Extensions == null || !file.Root.Extensions.TryGetValue("KHR_lights_punctual", out var lights)
            || !lights.TryGetProperty("lights", out var list) || lightIndex < 0 || lightIndex >= list.GetArrayLength())
        {
            return;
        }

        var gltfLight = list[lightIndex];
        world.Decompose(out var scale, out var rotation, out var translation);
        var color = Vector3F.One;
        if (gltfLight.TryGetProperty("color", out var colorElement) && colorElement.GetArrayLength() == 3)
        {
            color = new Vector3F(colorElement[0].GetSingle(), colorElement[1].GetSingle(), colorElement[2].GetSingle());
        }

        var intensity = gltfLight.TryGetProperty("intensity", out var intensityElement) ? intensityElement.GetSingle() : 1;
        var type = gltfLight.TryGetProperty("type", out var typeElement) ? typeElement.GetString() : "point";
        var light = new SceneData.Light
        {
            ID = NodeId(index),
            Name = gltfLight.TryGetProperty("name", out var nameElement) ? nameElement.GetString() : node.Name ?? $"Light {lightIndex}",
            LightType = type switch { "directional" => LightType.Directional, "spot" => LightType.Spot, _ => LightType.Point },
            Color = color * intensity,
            Position = translation,
            Rotation = rotation,
            Scale = scale,
            ConstantAttenuation = 1,
            QuadraticAttenuation = 1
        };

        if (type == "spot" && gltfLight.TryGetProperty("spot", out var spot))
        {
            var outer = spot.TryGetProperty("outerConeAngle", out var outerElement) ? outerElement.GetSingle() : MathF.PI / 4;
            light.FalloffAngle = MathHelper.RadiansToDegrees(outer);
        }

        SceneDataContainer.LightData[light.ID] = light;
    }

    private static Matrix4x4F Matrix(float[] m, int at) => new(m[at], m[at + 1], m[at + 2], m[at + 3], m[at + 4], m[at + 5],
        m[at + 6], m[at + 7], m[at + 8], m[at + 9], m[at + 10], m[at + 11], m[at + 12], m[at + 13], m[at + 14], m[at + 15]);

    /// <summary>The ID a node's model takes, so a skin or a camera can find it by its index.</summary>
    public static string NodeId(int index) => $"node{index}";

    private static void Place(SceneData.Model model, GltfNode node)
    {
        if (node.Matrix is { Length: 16 } m)
        {
            Matrix(m, 0).Decompose(out var scale, out var rotation, out var translation);
            model.Scale = scale;
            model.Rotation = rotation;
            model.Position = translation;
            return;
        }

        if (node.Translation is { Length: 3 } t) model.Position = new Vector3F(t[0], t[1], t[2]);
        if (node.Rotation is { Length: 4 } r) model.Rotation = QuaternionF.Normalize(new QuaternionF(r[0], r[1], r[2], r[3]));
        if (node.Scale is { Length: 3 } s) model.Scale = new Vector3F(s[0], s[1], s[2]);
    }

    private Mesh BuildPrimitive(GltfMesh gltfMesh, int index)
    {
        var primitive = gltfMesh.Primitives[index];
        var name = $"{gltfMesh.Name ?? "Mesh"} {index}";
        if (!primitive.Attributes.TryGetValue("POSITION", out var positionAccessor))
        {
            Tell($"{name} has no positions and is left out");
            return null;
        }

        if (primitive.Targets is { Count: > 0 })
        {
            Tell("morph targets are not read");
        }

        if (primitive.Extensions != null)
        {
            foreach (var extension in primitive.Extensions.Keys)
            {
                Tell($"the primitive extension {extension} is not read");
            }
        }

        var points = Vectors3(positionAccessor);
        var indices = primitive.Indices is { } indexAccessor ? GltfAccessors.ReadIntegers(file, indexAccessor) : Sequence(points.Length);
        foreach (var i in indices)
        {
            if ((uint)i >= (uint)points.Length)
            {
                throw new InvalidDataException($"{FilePath}: {name} has an index {i} past its {points.Length} vertices");
            }
        }

        var (topology, list) = AsList(primitive.Mode, indices);
        var mesh = new Mesh(topology) { Name = name };
        mesh.SetPoints(points).SetIndices(list);

        if (primitive.Attributes.TryGetValue("NORMAL", out var normals))
        {
            mesh.SetNormals(Vectors3F(normals));
        }

        var transform = BaseColorTransform(primitive.Material);
        for (int channel = 0; channel < 4; channel++)
        {
            if (primitive.Attributes.TryGetValue($"TEXCOORD_{channel}", out var uvs))
            {
                mesh.SetUVs(channel, TextureCoordinates(uvs, transform is { } t && t.TexCoord == channel ? t : null));
            }
        }

        if (primitive.Attributes.TryGetValue("COLOR_0", out var colors))
        {
            mesh.SetColors(Colors(colors));
        }

        if (primitive.Attributes.TryGetValue("JOINTS_0", out var joints) && primitive.Attributes.TryGetValue("WEIGHTS_0", out var weights))
        {
            var jointIndices = GltfAccessors.ReadIntegers(file, joints);
            var jointWeights = Floats(weights, 4);
            var indices4 = new Vector4F[jointIndices.Length / 4];
            var weights4 = new Vector4F[jointWeights.Length / 4];
            for (int v = 0; v < indices4.Length; v++)
            {
                indices4[v] = new Vector4F(jointIndices[v * 4], jointIndices[v * 4 + 1], jointIndices[v * 4 + 2], jointIndices[v * 4 + 3]);
            }

            for (int v = 0; v < weights4.Length; v++)
            {
                weights4[v] = new Vector4F(jointWeights[v * 4], jointWeights[v * 4 + 1], jointWeights[v * 4 + 2], jointWeights[v * 4 + 3]);
            }

            mesh.SetJointIndices(indices4).SetJointWeights(weights4);
            if (primitive.Attributes.ContainsKey("JOINTS_1"))
            {
                Tell("a vertex's joints past its first four are not read");
            }
        }

        if (primitive.Material is { } material && material < file.Root.Materials.Count)
        {
            mesh.MaterialID = MaterialId(material);
        }

        return mesh;
    }

    private static (PrimitiveType Topology, int[] Indices) AsList(int mode, int[] indices)
    {
        switch (mode)
        {
            case GltfPrimitiveMode.Points:
                return (PrimitiveType.PointList, indices);
            case GltfPrimitiveMode.Lines:
                return (PrimitiveType.LineList, indices);
            case GltfPrimitiveMode.LineStrip:
                return (PrimitiveType.LineStrip, indices);
            case GltfPrimitiveMode.LineLoop:
            {
                if (indices.Length == 0) return (PrimitiveType.LineStrip, indices);
                var closed = new int[indices.Length + 1];
                indices.CopyTo(closed, 0);
                closed[^1] = indices[0];
                return (PrimitiveType.LineStrip, closed);
            }
            case GltfPrimitiveMode.TriangleStrip:
            {
                var list = new List<int>(Math.Max(0, indices.Length - 2) * 3);
                for (int i = 0; i + 2 < indices.Length; i++)
                {
                    if (i % 2 == 0) list.AddRange([indices[i], indices[i + 1], indices[i + 2]]);
                    else list.AddRange([indices[i + 1], indices[i], indices[i + 2]]);
                }

                return (PrimitiveType.TriangleList, list.ToArray());
            }
            case GltfPrimitiveMode.TriangleFan:
            {
                var list = new List<int>(Math.Max(0, indices.Length - 2) * 3);
                for (int i = 1; i + 1 < indices.Length; i++)
                {
                    list.AddRange([indices[0], indices[i], indices[i + 1]]);
                }

                return (PrimitiveType.TriangleList, list.ToArray());
            }
            default:
                return (PrimitiveType.TriangleList, indices);
        }
    }

    private void ReadImages()
    {
        var images = file.Root.Images;
        for (int i = 0; i < images.Count; i++)
        {
            var gltfImage = images[i];
            var image = new SceneData.Image
            {
                ID = ImageId(i),
                FilePath = file.ImageFile(gltfImage),
                Data = file.ImageBytes(gltfImage),
                MimeType = gltfImage.MimeType
            };
            if (image.FilePath != null)
            {
                image.ImageName = Uri.UnescapeDataString(gltfImage.Uri);
                if (!File.Exists(image.FilePath))
                {
                    Tell($"the image {gltfImage.Uri} is not found");
                }
            }

            SceneDataContainer.Images[image.ID] = image;
        }
    }

    private void ReadMaterials()
    {
        var materials = file.Root.Materials;
        for (int i = 0; i < materials.Count; i++)
        {
            var gltfMaterial = materials[i];
            var pbr = gltfMaterial.PbrMetallicRoughness;
            var baseColor = pbr?.BaseColorFactor is { Length: 4 } factor
                ? new Vector4F(factor[0], factor[1], factor[2], factor[3])
                : Vector4F.One;
            var emissive = gltfMaterial.EmissiveFactor is { Length: 3 } e ? new Vector3F(e[0], e[1], e[2]) : Vector3F.Zero;
            emissive *= EmissiveStrength(gltfMaterial);
            var alphaMode = gltfMaterial.AlphaMode switch
            {
                "MASK" => AlphaMode.Mask,
                "BLEND" => AlphaMode.Blend,
                _ => AlphaMode.Opaque
            };

            var material = new SceneData.Material
            {
                ID = MaterialId(i),
                DiffuseColor = baseColor,
                DiffuseMap = ImageOf(pbr?.BaseColorTexture),
                MetallicFactor = pbr?.MetallicFactor ?? 1,
                RoughnessFactor = pbr?.RoughnessFactor ?? 1,
                MetallicRoughnessMap = ImageOf(pbr?.MetallicRoughnessTexture),
                NormalMap = ImageOf(gltfMaterial.NormalTexture),
                NormalScale = gltfMaterial.NormalTexture?.Scale ?? 1,
                OcclusionMap = ImageOf(gltfMaterial.OcclusionTexture),
                OcclusionStrength = gltfMaterial.OcclusionTexture?.Strength ?? 1,
                EmissiveMap = ImageOf(gltfMaterial.EmissiveTexture),
                Emission = new Vector4F(emissive, 1),
                AlphaMode = alphaMode,
                AlphaCutoff = gltfMaterial.AlphaCutoff,
                DoubleSided = gltfMaterial.DoubleSided,
                Transparency = alphaMode == AlphaMode.Blend ? baseColor.W : 1,
                Shininess = 1
            };

            if (gltfMaterial.Extensions != null)
            {
                foreach (var extension in gltfMaterial.Extensions.Keys)
                {
                    if (!GltfFile.SupportedExtensions.Contains(extension))
                    {
                        Tell($"the material extension {extension} is not read");
                    }
                }
            }

            SceneDataContainer.Materials[material.ID] = material;
        }
    }

    private static float EmissiveStrength(GltfMaterial material)
    {
        if (material.Extensions != null
            && material.Extensions.TryGetValue("KHR_materials_emissive_strength", out var extension)
            && extension.ValueKind == JsonValueKind.Object
            && extension.TryGetProperty("emissiveStrength", out var strength)
            && strength.TryGetSingle(out var value))
        {
            return value;
        }

        return 1;
    }

    /// <summary>The key in <see cref="SceneData.Images"/> of the image a texture shows, or null.</summary>
    private string ImageOf(GltfTextureInfo info)
    {
        if (info == null) return null;

        if ((uint)info.Index >= (uint)file.Root.Textures.Count)
        {
            Tell($"a material points at texture {info.Index}, which there is not");
            return null;
        }

        var set = TransformOf(info)?.TexCoord ?? info.TexCoord;
        if (set != 0)
        {
            Tell($"a texture on texture coordinates {set} is drawn with the first ones");
        }

        var texture = file.Root.Textures[info.Index];
        if (texture.Source is not { } source || source >= file.Root.Images.Count)
        {
            Tell("a texture with no image of its own (an image format by extension) is left out");
            return null;
        }

        return ImageId(source);
    }

    /// <summary>The key a material takes in <see cref="SceneData.Materials"/>.</summary>
    public static string MaterialId(int index) => $"material{index}";

    /// <summary>The key an image takes in <see cref="SceneData.Images"/>.</summary>
    public static string ImageId(int index) => $"image{index}";

    private void Tell(string feature)
    {
        if (told.Add(feature))
        {
            UnsupportedFeatures.Add(feature);
        }
    }

    private static int[] Sequence(int count)
    {
        var indices = new int[count];
        for (int i = 0; i < count; i++) indices[i] = i;
        return indices;
    }

    private Vector3[] Vectors3(int accessor)
    {
        var values = Floats(accessor, 3);
        var vectors = new Vector3[values.Length / 3];
        for (int i = 0; i < vectors.Length; i++)
        {
            vectors[i] = new Vector3(values[i * 3], values[i * 3 + 1], values[i * 3 + 2]);
        }

        return vectors;
    }

    private Vector3F[] Vectors3F(int accessor)
    {
        var values = Floats(accessor, 3);
        var vectors = new Vector3F[values.Length / 3];
        for (int i = 0; i < vectors.Length; i++)
        {
            vectors[i] = new Vector3F(values[i * 3], values[i * 3 + 1], values[i * 3 + 2]);
        }

        return vectors;
    }

    // The transform is glTF's, about the image's top left, so it is laid on before v is turned over.
    private Vector2F[] TextureCoordinates(int accessor, TextureTransform? transform)
    {
        var values = Floats(accessor, 2);
        var vectors = new Vector2F[values.Length / 2];
        for (int i = 0; i < vectors.Length; i++)
        {
            var uv = new Vector2F(values[i * 2], values[i * 2 + 1]);
            if (transform is { } t)
            {
                uv = new Vector2F(t.Cos * t.Scale.X * uv.X + t.Sin * t.Scale.Y * uv.Y + t.Offset.X,
                    -t.Sin * t.Scale.X * uv.X + t.Cos * t.Scale.Y * uv.Y + t.Offset.Y);
            }

            vectors[i] = new Vector2F(uv.X, 1 - uv.Y);
        }

        return vectors;
    }

    private readonly record struct TextureTransform(Vector2F Offset, float Cos, float Sin, Vector2F Scale, int TexCoord);

    /// <summary>The KHR_texture_transform of a material's base color texture, the one drawn; the texture coordinates it
    /// reads take it. Another texture of the material transformed otherwise is told, as those coordinates hold one.</summary>
    private TextureTransform? BaseColorTransform(int? materialIndex)
    {
        if (materialIndex is not { } index || (uint)index >= (uint)file.Root.Materials.Count) return null;

        var material = file.Root.Materials[index];
        var baseColor = material.PbrMetallicRoughness?.BaseColorTexture;
        var drawn = TransformOf(baseColor);
        foreach (var other in new GltfTextureInfo[]
                 {
                     material.PbrMetallicRoughness?.MetallicRoughnessTexture, material.NormalTexture,
                     material.OcclusionTexture, material.EmissiveTexture
                 })
        {
            if (other != null && TransformOf(other) is { } own && own != drawn)
            {
                Tell("a texture transformed otherwise than the base color is laid as the base color is");
            }
        }

        return drawn;
    }

    private static TextureTransform? TransformOf(GltfTextureInfo info)
    {
        if (info?.Extensions == null || !info.Extensions.TryGetValue("KHR_texture_transform", out var extension)
            || extension.ValueKind != JsonValueKind.Object)
        {
            return null;
        }

        var offset = Pair(extension, "offset", Vector2F.Zero);
        var scale = Pair(extension, "scale", Vector2F.One);
        var rotation = extension.TryGetProperty("rotation", out var r) && r.TryGetSingle(out var radians) ? radians : 0;
        var texCoord = extension.TryGetProperty("texCoord", out var c) && c.TryGetInt32(out var set) ? set : info.TexCoord;
        return new TextureTransform(offset, MathF.Cos(rotation), MathF.Sin(rotation), scale, texCoord);

        static Vector2F Pair(JsonElement element, string name, Vector2F otherwise) =>
            element.TryGetProperty(name, out var pair) && pair.ValueKind == JsonValueKind.Array && pair.GetArrayLength() == 2
                ? new Vector2F(pair[0].GetSingle(), pair[1].GetSingle())
                : otherwise;
    }

    private Color[] Colors(int accessor)
    {
        var values = GltfAccessors.ReadFloats(file, accessor, out var components);
        if (components is not (3 or 4))
        {
            throw new InvalidDataException($"{FilePath}: accessor {accessor} of colors has {components} components");
        }

        var colors = new Color[values.Length / components];
        for (int i = 0; i < colors.Length; i++)
        {
            var at = i * components;
            colors[i] = new Color(new Vector4F(values[at], values[at + 1], values[at + 2], components == 4 ? values[at + 3] : 1));
        }

        return colors;
    }

    private float[] Floats(int accessor, int components)
    {
        var values = GltfAccessors.ReadFloats(file, accessor, out var read);
        if (read != components)
        {
            throw new InvalidDataException($"{FilePath}: accessor {accessor} has {read} components, {components} are wanted");
        }

        return values;
    }
}
