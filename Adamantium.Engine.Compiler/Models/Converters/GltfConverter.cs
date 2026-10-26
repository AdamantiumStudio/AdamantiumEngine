using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
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
    private const string OtherNodesId = "otherNodes";
    private const string DracoExtension = "KHR_draco_mesh_compression";

    // The attributes a Mesh has arrays of; any other is kept by its name.
    private static readonly HashSet<string> DrawnAttributes =
    [
        "POSITION", "NORMAL", "TANGENT", "TEXCOORD_0", "TEXCOORD_1", "TEXCOORD_2", "TEXCOORD_3", "COLOR_0", "JOINTS_0", "WEIGHTS_0"
    ];

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
                Tell($"the glTF extension {used} is kept as the file has it, not understood");
            }
        }

        SceneDataContainer.Extra = ExtraOf(root.Extras, root.Extensions);
        SceneDataContainer.ExtensionsUsed.AddRange(root.ExtensionsUsed);
        SceneDataContainer.ExtensionsRequired.AddRange(root.ExtensionsRequired);
        if (root.Extensions != null && root.Extensions.TryGetValue("KHR_xmp_json_ld", out var xmp)
            && xmp.TryGetProperty("packets", out var packets) && packets.ValueKind == JsonValueKind.Array)
        {
            foreach (var packet in packets.EnumerateArray())
            {
                SceneDataContainer.XmpPackets.Add(packet.GetRawText());
            }
        }

        if (root.Asset is { } asset)
        {
            SceneDataContainer.Asset = new SceneData.AssetInfo
            {
                Version = asset.Version,
                MinVersion = asset.MinVersion,
                Generator = asset.Generator,
                Copyright = asset.Copyright,
                Extra = ExtraOf(asset.Extras, asset.Extensions)
            };
        }

        ReadImages();
        ReadSamplers();
        ReadTextures();
        ReadVariants();
        ReadMaterials();
        ReadScenes();

        SceneDataContainer.Models = SceneDataContainer.CreateMesh(null, "", Path.GetFileNameWithoutExtension(FileName));
        var visited = new HashSet<int>();
        foreach (var node in ShownRoots())
        {
            CancellationToken.ThrowIfCancellationRequested();
            AddNode(node, SceneDataContainer.Models, Matrix4x4F.Identity, visited);
        }

        var shown = new HashSet<int>(visited);
        foreach (var node in OtherRoots(visited))
        {
            CancellationToken.ThrowIfCancellationRequested();
            SceneDataContainer.OtherNodes ??= SceneDataContainer.CreateMesh(null, OtherNodesId, "Other nodes");
            AddNode(node, SceneDataContainer.OtherNodes, Matrix4x4F.Identity, visited);
        }

        ReadUnused(visited);
        ReadSkins(visited, shown);
        ReadAnimations(shown);
    }

    private void ReadScenes()
    {
        var root = file.Root;
        foreach (var scene in root.Scenes)
        {
            var kept = new SceneData.Scene { Name = scene.Name, Extra = ExtraOf(scene.Extras, scene.Extensions) };
            foreach (var node in scene.Nodes)
            {
                kept.Roots.Add(NodeId(node));
            }

            SceneDataContainer.Scenes.Add(kept);
        }

        if (root.Scenes.Count > 0)
        {
            SceneDataContainer.DefaultScene = root.Scene is { } chosen && chosen < root.Scenes.Count ? chosen : 0;
        }
    }

    /// <summary>The nodes of the scene the asset names, or of its first; with no scene at all, every node no other has
    /// as a child.</summary>
    private IEnumerable<int> ShownRoots()
    {
        var root = file.Root;
        if (root.Scenes.Count > 0)
        {
            var scene = SceneDataContainer.DefaultScene;
            if (root.Scenes.Count > 1)
            {
                Tell($"only scene {scene} of the {root.Scenes.Count} the asset has is shown; the others are kept");
            }

            return root.Scenes[scene].Nodes;
        }

        return Parentless();
    }

    // The roots of what the shown scene leaves out: those of the other scenes, then every node no other has as a child
    // that is still not read.
    private IEnumerable<int> OtherRoots(HashSet<int> visited)
    {
        foreach (var scene in file.Root.Scenes)
        {
            foreach (var node in scene.Nodes)
            {
                if (!visited.Contains(node))
                {
                    yield return node;
                }
            }
        }

        foreach (var node in Parentless())
        {
            if (!visited.Contains(node))
            {
                yield return node;
            }
        }
    }

    private List<int> Parentless()
    {
        var root = file.Root;
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
            model.Extra = ExtraOf(node.Extras, node.Extensions);
            Place(model, node);
            ReadInstances(model, node);
            var world = Matrix4x4F.Scaling(model.Scale) * Matrix4x4F.RotationQuaternion(model.Rotation)
                        * Matrix4x4F.Translation(model.Position) * underWorld;
            ReadCamera(at, node, world);
            ReadLight(at, node, world);

            if (node.Mesh is { } meshIndex && meshIndex < file.Root.Meshes.Count)
            {
                var mesh = file.Root.Meshes[meshIndex];
                model.MeshExtra = ExtraOf(mesh.Extras, mesh.Extensions);
                model.MorphWeights = node.Weights;

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

    // EXT_mesh_gpu_instancing: each copy's translation, rotation and scale, the rest kept by name.
    private void ReadInstances(SceneData.Model model, GltfNode node)
    {
        if (node.Extensions == null || !node.Extensions.TryGetValue("EXT_mesh_gpu_instancing", out var extension)
            || !extension.TryGetProperty("attributes", out var attributes) || attributes.ValueKind != JsonValueKind.Object)
        {
            return;
        }

        float[] translations = null, rotations = null, scales = null;
        model.InstanceAttributes = new Dictionary<string, VertexAttribute>();
        foreach (var attribute in attributes.EnumerateObject())
        {
            if (!attribute.Value.TryGetInt32(out var accessor))
            {
                continue;
            }

            switch (attribute.Name)
            {
                case "TRANSLATION":
                    translations = Floats(accessor, 3);
                    break;
                case "ROTATION":
                    rotations = Floats(accessor, 4);
                    break;
                case "SCALE":
                    scales = Floats(accessor, 3);
                    break;
                default:
                    model.InstanceAttributes[attribute.Name] = Attribute(accessor);
                    break;
            }
        }

        var count = Math.Max(translations?.Length / 3 ?? 0, Math.Max(rotations?.Length / 4 ?? 0, scales?.Length / 3 ?? 0));
        model.Instances = new List<InstanceTransform>(count);
        for (int i = 0; i < count; i++)
        {
            // An attribute shorter than the others leaves the copies past its end as they are by default.
            var copy = new InstanceTransform();
            if (translations != null && (i + 1) * 3 <= translations.Length)
            {
                copy.Position = new Vector3F(translations[i * 3], translations[i * 3 + 1], translations[i * 3 + 2]);
            }

            if (rotations != null && (i + 1) * 4 <= rotations.Length)
            {
                copy.Rotation = QuaternionF.Normalize(new QuaternionF(rotations[i * 4], rotations[i * 4 + 1], rotations[i * 4 + 2], rotations[i * 4 + 3]));
            }

            if (scales != null && (i + 1) * 3 <= scales.Length)
            {
                copy.Scale = new Vector3F(scales[i * 3], scales[i * 3 + 1], scales[i * 3 + 2]);
            }

            model.Instances.Add(copy);
        }

        Tell("copies of a mesh drawn many times over (EXT_mesh_gpu_instancing) are kept, not drawn");
    }

    private void ReadSkins(HashSet<int> imported, HashSet<int> shown)
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
            foreach (var joint in skin.Joints)
            {
                if (shown.Contains(index) && !shown.Contains(joint))
                {
                    Tell($"a joint of skin {skinIndex}, node {joint}, is outside the scene shown");
                }
            }

            var controller = Controller(skinIndex, NodeId(index), NodeId(index));
            SceneDataContainer.Controllers[controller.ControllerId] = controller;
        }
    }

    private SceneData.Controller Controller(int skinIndex, string id, string meshId)
    {
        var skin = file.Root.Skins[skinIndex];
        var controller = new SceneData.Controller
        {
            ControllerId = id,
            Name = skin.Name ?? $"Skin {skinIndex}",
            MeshId = meshId,
            JointsAreNodes = true,
            SkeletonRoot = skin.Skeleton is { } skeleton ? NodeId(skeleton) : null,
            Extra = ExtraOf(skin.Extras, skin.Extensions)
        };

        var inverseBinds = skin.InverseBindMatrices is { } accessor ? Floats(accessor, 16) : null;
        for (int j = 0; j < skin.Joints.Count; j++)
        {
            controller.JointNames.Add(NodeId(skin.Joints[j]));
            controller.JointMatrices.Add(inverseBinds != null && (j + 1) * 16 <= inverseBinds.Length
                ? Matrix(inverseBinds, j * 16)
                : Matrix4x4F.Identity);
        }

        return controller;
    }

    // Meshes, cameras and skins no node read uses are kept too: a mesh under OtherNodes, the rest by their own keys.
    private void ReadUnused(HashSet<int> imported)
    {
        var root = file.Root;
        var meshes = new HashSet<int>();
        var cameras = new HashSet<int>();
        var skins = new HashSet<int>();
        foreach (var index in imported)
        {
            var node = root.Nodes[index];
            if (node.Mesh is { } mesh)
            {
                meshes.Add(mesh);
            }

            if (node.Camera is { } camera)
            {
                cameras.Add(camera);
            }

            if (node.Skin is { } skin)
            {
                skins.Add(skin);
            }
        }

        for (int i = 0; i < root.Meshes.Count; i++)
        {
            if (meshes.Contains(i))
            {
                continue;
            }

            var gltfMesh = root.Meshes[i];
            SceneDataContainer.OtherNodes ??= SceneDataContainer.CreateMesh(null, OtherNodesId, "Other nodes");
            var model = SceneDataContainer.CreateMesh(SceneDataContainer.OtherNodes, MeshId(i), gltfMesh.Name ?? $"Mesh {i}");
            model.MeshExtra = ExtraOf(gltfMesh.Extras, gltfMesh.Extensions);
            for (int p = 0; p < gltfMesh.Primitives.Count; p++)
            {
                if (BuildPrimitive(gltfMesh, p) is { } built)
                {
                    model.Meshes.Add(built);
                }
            }
        }

        for (int i = 0; i < root.Cameras.Count; i++)
        {
            if (!cameras.Contains(i))
            {
                KeepCamera(root.Cameras[i], $"camera{i}", $"Camera {i}", Matrix4x4F.Identity);
            }
        }

        for (int i = 0; i < root.Skins.Count; i++)
        {
            if (!skins.Contains(i))
            {
                var controller = Controller(i, $"skin{i}", null);
                SceneDataContainer.Controllers[controller.ControllerId] = controller;
            }
        }
    }

    /// <summary>The key a mesh no node shows takes, as the ID of its model under <see cref="SceneData.OtherNodes"/>.</summary>
    public static string MeshId(int index) => $"mesh{index}";

    private void ReadAnimations(HashSet<int> imported)
    {
        var animations = file.Root.Animations;
        for (int a = 0; a < animations.Count; a++)
        {
            CancellationToken.ThrowIfCancellationRequested();
            var animation = animations[a];
            var source = Source(animation);
            SceneDataContainer.AnimationSources.Add(source);
            var clip = new SceneData.AnimationCollection();
            var channelsOf = new Dictionary<int, List<GltfAnimationChannel>>();
            foreach (var channel in animation.Channels)
            {
                if (channel.Target?.Node is not { } node || !imported.Contains(node)) continue;

                if (channel.Target.Path == "weights")
                {
                    Tell("morph target weights are kept, not animated");
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
            source.Clip = unique;
        }
    }

    // An animation as the file has it: each channel's target and its sampler's curve, unchanged.
    private SceneData.AnimationSource Source(GltfAnimation animation)
    {
        var source = new SceneData.AnimationSource
        {
            Name = animation.Name,
            Extra = ExtraOf(animation.Extras, animation.Extensions)
        };

        foreach (var channel in animation.Channels)
        {
            if ((uint)channel.Sampler >= (uint)animation.Samplers.Count || channel.Target == null)
            {
                continue;
            }

            var sampler = animation.Samplers[channel.Sampler];
            var times = GltfAccessors.ReadFloats(file, sampler.Input, out _);
            var values = GltfAccessors.ReadFloats(file, sampler.Output, out _);
            var perKey = sampler.Interpolation == "CUBICSPLINE" ? 3 * times.Length : times.Length;
            source.Channels.Add(new AnimationChannel
            {
                Node = channel.Target.Node is { } node ? NodeId(node) : null,
                Path = channel.Target.Path,
                Pointer = Pointer(channel.Target),
                Interpolation = sampler.Interpolation,
                Times = times,
                Values = values,
                Components = perKey > 0 ? values.Length / perKey : 0,
                Extra = ExtraOf(channel.Extras, channel.Extensions),
                TargetExtra = ExtraOf(channel.Target.Extras, channel.Target.Extensions),
                SamplerExtra = ExtraOf(sampler.Extras, sampler.Extensions)
            });
        }

        return source;
    }

    private static string Pointer(GltfAnimationTarget target) =>
        target.Extensions != null && target.Extensions.TryGetValue("KHR_animation_pointer", out var extension)
        && extension.ValueKind == JsonValueKind.Object && extension.TryGetProperty("pointer", out var pointer)
            ? pointer.GetString()
            : null;

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
        if (node.Camera is not { } cameraIndex || (uint)cameraIndex >= (uint)file.Root.Cameras.Count)
        {
            return;
        }

        KeepCamera(file.Root.Cameras[cameraIndex], NodeId(index), node.Name ?? $"Camera {cameraIndex}", world);
    }

    private void KeepCamera(GltfCamera gltfCamera, string id, string name, Matrix4x4F world)
    {
        world.Decompose(out _, out var rotation, out var translation);
        var camera = new SceneData.Camera
        {
            ID = id,
            Name = gltfCamera.Name ?? name,
            Rotation = rotation,
            Translation = translation,
            Extra = ExtraOf(gltfCamera.Extras, gltfCamera.Extensions)
        };

        if (gltfCamera.Type == "orthographic" && gltfCamera.Orthographic is { } ortho)
        {
            camera.ProjectionType = CameraProjectionType.Orthogonal;
            camera.ZNear = ortho.ZNear;
            camera.ZFar = ortho.ZFar;
            camera.XMag = ortho.XMag;
            camera.YMag = ortho.YMag;
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
            Color = color,
            Intensity = intensity,
            Range = gltfLight.TryGetProperty("range", out var rangeElement) ? rangeElement.GetSingle() : 0,
            Position = translation,
            Rotation = rotation,
            Scale = scale,
            ConstantAttenuation = 1,
            QuadraticAttenuation = 1,
            Extra = ExtraOf(gltfLight)
        };

        if (type == "spot" && gltfLight.TryGetProperty("spot", out var spot))
        {
            var outer = spot.TryGetProperty("outerConeAngle", out var outerElement) ? outerElement.GetSingle() : MathF.PI / 4;
            var inner = spot.TryGetProperty("innerConeAngle", out var innerElement) ? innerElement.GetSingle() : 0;
            light.FalloffAngle = MathHelper.RadiansToDegrees(outer);
            light.InnerConeAngle = MathHelper.RadiansToDegrees(inner);
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

        if (primitive.Extensions != null)
        {
            foreach (var extension in primitive.Extensions.Keys)
            {
                if (extension != DracoExtension)
                {
                    Tell($"the primitive extension {extension} is kept as the file has it, not used");
                }
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
        var mesh = new Mesh(topology) { Name = name, Extra = ExtraOf(primitive.Extras, primitive.Extensions) };
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
                Tell("a vertex's joints past its first four are kept, not used");
            }
        }

        if (primitive.Attributes.TryGetValue("TANGENT", out var tangents))
        {
            mesh.SetTangents(Vectors4F(tangents));
        }

        foreach (var (attribute, accessor) in primitive.Attributes)
        {
            if (!DrawnAttributes.Contains(attribute))
            {
                mesh.SetAttribute(attribute, Attribute(accessor));
            }
        }

        if (primitive.Targets is { Count: > 0 })
        {
            mesh.SetMorphTargets(MorphTargets(gltfMesh, primitive));
            mesh.MorphWeights = gltfMesh.Weights is { } meshWeights ? (float[])meshWeights.Clone() : new float[primitive.Targets.Count];
            Tell("morph targets are kept, not drawn");
        }

        if (primitive.Material is { } material && material < file.Root.Materials.Count)
        {
            mesh.MaterialID = MaterialId(material);
        }

        ReadVariantMaterials(primitive, mesh);
        return mesh;
    }

    private void ReadVariants()
    {
        if (file.Root.Extensions == null
            || !file.Root.Extensions.TryGetValue("KHR_materials_variants", out var extension)
            || !extension.TryGetProperty("variants", out var variants) || variants.ValueKind != JsonValueKind.Array)
        {
            return;
        }

        var i = 0;
        foreach (var variant in variants.EnumerateArray())
        {
            SceneDataContainer.MaterialVariants.Add(variant.TryGetProperty("name", out var name) ? name.GetString() : $"Variant {i}");
            i++;
        }
    }

    private void ReadVariantMaterials(GltfPrimitive primitive, Mesh mesh)
    {
        if (primitive.Extensions == null
            || !primitive.Extensions.TryGetValue("KHR_materials_variants", out var extension)
            || !extension.TryGetProperty("mappings", out var mappings) || mappings.ValueKind != JsonValueKind.Array)
        {
            return;
        }

        foreach (var mapping in mappings.EnumerateArray())
        {
            if (!mapping.TryGetProperty("material", out var material) || !material.TryGetInt32(out var materialIndex)
                || !mapping.TryGetProperty("variants", out var variants) || variants.ValueKind != JsonValueKind.Array)
            {
                continue;
            }

            foreach (var variant in variants.EnumerateArray())
            {
                if (variant.TryGetInt32(out var v) && (uint)v < (uint)SceneDataContainer.MaterialVariants.Count)
                {
                    mesh.VariantMaterials[v] = MaterialId(materialIndex);
                }
            }
        }
    }

    private void ReadSamplers()
    {
        var samplers = file.Root.Samplers;
        for (int i = 0; i < samplers.Count; i++)
        {
            var sampler = samplers[i];
            var (min, mip) = sampler.MinFilter switch
            {
                9728 => (TextureFilter.Nearest, (TextureFilter?)null),
                9729 => (TextureFilter.Linear, null),
                9984 => (TextureFilter.Nearest, TextureFilter.Nearest),
                9985 => (TextureFilter.Linear, TextureFilter.Nearest),
                9986 => (TextureFilter.Nearest, TextureFilter.Linear),
                9987 => (TextureFilter.Linear, TextureFilter.Linear),
                _ => ((TextureFilter?)null, (TextureFilter?)null)
            };
            SceneDataContainer.Samplers[SamplerId(i)] = new SceneData.Sampler
            {
                ID = SamplerId(i),
                Name = sampler.Name,
                MagFilter = sampler.MagFilter switch
                {
                    9728 => TextureFilter.Nearest,
                    9729 => TextureFilter.Linear,
                    _ => null
                },
                MinFilter = min,
                MipFilter = mip,
                WrapS = Wrap(sampler.WrapS),
                WrapT = Wrap(sampler.WrapT),
                Extra = ExtraOf(sampler.Extras, sampler.Extensions)
            };
        }

        static TextureWrap Wrap(int mode) => mode switch
        {
            33071 => TextureWrap.ClampToEdge,
            33648 => TextureWrap.MirroredRepeat,
            _ => TextureWrap.Repeat
        };
    }

    private void ReadTextures()
    {
        var textures = file.Root.Textures;
        for (int i = 0; i < textures.Count; i++)
        {
            var texture = textures[i];
            var kept = new SceneData.Texture
            {
                ID = TextureId(i),
                Name = texture.Name,
                Image = texture.Source is { } source && source < file.Root.Images.Count ? ImageId(source) : null,
                Sampler = texture.Sampler is { } sampler && sampler < file.Root.Samplers.Count ? SamplerId(sampler) : null,
                Extra = ExtraOf(texture.Extras, texture.Extensions)
            };

            if (texture.Extensions != null)
            {
                foreach (var (name, extension) in texture.Extensions)
                {
                    if (extension.ValueKind == JsonValueKind.Object && extension.TryGetProperty("source", out var other)
                        && other.TryGetInt32(out var image) && (uint)image < (uint)file.Root.Images.Count)
                    {
                        kept.Sources[name] = ImageId(image);
                    }
                }
            }

            SceneDataContainer.Textures[kept.ID] = kept;
        }
    }

    /// <summary>The key a sampler takes in <see cref="SceneData.Samplers"/>.</summary>
    public static string SamplerId(int index) => $"sampler{index}";

    /// <summary>The key a texture takes in <see cref="SceneData.Textures"/>.</summary>
    public static string TextureId(int index) => $"texture{index}";

    private VertexAttribute Attribute(int accessor)
    {
        var values = GltfAccessors.ReadFloats(file, accessor, out var components);
        return new VertexAttribute { Components = components, Values = values };
    }

    // A primitive's morph targets: how far each moves its vertices, named as the mesh's targetNames say.
    private List<MorphTarget> MorphTargets(GltfMesh gltfMesh, GltfPrimitive primitive)
    {
        var names = gltfMesh.Extras is { ValueKind: JsonValueKind.Object } extras
                    && extras.TryGetProperty("targetNames", out var list) && list.ValueKind == JsonValueKind.Array
            ? list.EnumerateArray().Select(n => n.ValueKind == JsonValueKind.String ? n.GetString() : null).ToList()
            : [];
        var targets = new List<MorphTarget>(primitive.Targets.Count);
        for (int t = 0; t < primitive.Targets.Count; t++)
        {
            var target = new MorphTarget { Name = t < names.Count ? names[t] : null };
            foreach (var (attribute, accessor) in primitive.Targets[t])
            {
                switch (attribute)
                {
                    case "POSITION":
                        target.Positions = Vectors3F(accessor);
                        break;
                    case "NORMAL":
                        target.Normals = Vectors3F(accessor);
                        break;
                    case "TANGENT":
                        target.Tangents = Vectors3F(accessor);
                        break;
                    default:
                        target.Attributes[attribute] = Attribute(accessor);
                        break;
                }
            }

            targets.Add(target);
        }

        return targets;
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
                MimeType = gltfImage.MimeType ?? DataUriType(gltfImage.Uri),
                Name = gltfImage.Name,
                Extra = ExtraOf(gltfImage.Extras, gltfImage.Extensions)
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

    // "data:image/png;base64,..." says its type before the first ';' or ','.
    private static string DataUriType(string uri)
    {
        if (uri == null || !uri.StartsWith("data:", StringComparison.Ordinal))
        {
            return null;
        }

        var end = uri.IndexOfAny([';', ','], 5);
        return end > 5 ? uri[5..end] : null;
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
            var emissiveFactor = gltfMaterial.EmissiveFactor is { Length: 3 } e ? new Vector3F(e[0], e[1], e[2]) : Vector3F.Zero;
            var emissiveStrength = EmissiveStrength(gltfMaterial);
            var emissive = emissiveFactor * emissiveStrength;
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
                Shininess = 1,
                Name = gltfMaterial.Name,
                BaseColorTexture = Reference(pbr?.BaseColorTexture),
                MetallicRoughnessTexture = Reference(pbr?.MetallicRoughnessTexture),
                NormalTexture = Reference(gltfMaterial.NormalTexture),
                OcclusionTexture = Reference(gltfMaterial.OcclusionTexture),
                EmissiveTexture = Reference(gltfMaterial.EmissiveTexture),
                EmissiveFactor = emissiveFactor,
                EmissiveStrength = emissiveStrength,
                Extra = ExtraOf(gltfMaterial.Extras, gltfMaterial.Extensions),
                PbrExtra = pbr == null ? null : ExtraOf(pbr.Extras, pbr.Extensions)
            };

            if (gltfMaterial.Extensions != null)
            {
                foreach (var (name, extension) in gltfMaterial.Extensions)
                {
                    if (extension.ValueKind == JsonValueKind.Object)
                    {
                        ReadLayer(material, name, extension);
                    }

                    if (name != "KHR_materials_emissive_strength")
                    {
                        Tell($"the material extension {name} is kept, not drawn");
                    }
                }
            }

            SceneDataContainer.Materials[material.ID] = material;
        }
    }

    // A material extension's values on the material; one this reader does not know stays in its ExtraData alone.
    private void ReadLayer(SceneData.Material material, string name, JsonElement e)
    {
        switch (name)
        {
            case "KHR_materials_ior":
                material.Ior = Number(e, "ior", 1.5f);
                break;
            case "KHR_materials_dispersion":
                material.Dispersion = Number(e, "dispersion", 0);
                break;
            case "KHR_materials_unlit":
                material.Unlit = true;
                break;
            case "KHR_materials_clearcoat":
                material.Clearcoat = new ClearcoatLayer
                {
                    Factor = Number(e, "clearcoatFactor", 0),
                    Texture = Reference(e, "clearcoatTexture"),
                    RoughnessFactor = Number(e, "clearcoatRoughnessFactor", 0),
                    RoughnessTexture = Reference(e, "clearcoatRoughnessTexture"),
                    NormalTexture = Reference(e, "clearcoatNormalTexture"),
                    NormalScale = e.TryGetProperty("clearcoatNormalTexture", out var normal) ? Number(normal, "scale", 1) : 1
                };
                break;
            case "KHR_materials_sheen":
                material.Sheen = new SheenLayer
                {
                    ColorFactor = Vector3(e, "sheenColorFactor", Vector3F.Zero),
                    ColorTexture = Reference(e, "sheenColorTexture"),
                    RoughnessFactor = Number(e, "sheenRoughnessFactor", 0),
                    RoughnessTexture = Reference(e, "sheenRoughnessTexture")
                };
                break;
            case "KHR_materials_transmission":
                material.Transmission = new TransmissionLayer
                {
                    Factor = Number(e, "transmissionFactor", 0),
                    Texture = Reference(e, "transmissionTexture")
                };
                break;
            case "KHR_materials_diffuse_transmission":
                material.DiffuseTransmission = new DiffuseTransmissionLayer
                {
                    Factor = Number(e, "diffuseTransmissionFactor", 0),
                    Texture = Reference(e, "diffuseTransmissionTexture"),
                    ColorFactor = Vector3(e, "diffuseTransmissionColorFactor", Vector3F.One),
                    ColorTexture = Reference(e, "diffuseTransmissionColorTexture")
                };
                break;
            case "KHR_materials_volume":
                material.Volume = new VolumeLayer
                {
                    ThicknessFactor = Number(e, "thicknessFactor", 0),
                    ThicknessTexture = Reference(e, "thicknessTexture"),
                    AttenuationDistance = Number(e, "attenuationDistance", float.PositiveInfinity),
                    AttenuationColor = Vector3(e, "attenuationColor", Vector3F.One)
                };
                break;
            case "KHR_materials_specular":
                material.Specular = new SpecularLayer
                {
                    Factor = Number(e, "specularFactor", 1),
                    Texture = Reference(e, "specularTexture"),
                    ColorFactor = Vector3(e, "specularColorFactor", Vector3F.One),
                    ColorTexture = Reference(e, "specularColorTexture")
                };
                break;
            case "KHR_materials_iridescence":
                material.Iridescence = new IridescenceLayer
                {
                    Factor = Number(e, "iridescenceFactor", 0),
                    Texture = Reference(e, "iridescenceTexture"),
                    Ior = Number(e, "iridescenceIor", 1.3f),
                    ThicknessMinimum = Number(e, "iridescenceThicknessMinimum", 100),
                    ThicknessMaximum = Number(e, "iridescenceThicknessMaximum", 400),
                    ThicknessTexture = Reference(e, "iridescenceThicknessTexture")
                };
                break;
            case "KHR_materials_anisotropy":
                material.Anisotropy = new AnisotropyLayer
                {
                    Strength = Number(e, "anisotropyStrength", 0),
                    Rotation = Number(e, "anisotropyRotation", 0),
                    Texture = Reference(e, "anisotropyTexture")
                };
                break;
            case "KHR_materials_pbrSpecularGlossiness":
                material.SpecularGlossiness = new SpecularGlossiness
                {
                    DiffuseFactor = e.TryGetProperty("diffuseFactor", out var diffuse) && diffuse.ValueKind == JsonValueKind.Array
                                    && diffuse.GetArrayLength() == 4
                        ? new Vector4F(diffuse[0].GetSingle(), diffuse[1].GetSingle(), diffuse[2].GetSingle(), diffuse[3].GetSingle())
                        : Vector4F.One,
                    DiffuseTexture = Reference(e, "diffuseTexture"),
                    SpecularFactor = Vector3(e, "specularFactor", Vector3F.One),
                    GlossinessFactor = Number(e, "glossinessFactor", 1),
                    SpecularGlossinessTexture = Reference(e, "specularGlossinessTexture")
                };
                break;
        }
    }

    private static float Number(JsonElement element, string name, float otherwise) =>
        element.TryGetProperty(name, out var value) && value.TryGetSingle(out var number) ? number : otherwise;

    private static Vector3F Vector3(JsonElement element, string name, Vector3F otherwise) =>
        element.TryGetProperty(name, out var value) && value.ValueKind == JsonValueKind.Array && value.GetArrayLength() == 3
            ? new Vector3F(value[0].GetSingle(), value[1].GetSingle(), value[2].GetSingle())
            : otherwise;

    // A material's reference to a texture as the file makes it; null when there is none.
    private TextureReference Reference(GltfTextureInfo info)
    {
        if (info == null || (uint)info.Index >= (uint)file.Root.Textures.Count)
        {
            return null;
        }

        return new TextureReference
        {
            Texture = TextureId(info.Index),
            TexCoord = info.TexCoord,
            Transform = info.Extensions != null && info.Extensions.TryGetValue("KHR_texture_transform", out var transform)
                ? KeptTransform(transform)
                : null,
            Extra = ExtraOf(info.Extras, info.Extensions)
        };
    }

    private TextureReference Reference(JsonElement owner, string name)
    {
        if (!owner.TryGetProperty(name, out var info) || info.ValueKind != JsonValueKind.Object
            || !info.TryGetProperty("index", out var indexElement) || !indexElement.TryGetInt32(out var index)
            || (uint)index >= (uint)file.Root.Textures.Count)
        {
            return null;
        }

        var reference = new TextureReference
        {
            Texture = TextureId(index),
            TexCoord = info.TryGetProperty("texCoord", out var set) && set.TryGetInt32(out var texCoord) ? texCoord : 0,
            Extra = ExtraOf(info)
        };
        if (info.TryGetProperty("extensions", out var extensions) && extensions.ValueKind == JsonValueKind.Object
            && extensions.TryGetProperty("KHR_texture_transform", out var transform))
        {
            reference.Transform = KeptTransform(transform);
        }

        return reference;
    }

    private static TextureTransform KeptTransform(JsonElement extension)
    {
        if (extension.ValueKind != JsonValueKind.Object)
        {
            return null;
        }

        return new TextureTransform
        {
            Offset = Pair(extension, "offset", Vector2F.Zero),
            Rotation = Number(extension, "rotation", 0),
            Scale = Pair(extension, "scale", Vector2F.One),
            TexCoord = extension.TryGetProperty("texCoord", out var set) && set.TryGetInt32(out var texCoord) ? texCoord : null
        };
    }

    private static Vector2F Pair(JsonElement element, string name, Vector2F otherwise) =>
        element.TryGetProperty(name, out var pair) && pair.ValueKind == JsonValueKind.Array && pair.GetArrayLength() == 2
            ? new Vector2F(pair[0].GetSingle(), pair[1].GetSingle())
            : otherwise;

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
            Tell("a texture only in a format by extension (KTX2, WebP, AVIF) is kept, not drawn");
            return null;
        }

        return ImageId(source);
    }

    /// <summary>The key a material takes in <see cref="SceneData.Materials"/>.</summary>
    public static string MaterialId(int index) => $"material{index}";

    /// <summary>The key an image takes in <see cref="SceneData.Images"/>.</summary>
    public static string ImageId(int index) => $"image{index}";

    // An object's own data and extensions as JSON text; null when it has neither.
    private static ExtraData ExtraOf(JsonElement? extras, Dictionary<string, JsonElement> extensions)
    {
        var data = extras is { ValueKind: not (JsonValueKind.Undefined or JsonValueKind.Null) } some ? some.GetRawText() : null;
        if (data == null && extensions is not { Count: > 0 })
        {
            return null;
        }

        var extra = new ExtraData { Data = data };
        if (extensions != null)
        {
            foreach (var (name, json) in extensions)
            {
                extra.Extensions[name] = json.GetRawText();
            }
        }

        return extra;
    }

    // The same, of an object an extension holds, such as a light.
    private static ExtraData ExtraOf(JsonElement element)
    {
        var extras = element.TryGetProperty("extras", out var data) ? data : (JsonElement?)null;
        Dictionary<string, JsonElement> extensions = null;
        if (element.TryGetProperty("extensions", out var list) && list.ValueKind == JsonValueKind.Object)
        {
            extensions = new Dictionary<string, JsonElement>();
            foreach (var property in list.EnumerateObject())
            {
                extensions[property.Name] = property.Value;
            }
        }

        return ExtraOf(extras, extensions);
    }

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

    private Vector4F[] Vectors4F(int accessor)
    {
        var values = Floats(accessor, 4);
        var vectors = new Vector4F[values.Length / 4];
        for (int i = 0; i < vectors.Length; i++)
        {
            vectors[i] = new Vector4F(values[i * 4], values[i * 4 + 1], values[i * 4 + 2], values[i * 4 + 3]);
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
    private Vector2F[] TextureCoordinates(int accessor, UvTransform? transform)
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

    private readonly record struct UvTransform(Vector2F Offset, float Cos, float Sin, Vector2F Scale, int TexCoord);

    /// <summary>The KHR_texture_transform of a material's base color texture, the one drawn; the texture coordinates it
    /// reads take it. Another texture of the material transformed otherwise is told, as those coordinates hold one.</summary>
    private UvTransform? BaseColorTransform(int? materialIndex)
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

    private static UvTransform? TransformOf(GltfTextureInfo info)
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
        return new UvTransform(offset, MathF.Cos(rotation), MathF.Sin(rotation), scale, texCoord);
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
