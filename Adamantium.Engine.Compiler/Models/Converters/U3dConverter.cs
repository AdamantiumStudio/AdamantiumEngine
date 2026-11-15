using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Text.Json;
using Adamantium.Engine.Compiler.Converter.Configs;
using Adamantium.Engine.Compiler.Converter.Converters;
using Adamantium.Engine.Compiler.Models.U3d;
using Adamantium.Graphics.Core;
using Adamantium.Graphics.Core.Models;
using Adamantium.Mathematics;

namespace Adamantium.Engine.Compiler.Models.Converters;

/// <summary>Imports a U3D file (ECMA-363, 4th edition) - a <c>.u3d</c> file, or the 3D artwork of a PDF read from its
/// bytes. The node graph becomes the model tree from the world down, a node with several parents copied under each,
/// and nodes outside the world go under <see cref="SceneData.OtherNodes"/>; a model node gets the meshes of its model
/// resource - a CLOD mesh at the full resolution its blocks give, a point set or a line set - one mesh a shading ID,
/// drawn by the first shader of that ID's shader list. A lit texture shader becomes a material of its name: its
/// material resource's colours and opacity, its reflectivity as the specular exponent (0 to 128) the spec makes of it,
/// its first texture layer as the diffuse map, the texture's PNG or JPEG kept as the image's bytes. The default
/// material and any the file names without defining are lit as the spec's default ambient grey, 0.75, as diffuse.
/// Light and view nodes become lights and cameras. U3D is Z up and right-handed; its texture coordinates start at the
/// image's top left, so v is read turned over, as glTF's.</summary>
public class U3dConverter : ConverterBase
{
    private const string OtherNodesId = "otherNodes";
    private const string SpecularAttribute = "SPECULAR_COLOR";
    private const int MaximumNodeAppearances = 1_000_000;
    private const int DrawnTextureLayers = 4;
    private const float DefaultGrey = 0.75f;
    private const uint MeshShading = 0x1;
    private const uint LineShading = 0x2;
    private const uint PointShading = 0x4;

    private readonly HashSet<string> told = new(StringComparer.Ordinal);
    private readonly Dictionary<string, int> appearancesOf = new(StringComparer.Ordinal);
    private U3dFile file;
    private int appearances;

    public U3dConverter(string filePath, ConversionConfig config) : base(filePath, config, UpAxis.Z_UP)
    {
    }

    /// <summary>A converter of a U3D model held in memory, known by <paramref name="fileName"/>.</summary>
    public U3dConverter(string fileName, byte[] bytes, ConversionConfig config) : base(fileName, bytes, config, UpAxis.Z_UP)
    {
        ArgumentNullException.ThrowIfNull(bytes);
    }

    protected override void Convert()
    {
        file = U3dFile.Read(ReadFileBytes(), CancellationToken);
        foreach (var note in file.Notes)
        {
            Tell(note);
        }

        if (file.UnitsScalingFactor is { } units)
        {
            SceneDataContainer.Units = new SceneData.Unit(UnitType.Meter, (float)units);
        }

        ReadTextures();
        SceneDataContainer.Models = SceneDataContainer.CreateMesh(null, "", Path.GetFileNameWithoutExtension(FileName));
        BuildTree();
    }

    private void Tell(string feature)
    {
        if (told.Add(feature))
        {
            UnsupportedFeatures.Add(feature);
        }
    }

    private void BuildTree()
    {
        var byName = file.Nodes.ToDictionary(node => node.Name, StringComparer.Ordinal);
        var children = new Dictionary<string, List<U3dChildLink>>(StringComparer.Ordinal);
        var parentsInFile = new Dictionary<string, int>(StringComparer.Ordinal);
        var orphans = new List<U3dNode>();
        foreach (var node in file.Nodes)
        {
            var linked = 0;
            parentsInFile[node.Name] = 0;
            foreach (var parent in node.Parents)
            {
                if (parent.Name.Length > 0 && !byName.ContainsKey(parent.Name))
                {
                    Tell($"node {node.Name} names parent {parent.Name}, which the file does not hold");
                    continue;
                }

                if (!children.TryGetValue(parent.Name, out var list))
                {
                    list = [];
                    children.Add(parent.Name, list);
                }

                list.Add(new U3dChildLink(node, parent.Transform));
                linked++;
                if (parent.Name.Length > 0)
                {
                    parentsInFile[node.Name]++;
                }
            }

            if (linked == 0)
            {
                orphans.Add(node);
            }
        }

        var acyclic = Acyclic(children, parentsInFile);
        foreach (var node in file.Nodes)
        {
            if (!acyclic.Contains(node.Name))
            {
                Tell($"node {node.Name} is its own ancestor, or hangs under one - the node graph has a cycle - and is left out");
            }
        }

        if (children.TryGetValue(string.Empty, out var worldChildren))
        {
            Expand(worldChildren, SceneDataContainer.Models, children, acyclic);
        }

        if (orphans.Count > 0)
        {
            SceneDataContainer.OtherNodes = SceneDataContainer.CreateMesh(null, OtherNodesId, "Other nodes");
            var roots = orphans.Select(node => new U3dChildLink(node, Matrix4x4F.Identity)).ToList();
            Expand(roots, SceneDataContainer.OtherNodes, children, acyclic);
        }
    }

