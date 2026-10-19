using System.Collections.Generic;
using System.Text.Json;
using System.Text.Json.Serialization;

namespace Adamantium.Engine.Compiler.Models.Gltf;

/// <summary>The JSON of a glTF 2.0 asset, as the specification names it: what is read is what the file says, and
/// the converter makes engine data of it.</summary>
public sealed class GltfRoot
{
    [JsonPropertyName("asset")] public GltfAsset Asset { get; set; }
    [JsonPropertyName("scene")] public int? Scene { get; set; }
    [JsonPropertyName("scenes")] public List<GltfScene> Scenes { get; set; } = [];
    [JsonPropertyName("nodes")] public List<GltfNode> Nodes { get; set; } = [];
    [JsonPropertyName("meshes")] public List<GltfMesh> Meshes { get; set; } = [];
    [JsonPropertyName("accessors")] public List<GltfAccessor> Accessors { get; set; } = [];
    [JsonPropertyName("bufferViews")] public List<GltfBufferView> BufferViews { get; set; } = [];
    [JsonPropertyName("buffers")] public List<GltfBuffer> Buffers { get; set; } = [];
    [JsonPropertyName("materials")] public List<GltfMaterial> Materials { get; set; } = [];
    [JsonPropertyName("textures")] public List<GltfTexture> Textures { get; set; } = [];
    [JsonPropertyName("images")] public List<GltfImage> Images { get; set; } = [];
    [JsonPropertyName("samplers")] public List<GltfSampler> Samplers { get; set; } = [];
    [JsonPropertyName("skins")] public List<GltfSkin> Skins { get; set; } = [];
    [JsonPropertyName("animations")] public List<GltfAnimation> Animations { get; set; } = [];
    [JsonPropertyName("cameras")] public List<GltfCamera> Cameras { get; set; } = [];
    [JsonPropertyName("extensionsUsed")] public List<string> ExtensionsUsed { get; set; } = [];
    [JsonPropertyName("extensionsRequired")] public List<string> ExtensionsRequired { get; set; } = [];
    [JsonPropertyName("extensions")] public Dictionary<string, JsonElement> Extensions { get; set; }
    [JsonPropertyName("extras")] public JsonElement? Extras { get; set; }
}

public sealed class GltfAsset
{
    [JsonPropertyName("version")] public string Version { get; set; }
    [JsonPropertyName("minVersion")] public string MinVersion { get; set; }
    [JsonPropertyName("generator")] public string Generator { get; set; }
    [JsonPropertyName("copyright")] public string Copyright { get; set; }
    [JsonPropertyName("extensions")] public Dictionary<string, JsonElement> Extensions { get; set; }
    [JsonPropertyName("extras")] public JsonElement? Extras { get; set; }
}

public sealed class GltfScene
{
    [JsonPropertyName("name")] public string Name { get; set; }
    [JsonPropertyName("nodes")] public List<int> Nodes { get; set; } = [];
    [JsonPropertyName("extensions")] public Dictionary<string, JsonElement> Extensions { get; set; }
    [JsonPropertyName("extras")] public JsonElement? Extras { get; set; }
}

public sealed class GltfNode
{
    [JsonPropertyName("name")] public string Name { get; set; }
    [JsonPropertyName("children")] public List<int> Children { get; set; } = [];
    [JsonPropertyName("mesh")] public int? Mesh { get; set; }
    [JsonPropertyName("skin")] public int? Skin { get; set; }
    [JsonPropertyName("camera")] public int? Camera { get; set; }
    [JsonPropertyName("matrix")] public float[] Matrix { get; set; }
    [JsonPropertyName("translation")] public float[] Translation { get; set; }
    [JsonPropertyName("rotation")] public float[] Rotation { get; set; }
    [JsonPropertyName("scale")] public float[] Scale { get; set; }
    [JsonPropertyName("weights")] public float[] Weights { get; set; }
    [JsonPropertyName("extensions")] public Dictionary<string, JsonElement> Extensions { get; set; }
    [JsonPropertyName("extras")] public JsonElement? Extras { get; set; }
}

