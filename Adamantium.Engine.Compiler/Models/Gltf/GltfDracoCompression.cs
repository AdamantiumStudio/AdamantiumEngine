using System.Collections.Generic;
using System.Text.Json.Serialization;

namespace Adamantium.Engine.Compiler.Models.Gltf;

/// <summary>A primitive's <c>KHR_draco_mesh_compression</c>: the buffer view of its Draco mesh and the Draco attribute
/// each of its attributes is.</summary>
public sealed class GltfDracoCompression
{
    [JsonPropertyName("bufferView")] public int BufferView { get; set; }
    [JsonPropertyName("attributes")] public Dictionary<string, int> Attributes { get; set; } = [];
}