    private static HashSet<string> Acyclic(Dictionary<string, List<U3dChildLink>> children, Dictionary<string, int> parentsInFile)
    {
        var remaining = new Dictionary<string, int>(parentsInFile, StringComparer.Ordinal);
        var ready = new Queue<string>(remaining.Where(entry => entry.Value == 0).Select(entry => entry.Key));
        var acyclic = new HashSet<string>(StringComparer.Ordinal);
        while (ready.Count > 0)
        {
            var name = ready.Dequeue();
            acyclic.Add(name);
            if (!children.TryGetValue(name, out var links))
            {
                continue;
            }

            foreach (var link in links)
            {
                if (--remaining[link.Node.Name] == 0)
                {
                    ready.Enqueue(link.Node.Name);
                }
            }
        }

        return acyclic;
    }

    private void Expand(List<U3dChildLink> roots, SceneData.Model under, Dictionary<string, List<U3dChildLink>> children,
        HashSet<string> acyclic)
    {
        var pending = new Stack<U3dAppearance>();
        for (var index = roots.Count - 1; index >= 0; index--)
        {
            pending.Push(new U3dAppearance(roots[index], under, Matrix4x4F.Identity));
        }

        while (pending.Count > 0)
        {
            CancellationToken.ThrowIfCancellationRequested();
            var appearance = pending.Pop();
            var node = appearance.Link.Node;
            if (!acyclic.Contains(node.Name))
            {
                continue;
            }

            if (++appearances > MaximumNodeAppearances)
            {
                Tell($"the node graph shows nodes more than {MaximumNodeAppearances} times over; the rest are left out");
                return;
            }

            var model = SceneDataContainer.CreateMesh(appearance.Parent, AppearanceId(node.Name), node.Name);
            model.Extra = ExtraOf(node.MetaData);
            var local = appearance.Link.Transform;
            local.Decompose(out var scale, out var rotation, out var translation);
            model.Scale = scale;
            model.Rotation = rotation;
            model.Position = translation;
            var world = local * appearance.ParentWorld;
            switch (node.Kind)
            {
                case U3dNodeKind.Model:
                    AddMeshes(model, node);
                    break;
                case U3dNodeKind.Light:
                    AddLight(model.ID, node, world);
                    break;
                case U3dNodeKind.View:
                    AddCamera(model.ID, node, world);
                    break;
            }

            if (children.TryGetValue(node.Name, out var links))
            {
                for (var index = links.Count - 1; index >= 0; index--)
                {
                    pending.Push(new U3dAppearance(links[index], model, world));
                }
            }
        }
    }

    private string AppearanceId(string name)
    {
        appearancesOf.TryGetValue(name, out var count);
        appearancesOf[name] = ++count;
        return count == 1 ? name : $"{name} ({count})";
    }

    private void AddMeshes(SceneData.Model model, U3dNode node)
    {
        if (node.Visibility == 0)
        {
            Tell($"model node {node.Name} is set not to be seen; its meshes are left out");
            return;
        }

        if (node.Visibility != 1)
        {
            Tell($"model node {node.Name} is to be seen from the back as well; it is drawn from the front alone");
        }

        if (!file.Geometries.TryGetValue(node.ResourceName, out var geometry))
        {
            Tell($"model node {node.Name} shows model resource {node.ResourceName}, which the file does not hold");
            return;
        }

        var bit = geometry.Kind switch
        {
            U3dGeometryKind.PointSet => PointShading,
            U3dGeometryKind.LineSet => LineShading,
            _ => MeshShading
        };
        var shading = ShadingOf(file.NodeShading, node.Name, bit) ?? ShadingOf(file.ResourceShading, geometry.Name, bit);
        if (geometry.Kind == U3dGeometryKind.Mesh && geometry.Primitives.Count == 0)
        {
            Tell($"mesh {geometry.Name} has no faces");
        }

        foreach (var group in geometry.Primitives.GroupBy(primitive => primitive.ShadingId).OrderBy(group => group.Key))
        {
            var mesh = BuildMesh(geometry, group.Key, group.ToList());
            mesh.MaterialID = MaterialFor(ShaderOf(shading, group.Key));
            model.Meshes.Add(mesh);
        }
    }