public sealed class GltfMesh
{
    [JsonPropertyName("name")] public string Name { get; set; }
    [JsonPropertyName("primitives")] public List<GltfPrimitive> Primitives { get; set; } = [];
    [JsonPropertyName("weights")] public float[] Weights { get; set; }
    [JsonPropertyName("extensions")] public Dictionary<string, JsonElement> Extensions { get; set; }
    [JsonPropertyName("extras")] public JsonElement? Extras { get; set; }
}

public sealed class GltfPrimitive
{
    [JsonPropertyName("attributes")] public Dictionary<string, int> Attributes { get; set; } = [];
    [JsonPropertyName("indices")] public int? Indices { get; set; }
    [JsonPropertyName("material")] public int? Material { get; set; }
    [JsonPropertyName("mode")] public int Mode { get; set; } = GltfPrimitiveMode.Triangles;
    [JsonPropertyName("targets")] public List<Dictionary<string, int>> Targets { get; set; }
    [JsonPropertyName("extensions")] public Dictionary<string, JsonElement> Extensions { get; set; }
    [JsonPropertyName("extras")] public JsonElement? Extras { get; set; }
}

public static class GltfPrimitiveMode
{
    public const int Points = 0;
    public const int Lines = 1;
    public const int LineLoop = 2;
    public const int LineStrip = 3;
    public const int Triangles = 4;
    public const int TriangleStrip = 5;
    public const int TriangleFan = 6;
}

public sealed class GltfAccessor
{
    [JsonPropertyName("bufferView")] public int? BufferView { get; set; }
    [JsonPropertyName("byteOffset")] public int ByteOffset { get; set; }
    [JsonPropertyName("componentType")] public int ComponentType { get; set; }
    [JsonPropertyName("normalized")] public bool Normalized { get; set; }
    [JsonPropertyName("count")] public int Count { get; set; }
    [JsonPropertyName("type")] public string Type { get; set; }
    [JsonPropertyName("min")] public float[] Min { get; set; }
    [JsonPropertyName("max")] public float[] Max { get; set; }
    [JsonPropertyName("sparse")] public GltfSparse Sparse { get; set; }
}

public static class GltfComponentType
{
    public const int Byte = 5120;
    public const int UnsignedByte = 5121;
    public const int Short = 5122;
    public const int UnsignedShort = 5123;
    public const int UnsignedInt = 5125;
    public const int Float = 5126;
}

public sealed class GltfSparse
{
    [JsonPropertyName("count")] public int Count { get; set; }
    [JsonPropertyName("indices")] public GltfSparseIndices Indices { get; set; }
    [JsonPropertyName("values")] public GltfSparseValues Values { get; set; }
}

public sealed class GltfSparseIndices
{
    [JsonPropertyName("bufferView")] public int BufferView { get; set; }
    [JsonPropertyName("byteOffset")] public int ByteOffset { get; set; }
    [JsonPropertyName("componentType")] public int ComponentType { get; set; }
}

public sealed class GltfSparseValues
{
    [JsonPropertyName("bufferView")] public int BufferView { get; set; }
    [JsonPropertyName("byteOffset")] public int ByteOffset { get; set; }
}

public sealed class GltfBufferView
{
    [JsonPropertyName("buffer")] public int Buffer { get; set; }
    [JsonPropertyName("byteOffset")] public int ByteOffset { get; set; }
    [JsonPropertyName("byteLength")] public int ByteLength { get; set; }
    [JsonPropertyName("byteStride")] public int? ByteStride { get; set; }
    [JsonPropertyName("extensions")] public Dictionary<string, JsonElement> Extensions { get; set; }
}

public sealed class GltfBuffer
{
    [JsonPropertyName("uri")] public string Uri { get; set; }
    [JsonPropertyName("byteLength")] public int ByteLength { get; set; }
}

