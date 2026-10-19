using System.Text.Json.Serialization;

namespace Adamantium.Engine.Compiler.Models.Gltf;

/// <summary>A buffer view's <c>EXT_meshopt_compression</c> or <c>KHR_meshopt_compression</c>: where its compressed
/// bytes lie and how they unpack into <see cref="Count"/> elements of <see cref="ByteStride"/> bytes.</summary>
public sealed class GltfMeshoptCompression
{
    [JsonPropertyName("buffer")] public int Buffer { get; set; }
    [JsonPropertyName("byteOffset")] public int ByteOffset { get; set; }
    [JsonPropertyName("byteLength")] public int ByteLength { get; set; }
    [JsonPropertyName("byteStride")] public int ByteStride { get; set; }
    [JsonPropertyName("count")] public int Count { get; set; }
    [JsonPropertyName("mode")] public string Mode { get; set; }
    [JsonPropertyName("filter")] public string Filter { get; set; } = "NONE";
}