    private static U3dShadingModifier ShadingOf(Dictionary<string, U3dShadingModifier> modifiers, string name, uint bit) =>
        modifiers.TryGetValue(name, out var modifier) && (modifier.Attributes == 0 || (modifier.Attributes & bit) != 0)
            ? modifier
            : null;

    private string ShaderOf(U3dShadingModifier shading, int shadingId)
    {
        if (shading == null || shadingId >= shading.ShaderLists.Count || shading.ShaderLists[shadingId].Length == 0)
        {
            return string.Empty;
        }

        var list = shading.ShaderLists[shadingId];
        if (list.Length > 1)
        {
            Tell("a shader list of several shaders, drawn in passes, is drawn by its first shader");
        }

        return list[0];
    }

    private Mesh BuildMesh(U3dGeometry geometry, int shadingId, List<U3dPrimitive> primitives)
    {
        var description = geometry.Shadings[shadingId];
        var topology = geometry.Kind switch
        {
            U3dGeometryKind.PointSet => PrimitiveType.PointList,
            U3dGeometryKind.LineSet => PrimitiveType.LineList,
            _ => PrimitiveType.TriangleList
        };
        var layers = Math.Min(description.TextureLayerCount, DrawnTextureLayers);
        if (description.TextureLayerCount > DrawnTextureLayers)
        {
            Tell($"texture layers past the first {DrawnTextureLayers} of {geometry.Name} are left out");
        }

        var vertexOf = new Dictionary<U3dCorner, int>(new U3dCornerComparer());
        var points = new List<Vector3<double>>();
        var normals = new List<Vector3F>();
        var colors = new List<Color>();
        var specular = new List<float>();
        var uvs = new List<Vector2F>[layers];
        for (var layer = 0; layer < layers; layer++)
        {
            uvs[layer] = [];
        }

        var hasNormals = true;
        var hasColors = description.HasDiffuseColors;
        var hasSpecular = description.HasSpecularColors;
        var indices = new List<int>();
        foreach (var primitive in primitives)
        {
            foreach (var corner in primitive.Corners)
            {
                if (!vertexOf.TryGetValue(corner, out var vertex))
                {
                    vertex = points.Count;
                    vertexOf.Add(corner, vertex);
                    var position = geometry.Positions[corner.Position];
                    points.Add(new Vector3<double>(position.X, position.Y, position.Z));
                    hasNormals &= corner.Normal >= 0 && corner.Normal < geometry.Normals.Count;
                    normals.Add(hasNormals ? geometry.Normals[corner.Normal] : Vector3F.Zero);
                    hasColors &= corner.Diffuse >= 0 && corner.Diffuse < geometry.DiffuseColors.Count;
                    colors.Add(hasColors ? new Color(geometry.DiffuseColors[corner.Diffuse]) : default);
                    hasSpecular &= corner.Specular >= 0 && corner.Specular < geometry.SpecularColors.Count;
                    var specularColor = hasSpecular ? geometry.SpecularColors[corner.Specular] : Vector4F.Zero;
                    specular.AddRange([specularColor.X, specularColor.Y, specularColor.Z, specularColor.W]);
                    for (var layer = 0; layer < layers; layer++)
                    {
                        var index = layer < corner.TextureCoordinates.Length ? corner.TextureCoordinates[layer] : -1;
                        var coordinate = index >= 0 && index < geometry.TextureCoordinates.Count
                            ? geometry.TextureCoordinates[index]
                            : Vector4F.Zero;
                        uvs[layer].Add(new Vector2F(coordinate.X, 1 - coordinate.Y));
                    }
                }

                indices.Add(vertex);
            }
        }

        var mesh = new Mesh(topology) { Name = $"{geometry.Name} {shadingId}" };
        mesh.SetPoints(points).SetIndices(indices);
        if (hasNormals && !geometry.ExcludesNormals)
        {
            mesh.SetNormals(normals);
        }

        if (hasColors)
        {
            mesh.SetColors(colors);
        }

        if (hasSpecular)
        {
            mesh.SetAttribute(SpecularAttribute, new VertexAttribute { Components = 4, Values = specular.ToArray() });
        }

        for (var layer = 0; layer < layers; layer++)
        {
            mesh.SetUVs(layer, uvs[layer]);
        }

        return mesh;
    }