public sealed class GltfMaterial
{
    [JsonPropertyName("name")] public string Name { get; set; }
    [JsonPropertyName("pbrMetallicRoughness")] public GltfPbrMetallicRoughness PbrMetallicRoughness { get; set; }
    [JsonPropertyName("normalTexture")] public GltfNormalTextureInfo NormalTexture { get; set; }
    [JsonPropertyName("occlusionTexture")] public GltfOcclusionTextureInfo OcclusionTexture { get; set; }
    [JsonPropertyName("emissiveTexture")] public GltfTextureInfo EmissiveTexture { get; set; }
    [JsonPropertyName("emissiveFactor")] public float[] EmissiveFactor { get; set; }
    [JsonPropertyName("alphaMode")] public string AlphaMode { get; set; } = "OPAQUE";
    [JsonPropertyName("alphaCutoff")] public float AlphaCutoff { get; set; } = 0.5f;
    [JsonPropertyName("doubleSided")] public bool DoubleSided { get; set; }
    [JsonPropertyName("extensions")] public Dictionary<string, JsonElement> Extensions { get; set; }
    [JsonPropertyName("extras")] public JsonElement? Extras { get; set; }
}

public sealed class GltfPbrMetallicRoughness
{
    [JsonPropertyName("baseColorFactor")] public float[] BaseColorFactor { get; set; }
    [JsonPropertyName("baseColorTexture")] public GltfTextureInfo BaseColorTexture { get; set; }
    [JsonPropertyName("metallicFactor")] public float MetallicFactor { get; set; } = 1;
    [JsonPropertyName("roughnessFactor")] public float RoughnessFactor { get; set; } = 1;
    [JsonPropertyName("metallicRoughnessTexture")] public GltfTextureInfo MetallicRoughnessTexture { get; set; }
    [JsonPropertyName("extensions")] public Dictionary<string, JsonElement> Extensions { get; set; }
    [JsonPropertyName("extras")] public JsonElement? Extras { get; set; }
}

public class GltfTextureInfo
{
    [JsonPropertyName("index")] public int Index { get; set; }
    [JsonPropertyName("texCoord")] public int TexCoord { get; set; }
    [JsonPropertyName("extensions")] public Dictionary<string, JsonElement> Extensions { get; set; }
    [JsonPropertyName("extras")] public JsonElement? Extras { get; set; }
}

public sealed class GltfNormalTextureInfo : GltfTextureInfo
{
    [JsonPropertyName("scale")] public float Scale { get; set; } = 1;
}

public sealed class GltfOcclusionTextureInfo : GltfTextureInfo
{
    [JsonPropertyName("strength")] public float Strength { get; set; } = 1;
}

public sealed class GltfTexture
{
    [JsonPropertyName("source")] public int? Source { get; set; }
    [JsonPropertyName("sampler")] public int? Sampler { get; set; }
    [JsonPropertyName("name")] public string Name { get; set; }
    [JsonPropertyName("extensions")] public Dictionary<string, JsonElement> Extensions { get; set; }
    [JsonPropertyName("extras")] public JsonElement? Extras { get; set; }
}

public sealed class GltfImage
{
    [JsonPropertyName("uri")] public string Uri { get; set; }
    [JsonPropertyName("mimeType")] public string MimeType { get; set; }
    [JsonPropertyName("bufferView")] public int? BufferView { get; set; }
    [JsonPropertyName("name")] public string Name { get; set; }
    [JsonPropertyName("extensions")] public Dictionary<string, JsonElement> Extensions { get; set; }
    [JsonPropertyName("extras")] public JsonElement? Extras { get; set; }
}

