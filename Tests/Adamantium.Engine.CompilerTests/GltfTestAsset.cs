using System;
using System.Collections.Generic;
using System.IO;
using System.Text;
using System.Text.Json;
using System.Text.Json.Nodes;

namespace Adamantium.Engine.CompilerTests;

/// <summary>A glTF asset put together in a test: its JSON as nodes, one binary buffer grown view by view, written as a
/// <c>.gltf</c> with the buffer in a data URI or a file beside it, or as a <c>.glb</c>.</summary>
internal sealed class GltfTestAsset
{
    private readonly List<byte> buffer = [];

    public JsonObject Json { get; } = new()
    {
        ["asset"] = new JsonObject { ["version"] = "2.0" },
        ["bufferViews"] = new JsonArray(),
        ["accessors"] = new JsonArray()
    };

    public JsonArray Array(string name)
    {
        if (Json[name] is not JsonArray array)
        {
            array = new JsonArray();
            Json[name] = array;
        }

        return array;
    }

    /// <summary>Adds bytes as a buffer view of their own, on a four-byte boundary; its index.</summary>
    public int View(byte[] bytes, int? stride = null)
    {
        while (buffer.Count % 4 != 0) buffer.Add(0);
        var view = new JsonObject { ["buffer"] = 0, ["byteOffset"] = buffer.Count, ["byteLength"] = bytes.Length };
        if (stride is { } byteStride) view["byteStride"] = byteStride;
        buffer.AddRange(bytes);
        var views = Array("bufferViews");
        views.Add(view);
        return views.Count - 1;
    }

    /// <summary>An accessor over a view; its index.</summary>
    public int Accessor(int? view, int componentType, int count, string type, int byteOffset = 0, bool normalized = false,
        JsonObject sparse = null)
    {
        var accessor = new JsonObject { ["componentType"] = componentType, ["count"] = count, ["type"] = type };
        if (view is { } bufferView) accessor["bufferView"] = bufferView;
        if (byteOffset != 0) accessor["byteOffset"] = byteOffset;
        if (normalized) accessor["normalized"] = true;
        if (sparse != null) accessor["sparse"] = sparse;
        var accessors = Array("accessors");
        accessors.Add(accessor);
        return accessors.Count - 1;
    }

    public int Floats(string type, params float[] values)
    {
        var bytes = new byte[values.Length * 4];
        Buffer.BlockCopy(values, 0, bytes, 0, bytes.Length);
        var components = type switch { "SCALAR" => 1, "VEC2" => 2, "VEC3" => 3, "VEC4" => 4, "MAT4" => 16, _ => 1 };
        return Accessor(View(bytes), 5126, values.Length / components, type);
    }

    public int UShorts(params ushort[] values)
    {
        var bytes = new byte[values.Length * 2];
        Buffer.BlockCopy(values, 0, bytes, 0, bytes.Length);
        return Accessor(View(bytes), 5123, values.Length, "SCALAR");
    }

    public int Mesh(JsonObject primitive)
    {
        var meshes = Array("meshes");
        meshes.Add(new JsonObject { ["primitives"] = new JsonArray(primitive) });
        return meshes.Count - 1;
    }

    public int Node(JsonObject node)
    {
        var nodes = Array("nodes");
        nodes.Add(node);
        return nodes.Count - 1;
    }

    public void Scene(params int[] nodes)
    {
        var list = new JsonArray();
        foreach (var node in nodes) list.Add(node);
        Json["scenes"] = new JsonArray(new JsonObject { ["nodes"] = list });
        Json["scene"] = 0;
    }

    public static JsonObject Primitive(int position, int? indices = null, int? material = null, int? mode = null,
        params (string Name, int Accessor)[] attributes)
    {
        var attributeObject = new JsonObject { ["POSITION"] = position };
        foreach (var (name, accessor) in attributes) attributeObject[name] = accessor;
        var primitive = new JsonObject { ["attributes"] = attributeObject };
        if (indices is { } i) primitive["indices"] = i;
        if (material is { } m) primitive["material"] = m;
        if (mode is { } mo) primitive["mode"] = mo;
        return primitive;
    }

    /// <summary>Writes the asset as a <c>.gltf</c>, the buffer in a data URI; its path.</summary>
    public string WriteGltf(string folder, string name = "asset.gltf")
    {
        Json["buffers"] = new JsonArray(new JsonObject
        {
            ["byteLength"] = buffer.Count,
            ["uri"] = "data:application/octet-stream;base64," + Convert.ToBase64String(buffer.ToArray())
        });
        var path = Path.Combine(folder, name);
        File.WriteAllText(path, Json.ToJsonString(new JsonSerializerOptions { WriteIndented = true }));
        return path;
    }

    /// <summary>Writes the asset as a <c>.gltf</c> with its buffer in a <c>.bin</c> beside it; its path.</summary>
    public string WriteGltfWithBin(string folder, string name = "asset.gltf", string bin = "asset data.bin")
    {
        File.WriteAllBytes(Path.Combine(folder, bin), buffer.ToArray());
        Json["buffers"] = new JsonArray(new JsonObject { ["byteLength"] = buffer.Count, ["uri"] = Uri.EscapeDataString(bin) });
        var path = Path.Combine(folder, name);
        File.WriteAllText(path, Json.ToJsonString());
        return path;
    }

    /// <summary>Writes the asset as a <c>.glb</c>: a JSON chunk and the buffer as its binary chunk; its path.</summary>
    public string WriteGlb(string folder, string name = "asset.glb")
    {
        while (buffer.Count % 4 != 0) buffer.Add(0);
        Json["buffers"] = new JsonArray(new JsonObject { ["byteLength"] = buffer.Count });
        var json = Encoding.UTF8.GetBytes(Json.ToJsonString());
        var paddedJson = (json.Length + 3) & ~3;
        using var stream = new MemoryStream();
        using (var writer = new BinaryWriter(stream, Encoding.UTF8, true))
        {
            writer.Write(0x46546C67u);
            writer.Write(2u);
            writer.Write((uint)(12 + 8 + paddedJson + 8 + buffer.Count));
            writer.Write((uint)paddedJson);
            writer.Write(0x4E4F534Au);
            writer.Write(json);
            for (int i = json.Length; i < paddedJson; i++) writer.Write((byte)' ');
            writer.Write((uint)buffer.Count);
            writer.Write(0x004E4942u);
            writer.Write(buffer.ToArray());
        }

        var path = Path.Combine(folder, name);
        File.WriteAllBytes(path, stream.ToArray());
        return path;
    }
}