    private string MaterialFor(string shaderName)
    {
        if (SceneDataContainer.Materials.ContainsKey(shaderName))
        {
            return shaderName;
        }

        file.Shaders.TryGetValue(shaderName, out var shader);
        if (shader == null && shaderName.Length > 0)
        {
            Tell($"shader {shaderName} is not in the file; what it draws is drawn by the default material");
        }

        U3dMaterial resource = null;
        if (shader != null && !file.Materials.TryGetValue(shader.MaterialName, out resource) && shader.MaterialName.Length > 0)
        {
            Tell($"material {shader.MaterialName} is not in the file; shader {shaderName} is drawn by the default one");
        }

        var material = resource == null ? DefaultMaterial() : MaterialOf(resource);
        material.ID = shaderName;
        material.Name = shaderName.Length == 0 ? "Default" : shaderName;
        if (shader != null)
        {
            ApplyShader(material, shader);
        }

        SceneDataContainer.Materials[shaderName] = material;
        return shaderName;
    }

    private static SceneData.Material DefaultMaterial() => new()
    {
        AmbientColor = new Vector4F(DefaultGrey, DefaultGrey, DefaultGrey, 1),
        DiffuseColor = new Vector4F(DefaultGrey, DefaultGrey, DefaultGrey, 1),
        SpecularColor = new Vector4F(0, 0, 0, 1),
        Emission = new Vector4F(0, 0, 0, 1),
        Shininess = 0,
        Transparency = 1,
        AlphaMode = AlphaMode.Opaque
    };

    private static SceneData.Material MaterialOf(U3dMaterial resource)
    {
        var flags = resource.Attributes;
        var ambient = (flags & U3dMaterial.AmbientFlag) != 0 ? resource.AmbientColor : Vector3F.Zero;
        var diffuse = (flags & U3dMaterial.DiffuseFlag) != 0 ? resource.DiffuseColor : Vector3F.Zero;
        var specular = (flags & U3dMaterial.SpecularFlag) != 0 ? resource.SpecularColor : Vector3F.Zero;
        var emissive = (flags & U3dMaterial.EmissiveFlag) != 0 ? resource.EmissiveColor : Vector3F.Zero;
        var reflectivity = (flags & U3dMaterial.ReflectivityFlag) != 0 ? Math.Clamp(resource.Reflectivity, 0, 1) : 0;
        var opacity = (flags & U3dMaterial.OpacityFlag) != 0 ? resource.Opacity : 1;
        return new SceneData.Material
        {
            AmbientColor = new Vector4F(ambient, 1),
            DiffuseColor = new Vector4F(diffuse, opacity),
            SpecularColor = new Vector4F(specular, 1),
            Emission = new Vector4F(emissive, 1),
            EmissiveFactor = emissive,
            Shininess = reflectivity * 128,
            Transparency = opacity,
            AlphaMode = opacity < 1 ? AlphaMode.Blend : AlphaMode.Opaque
        };
    }