public sealed class GltfSampler
{
    [JsonPropertyName("magFilter")] public int? MagFilter { get; set; }
    [JsonPropertyName("minFilter")] public int? MinFilter { get; set; }
    [JsonPropertyName("wrapS")] public int WrapS { get; set; } = 10497;
    [JsonPropertyName("wrapT")] public int WrapT { get; set; } = 10497;
    [JsonPropertyName("name")] public string Name { get; set; }
    [JsonPropertyName("extensions")] public Dictionary<string, JsonElement> Extensions { get; set; }
    [JsonPropertyName("extras")] public JsonElement? Extras { get; set; }
}

public sealed class GltfSkin
{
    [JsonPropertyName("name")] public string Name { get; set; }
    [JsonPropertyName("inverseBindMatrices")] public int? InverseBindMatrices { get; set; }
    [JsonPropertyName("skeleton")] public int? Skeleton { get; set; }
    [JsonPropertyName("joints")] public List<int> Joints { get; set; } = [];
    [JsonPropertyName("extensions")] public Dictionary<string, JsonElement> Extensions { get; set; }
    [JsonPropertyName("extras")] public JsonElement? Extras { get; set; }
}

public sealed class GltfAnimation
{
    [JsonPropertyName("name")] public string Name { get; set; }
    [JsonPropertyName("channels")] public List<GltfAnimationChannel> Channels { get; set; } = [];
    [JsonPropertyName("samplers")] public List<GltfAnimationSampler> Samplers { get; set; } = [];
    [JsonPropertyName("extensions")] public Dictionary<string, JsonElement> Extensions { get; set; }
    [JsonPropertyName("extras")] public JsonElement? Extras { get; set; }
}

public sealed class GltfAnimationChannel
{
    [JsonPropertyName("sampler")] public int Sampler { get; set; }
    [JsonPropertyName("target")] public GltfAnimationTarget Target { get; set; }
    [JsonPropertyName("extensions")] public Dictionary<string, JsonElement> Extensions { get; set; }
    [JsonPropertyName("extras")] public JsonElement? Extras { get; set; }
}

public sealed class GltfAnimationTarget
{
    [JsonPropertyName("node")] public int? Node { get; set; }
    [JsonPropertyName("path")] public string Path { get; set; }
    [JsonPropertyName("extensions")] public Dictionary<string, JsonElement> Extensions { get; set; }
    [JsonPropertyName("extras")] public JsonElement? Extras { get; set; }
}

public sealed class GltfAnimationSampler
{
    [JsonPropertyName("input")] public int Input { get; set; }
    [JsonPropertyName("output")] public int Output { get; set; }
    [JsonPropertyName("interpolation")] public string Interpolation { get; set; } = "LINEAR";
    [JsonPropertyName("extensions")] public Dictionary<string, JsonElement> Extensions { get; set; }
    [JsonPropertyName("extras")] public JsonElement? Extras { get; set; }
}

public sealed class GltfCamera
{
    [JsonPropertyName("name")] public string Name { get; set; }
    [JsonPropertyName("type")] public string Type { get; set; }
    [JsonPropertyName("perspective")] public GltfPerspective Perspective { get; set; }
    [JsonPropertyName("orthographic")] public GltfOrthographic Orthographic { get; set; }
    [JsonPropertyName("extensions")] public Dictionary<string, JsonElement> Extensions { get; set; }
    [JsonPropertyName("extras")] public JsonElement? Extras { get; set; }
}

public sealed class GltfPerspective
{
    [JsonPropertyName("yfov")] public float YFov { get; set; }
    [JsonPropertyName("aspectRatio")] public float? AspectRatio { get; set; }
    [JsonPropertyName("znear")] public float ZNear { get; set; }
    [JsonPropertyName("zfar")] public float? ZFar { get; set; }
}

public sealed class GltfOrthographic
{
    [JsonPropertyName("xmag")] public float XMag { get; set; }
    [JsonPropertyName("ymag")] public float YMag { get; set; }
    [JsonPropertyName("znear")] public float ZNear { get; set; }
    [JsonPropertyName("zfar")] public float ZFar { get; set; }
}