    private void ApplyShader(SceneData.Material material, U3dShader shader)
    {
        material.Extra = ExtraOf(shader.MetaData);
        material.Unlit = (shader.Attributes & U3dShader.LightingEnabled) == 0;
        if ((shader.Attributes & U3dShader.AlphaTestEnabled) != 0)
        {
            if (shader.AlphaTestFunction is U3dShader.AlphaGreater or U3dShader.AlphaGreaterOrEqual)
            {
                material.AlphaMode = AlphaMode.Mask;
                material.AlphaCutoff = shader.AlphaTestReference;
            }
            else
            {
                Tell($"the alpha test 0x{shader.AlphaTestFunction:X} of shader {shader.Name} is not drawn");
            }
        }

        if (shader.ColorBlendFunction != U3dShader.AlphaBlend)
        {
            Tell($"shader {shader.Name} blends into the frame by function 0x{shader.ColorBlendFunction:X}; it is drawn alpha-blended");
        }

        if (shader.Layers.Count == 0)
        {
            return;
        }

        if (shader.Layers.Count > 1)
        {
            Tell($"texture layers past the first of shader {shader.Name} are left out");
        }

        if ((shader.ShaderChannels & 0x1) == 0)
        {
            Tell($"shader {shader.Name} lays its texture by a texture layer other than the first; the first is used");
        }

        var layer = shader.Layers[0];
        if (layer.TextureMode != 0)
        {
            Tell($"shader {shader.Name} makes its texture coordinates up (texture mode {layer.TextureMode}); the mesh's are used");
        }

        if (layer.TextureTransform != Matrix4x4F.Identity)
        {
            Tell($"the texture transform of shader {shader.Name} is not laid on");
        }

        if ((layer.Repeat & 0x3) != 0x3)
        {
            Tell($"shader {shader.Name} clamps its texture rather than tiling it; it is tiled");
        }

        if (SceneDataContainer.Images.ContainsKey(layer.TextureName))
        {
            material.DiffuseMap = layer.TextureName;
        }
        else
        {
            Tell($"shader {shader.Name} lays texture {layer.TextureName}, which the file does not hold");
        }
    }

    private void ReadTextures()
    {
        var directory = Path.GetDirectoryName(FilePath);
        foreach (var texture in file.Textures.Values)
        {
            if (texture.Images.Count == 0)
            {
                Tell($"texture {texture.Name} has no image");
                continue;
            }

            var colour = texture.Images.FirstOrDefault(image => (image.Channels & U3dTextureImage.ColorChannels) != 0)
                         ?? texture.Images[0];
            if (texture.Images.Count > 1)
            {
                Tell($"texture {texture.Name} is made of {texture.Images.Count} images; only its colour image is kept");
            }

            var kept = new SceneData.Image { ID = texture.Name, Name = texture.Name, Extra = ExtraOf(texture.MetaData) };
            if (colour.IsExternal)
            {
                if (colour.Urls.Count == 0)
                {
                    Tell($"texture {texture.Name} lies in a file it does not name");
                    continue;
                }

                kept.ImageName = colour.Urls[0];
                kept.FilePath = string.IsNullOrEmpty(directory) || Uri.IsWellFormedUriString(colour.Urls[0], UriKind.Absolute)
                    ? colour.Urls[0]
                    : Path.Combine(directory, colour.Urls[0]);
                if (!File.Exists(kept.FilePath))
                {
                    Tell($"the image {colour.Urls[0]} of texture {texture.Name} is not found");
                }
            }
            else
            {
                var data = colour.Chunks.SelectMany(chunk => chunk).ToArray();
                if (data.Length != colour.ByteCount)
                {
                    Tell($"texture {texture.Name} brings {data.Length} bytes of the {colour.ByteCount} its image has");
                }

                kept.Data = data;
                kept.MimeType = colour.CompressionType switch
                {
                    U3dTextureImage.Png => "image/png",
                    U3dTextureImage.Tiff => "image/tiff",
                    _ => "image/jpeg"
                };
            }

            SceneDataContainer.Images[kept.ID] = kept;
        }
    }

    private void AddLight(string id, U3dNode node, Matrix4x4F world)
    {
        if (!file.Lights.TryGetValue(node.ResourceName, out var resource))
        {
            if (node.ResourceName.Length > 0)
            {
                Tell($"light node {node.Name} shines light resource {node.ResourceName}, which the file does not hold");
            }

            resource = new U3dLightResource { Color = new Vector3F(DefaultGrey, DefaultGrey, DefaultGrey), Intensity = 1 };
        }

        if ((resource.Attributes & U3dLightResource.Enabled) == 0)
        {
            Tell($"light {resource.Name} is switched off in the file; it is kept as it is");
        }

        if (resource.Type > 3)
        {
            Tell($"light {resource.Name} is of type {resource.Type}, which is not known; it is taken for a point light");
        }

        world.Decompose(out var scale, out var rotation, out var translation);
        SceneDataContainer.LightData[id] = new SceneData.Light
        {
            ID = id,
            Name = node.Name,
            LightType = resource.Type switch
            {
                0 => LightType.Ambient,
                1 => LightType.Directional,
                3 => LightType.Spot,
                _ => LightType.Point
            },
            Color = resource.Color,
            Intensity = resource.Intensity,
            ConstantAttenuation = resource.ConstantAttenuation,
            LinearAttenuation = resource.LinearAttenuation,
            QuadraticAttenuation = resource.QuadraticAttenuation,
            FalloffAngle = resource.SpotAngle,
            Position = translation,
            Rotation = rotation,
            Scale = scale,
            Extra = ExtraOf(node.MetaData)
        };
    }

    private void AddCamera(string id, U3dNode node, Matrix4x4F world)
    {
        var view = node.View;
        if (file.Views.TryGetValue(node.ResourceName, out var resource))
        {
            if (resource.FogEnabled)
            {
                Tell($"the fog of view {resource.Name} is left out");
            }

            if (resource.PassCount > 1 || resource.RootNodeName.Length > 0)
            {
                Tell($"view {resource.Name} renders a part of the scene, or in passes; the camera sees all of it");
            }
        }

        if (view.BackdropCount > 0 || view.OverlayCount > 0)
        {
            Tell($"the backdrops and overlays of view node {node.Name} are left out");
        }

        world.Decompose(out _, out var rotation, out var translation);
        var aspect = view.PortHeight > 0 ? view.PortWidth / view.PortHeight : 0;
        var camera = new SceneData.Camera
        {
            ID = id,
            Name = node.Name,
            Rotation = rotation,
            Translation = translation,
            ZNear = view.NearClip,
            ZFar = view.FarClip,
            AspectRatio = aspect,
            ProjectionType = CameraProjectionType.Perspective,
            FovType = FovType.YFov,
            Extra = ExtraOf(node.MetaData)
        };

        switch (view.ProjectionMode)
        {
            case U3dViewNodeParameters.ThreePointPerspective:
                camera.Fov = view.FieldOfView;
                break;
            case U3dViewNodeParameters.Orthographic:
                camera.ProjectionType = CameraProjectionType.Orthogonal;
                camera.YMag = view.OrthographicHeight / 2;
                camera.XMag = camera.YMag * (aspect > 0 ? aspect : 1);
                break;
            default:
                Tell($"view node {node.Name} projects with one- or two-point perspective; it is kept without its field of view");
                break;
        }

        SceneDataContainer.CameraData[id] = camera;
    }

    private static ExtraData ExtraOf(IReadOnlyList<U3dMetaDataEntry> metaData)
    {
        if (metaData == null || metaData.Count == 0)
        {
            return null;
        }

        var values = new Dictionary<string, string>(StringComparer.Ordinal);
        foreach (var entry in metaData)
        {
            values[entry.Key] = entry.IsBinary ? System.Convert.ToBase64String(entry.BinaryValue) : entry.Value;
        }

        return new ExtraData { Data = JsonSerializer.Serialize(values) };
    }
}

/// <summary>A node as a child of one of its parents, with where it stands relative to that parent.</summary>
internal sealed class U3dChildLink
{
    internal U3dChildLink(U3dNode node, Matrix4x4F transform)
    {
        Node = node;
        Transform = transform;
    }

    internal U3dNode Node { get; }

    internal Matrix4x4F Transform { get; }
}

/// <summary>A node waiting to be placed under the model of its parent, with its parent's place in the world.</summary>
internal sealed class U3dAppearance
{
    internal U3dAppearance(U3dChildLink link, SceneData.Model parent, Matrix4x4F parentWorld)
    {
        Link = link;
        Parent = parent;
        ParentWorld = parentWorld;
    }

    internal U3dChildLink Link { get; }

    internal SceneData.Model Parent { get; }

    internal Matrix4x4F ParentWorld { get; }
}

/// <summary>Corners alike in every index are one vertex.</summary>
internal sealed class U3dCornerComparer : IEqualityComparer<U3dCorner>
{
    public bool Equals(U3dCorner left, U3dCorner right)
    {
        if (ReferenceEquals(left, right))
        {
            return true;
        }

        return left != null && right != null && left.Position == right.Position && left.Normal == right.Normal &&
               left.Diffuse == right.Diffuse && left.Specular == right.Specular &&
               left.TextureCoordinates.AsSpan().SequenceEqual(right.TextureCoordinates);
    }

    public int GetHashCode(U3dCorner corner)
    {
        var hash = new HashCode();
        hash.Add(corner.Position);
        hash.Add(corner.Normal);
        hash.Add(corner.Diffuse);
        hash.Add(corner.Specular);
        foreach (var coordinate in corner.TextureCoordinates)
        {
            hash.Add(coordinate);
        }

        return hash.ToHashCode();
    }
}
