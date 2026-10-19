using System;
using System.Collections.Generic;
using System.IO;
using System.Text;
using System.Text.Json;
using Adamantium.Engine.Compiler.Models.Gltf.Draco;

namespace Adamantium.Engine.Compiler.Models.Gltf;

/// <summary>A glTF 2.0 asset read whole: its JSON, every buffer's bytes - from files beside it, data URIs or the
/// binary chunk of a <c>.glb</c> - and where its images lie or what bytes they are.</summary>
public sealed class GltfFile
{
    private const uint GlbMagic = 0x46546C67;
    private const uint JsonChunk = 0x4E4F534A;
    private const uint BinaryChunk = 0x004E4942;
    private const string Meshopt = "KHR_meshopt_compression";
    private const string MeshoptVendor = "EXT_meshopt_compression";
    private const string Draco = "KHR_draco_mesh_compression";

    private static readonly JsonSerializerOptions JsonOptions = new()
    {
        AllowTrailingCommas = true,
        ReadCommentHandling = JsonCommentHandling.Skip
    };

    private readonly byte[][] unpackedViews;
    private byte[][] unpackedAccessors;

    private GltfFile(string path, GltfRoot root, IReadOnlyList<byte[]> buffers, byte[][] unpackedViews)
    {
        Path = path;
        Root = root;
        Buffers = buffers;
        this.unpackedViews = unpackedViews;
    }

    /// <summary>The file read.</summary>
    public string Path { get; }

    /// <summary>The asset's JSON.</summary>
    public GltfRoot Root { get; }

    /// <summary>Each of <see cref="GltfRoot.Buffers"/>, its bytes; null for one no buffer view reads, such as the
    /// placeholder behind meshopt-compressed views.</summary>
    public IReadOnlyList<byte[]> Buffers { get; }

    /// <summary>The extensions this reader understands; one the asset requires beyond these makes it unreadable.</summary>
    public static readonly IReadOnlySet<string> SupportedExtensions = new HashSet<string>
    {
        "KHR_lights_punctual",
        "KHR_materials_emissive_strength",
        "KHR_texture_transform",
        "KHR_materials_ior",
        "KHR_materials_dispersion",
        "KHR_materials_unlit",
        "KHR_materials_clearcoat",
        "KHR_materials_sheen",
        "KHR_materials_transmission",
        "KHR_materials_diffuse_transmission",
        "KHR_materials_volume",
        "KHR_materials_specular",
        "KHR_materials_iridescence",
        "KHR_materials_anisotropy",
        "KHR_materials_pbrSpecularGlossiness",
        "KHR_materials_variants",
        "KHR_texture_basisu",
        "EXT_texture_webp",
        "EXT_texture_avif",
        "KHR_animation_pointer",
        "KHR_mesh_quantization",
        "EXT_mesh_gpu_instancing",
        "KHR_xmp_json_ld",
        Meshopt,
        MeshoptVendor,
        Draco
    };

    /// <summary>Reads a <c>.gltf</c> or <c>.glb</c>, told apart by its first bytes rather than its name.</summary>
    /// <exception cref="InvalidDataException">The file is no glTF 2.0, or a buffer it points at cannot be had.</exception>
    /// <exception cref="NotSupportedException">The asset requires an extension this reader does not know.</exception>
    public static GltfFile Read(string path)
    {
        var bytes = File.ReadAllBytes(path);
        return Read(path, bytes);
    }

    /// <summary><see cref="Read(string)"/> from bytes already in memory; <paramref name="path"/> places the files it
    /// points at.</summary>
    public static GltfFile Read(string path, byte[] bytes)
    {
        byte[] glbBinary = null;
        ReadOnlySpan<byte> json = bytes;
        if (bytes.Length >= 12 && BitConverter.ToUInt32(bytes, 0) == GlbMagic)
        {
            (var glbJson, glbBinary) = SplitGlb(bytes);
            json = glbJson;
        }

        if (json.Length >= 3 && json[0] == 0xEF && json[1] == 0xBB && json[2] == 0xBF)
        {
            json = json[3..];
        }

        var root = JsonSerializer.Deserialize<GltfRoot>(json, JsonOptions)
                   ?? throw new InvalidDataException($"{path}: the glTF JSON is empty");
        if (root.Asset?.Version is not { } version || !version.StartsWith("2.", StringComparison.Ordinal))
        {
            throw new InvalidDataException($"{path}: glTF {root.Asset?.Version ?? "of no version"} is not 2.0");
        }

        foreach (var required in root.ExtensionsRequired)
        {
            if (!SupportedExtensions.Contains(required))
            {
                throw new NotSupportedException($"{path}: the asset requires the glTF extension {required}");
            }
        }

        var compressions = new GltfMeshoptCompression[root.BufferViews.Count];
        var read = new bool[root.Buffers.Count];
        for (int i = 0; i < compressions.Length; i++)
        {
            try
            {
                compressions[i] = MeshoptOf(root.BufferViews[i]);
            }
            catch (InvalidDataException e)
            {
                throw new InvalidDataException($"{path}: buffer view {i}: {e.Message}", e);
            }

            var buffer = compressions[i]?.Buffer ?? root.BufferViews[i].Buffer;
            if ((uint)buffer < (uint)read.Length)
            {
                read[buffer] = true;
            }
        }

        var buffers = new byte[root.Buffers.Count][];
        for (int i = 0; i < buffers.Length; i++)
        {
            if (read[i])
            {
                buffers[i] = LoadBuffer(path, root.Buffers[i], i, glbBinary);
            }
        }

        var file = new GltfFile(path, root, buffers, Unpack(path, root, buffers, compressions));
        file.UnpackDraco();
        return file;
    }

    /// <summary>The accessor's elements, tightly packed, when a Draco-compressed primitive gave them; null for one
    /// read from its buffer view.</summary>
    public byte[] UnpackedAccessor(int accessor) => unpackedAccessors?[accessor];

    /// <summary>The bytes <paramref name="bufferView"/> covers, unpacked when it is meshopt-compressed.</summary>
    public ReadOnlySpan<byte> View(int bufferView)
    {
        if (unpackedViews?[bufferView] is { } unpacked)
        {
            return unpacked;
        }

        var view = Root.BufferViews[bufferView];
        return Buffers[view.Buffer].AsSpan(view.ByteOffset, view.ByteLength);
    }

    /// <summary>The image's file, if it has one beside the asset; null for one given as bytes.</summary>
    public string ImageFile(GltfImage image)
    {
        if (image.Uri == null || IsDataUri(image.Uri)) return null;

        return System.IO.Path.GetFullPath(System.IO.Path.Combine(Directory(), Uri.UnescapeDataString(image.Uri)));
    }

    /// <summary>The image's bytes when it is inside the asset - a data URI or a buffer view; null for one in a file.</summary>
    public byte[] ImageBytes(GltfImage image)
    {
        if (image.BufferView is { } view) return View(view).ToArray();
        if (image.Uri != null && IsDataUri(image.Uri)) return DecodeDataUri(image.Uri, out _);
        return null;
    }

    private string Directory() => System.IO.Path.GetDirectoryName(System.IO.Path.GetFullPath(Path)) ?? string.Empty;

    private void UnpackDraco()
    {
        for (int m = 0; m < Root.Meshes.Count; m++)
        {
            for (int p = 0; p < Root.Meshes[m].Primitives.Count; p++)
            {
                var primitive = Root.Meshes[m].Primitives[p];
                if (primitive.Extensions == null || !primitive.Extensions.TryGetValue(Draco, out var json))
                {
                    continue;
                }

                try
                {
                    UnpackDraco(primitive, json);
                }
                catch (InvalidDataException e)
                {
                    throw new InvalidDataException($"{Path}: mesh {m} primitive {p}: {e.Message}", e);
                }
            }
        }
    }

    private void UnpackDraco(GltfPrimitive primitive, JsonElement json)
    {
        var compression = Deserialize<GltfDracoCompression>(json, Draco);
        if (compression?.Attributes == null || (uint)compression.BufferView >= (uint)Root.BufferViews.Count)
        {
            throw new InvalidDataException($"its {Draco} names no buffer view or no attributes");
        }

        if (primitive.Mode is not (GltfPrimitiveMode.Triangles or GltfPrimitiveMode.TriangleStrip))
        {
            throw new InvalidDataException($"a Draco mesh is drawn as mode {primitive.Mode}, not triangles");
        }

        if (primitive.Indices is not { } indices || (uint)indices >= (uint)Root.Accessors.Count)
        {
            throw new InvalidDataException("a Draco mesh has no indices accessor");
        }

        var attributes = new List<(string Name, int Accessor, uint Id)>();
        var maxPoints = 0;
        foreach (var (name, id) in compression.Attributes)
        {
            if (!primitive.Attributes.TryGetValue(name, out var accessor))
            {
                continue;
            }

            if ((uint)accessor >= (uint)Root.Accessors.Count)
            {
                throw new InvalidDataException($"its {name} accessor {accessor} is not in the asset");
            }

            attributes.Add((name, accessor, (uint)id));
            maxPoints = Math.Max(maxPoints, Root.Accessors[accessor].Count);
        }

        var mesh = DracoDecoder.Decode(View(compression.BufferView).ToArray(), Root.Accessors[indices].Count / 3,
            attributes.Count > 0 ? maxPoints : int.MaxValue);
        unpackedAccessors ??= new byte[Root.Accessors.Count][];
        unpackedAccessors[indices] = IndexBytes(mesh, Root.Accessors[indices]);
        foreach (var (name, accessor, id) in attributes)
        {
            var attribute = mesh.Attribute(id) ?? throw new InvalidDataException($"its Draco mesh has no attribute {id} for {name}");
            var info = Root.Accessors[accessor];
            if (info.Count != mesh.PointCount || GltfAccessors.ComponentsOf(info.Type) != attribute.Components
                || info.ComponentType != ComponentTypeOf(attribute.DataType))
            {
                throw new InvalidDataException($"its {name} is {info.Count} {info.Type} of type {info.ComponentType}, " +
                                               $"Draco gives {mesh.PointCount} of {attribute.Components} {attribute.DataType}");
            }

            unpackedAccessors[accessor] = attribute.PointValues(mesh.PointCount);
        }
    }

    private static T Deserialize<T>(JsonElement json, string extension)
    {
        try
        {
            return json.Deserialize<T>(JsonOptions);
        }
        catch (JsonException e)
        {
            throw new InvalidDataException($"{extension} is not what the extension says: {e.Message}", e);
        }
    }

    private static byte[] IndexBytes(DracoMesh mesh, GltfAccessor accessor)
    {
        var type = accessor.ComponentType;
        var unsigned = type is GltfComponentType.UnsignedByte or GltfComponentType.UnsignedShort or GltfComponentType.UnsignedInt;
        var size = unsigned ? GltfAccessors.SizeOf(type) : 0;
        if (!unsigned || accessor.Count != mesh.Indices.Length || (size < 4 && mesh.PointCount > 1 << (8 * size)))
        {
            throw new InvalidDataException($"its indices are {accessor.Count} of type {type}, " +
                                           $"Draco gives {mesh.Indices.Length} over {mesh.PointCount} points");
        }

        var bytes = new byte[(long)mesh.Indices.Length * size];
        for (int i = 0; i < mesh.Indices.Length; i++)
        {
            for (int b = 0; b < size; b++)
            {
                bytes[i * size + b] = (byte)(mesh.Indices[i] >> (8 * b));
            }
        }

        return bytes;
    }

    private static int ComponentTypeOf(DracoDataType type) => type switch
    {
        DracoDataType.Int8 => GltfComponentType.Byte,
        DracoDataType.UInt8 => GltfComponentType.UnsignedByte,
        DracoDataType.Int16 => GltfComponentType.Short,
        DracoDataType.UInt16 => GltfComponentType.UnsignedShort,
        DracoDataType.UInt32 => GltfComponentType.UnsignedInt,
        DracoDataType.Float32 => GltfComponentType.Float,
        _ => -1
    };

    private static GltfMeshoptCompression MeshoptOf(GltfBufferView view)
    {
        if (view.Extensions == null
            || !(view.Extensions.TryGetValue(Meshopt, out var json) || view.Extensions.TryGetValue(MeshoptVendor, out json)))
        {
            return null;
        }

        return Deserialize<GltfMeshoptCompression>(json, Meshopt);
    }

    private static byte[][] Unpack(string path, GltfRoot root, IReadOnlyList<byte[]> buffers,
        GltfMeshoptCompression[] compressions)
    {
        byte[][] unpacked = null;
        for (int i = 0; i < compressions.Length; i++)
        {
            if (compressions[i] is not { } compression)
            {
                continue;
            }

            var source = (uint)compression.Buffer < (uint)buffers.Count ? buffers[compression.Buffer] : null;
            if (source == null || compression.ByteOffset < 0 || compression.ByteLength < 0
                || (long)compression.ByteOffset + compression.ByteLength > source.Length)
            {
                throw new InvalidDataException($"{path}: buffer view {i}'s compressed bytes lie past buffer {compression.Buffer}");
            }

            if ((long)compression.Count * compression.ByteStride != root.BufferViews[i].ByteLength)
            {
                throw new InvalidDataException($"{path}: buffer view {i} unpacks into {compression.Count} elements " +
                                               $"of {compression.ByteStride} bytes, {root.BufferViews[i].ByteLength} are said");
            }

            byte[] bytes;
            try
            {
                bytes = GltfMeshopt.Decode(compression, source.AsSpan(compression.ByteOffset, compression.ByteLength));
            }
            catch (InvalidDataException e)
            {
                throw new InvalidDataException($"{path}: buffer view {i}: {e.Message}", e);
            }

            unpacked ??= new byte[compressions.Length][];
            unpacked[i] = bytes;
        }

        return unpacked;
    }

    private static (byte[] Json, byte[] Binary) SplitGlb(byte[] bytes)
    {
        var version = BitConverter.ToUInt32(bytes, 4);
        if (version != 2)
        {
            throw new InvalidDataException($"a GLB of version {version} is not 2");
        }

        var length = (int)Math.Min(BitConverter.ToUInt32(bytes, 8), (uint)bytes.Length);
        byte[] json = null;
        byte[] binary = null;
        var at = 12;
        while (at + 8 <= length)
        {
            var chunkLength = (int)BitConverter.ToUInt32(bytes, at);
            var chunkType = BitConverter.ToUInt32(bytes, at + 4);
            var start = at + 8;
            if (chunkLength < 0 || start + chunkLength > length)
            {
                throw new InvalidDataException("a GLB chunk runs past the end of the file");
            }

            if (chunkType == JsonChunk && json == null)
            {
                json = bytes.AsSpan(start, chunkLength).ToArray();
            }
            else if (chunkType == BinaryChunk && binary == null)
            {
                binary = bytes.AsSpan(start, chunkLength).ToArray();
            }

            at = start + chunkLength;
        }

        if (json == null)
        {
            throw new InvalidDataException("a GLB has no JSON chunk");
        }

        return (json, binary);
    }

    private static byte[] LoadBuffer(string path, GltfBuffer buffer, int index, byte[] glbBinary)
    {
        byte[] bytes;
        if (buffer.Uri == null)
        {
            bytes = index == 0 ? glbBinary : null;
            if (bytes == null)
            {
                throw new InvalidDataException($"{path}: buffer {index} has no URI and no GLB binary chunk to be");
            }
        }
        else if (IsDataUri(buffer.Uri))
        {
            bytes = DecodeDataUri(buffer.Uri, out _);
        }
        else
        {
            var directory = System.IO.Path.GetDirectoryName(System.IO.Path.GetFullPath(path)) ?? string.Empty;
            var file = System.IO.Path.GetFullPath(System.IO.Path.Combine(directory, Uri.UnescapeDataString(buffer.Uri)));
            if (!File.Exists(file))
            {
                throw new InvalidDataException($"{path}: buffer {index}'s file {buffer.Uri} is not found");
            }

            bytes = File.ReadAllBytes(file);
        }

        if (bytes.Length < buffer.ByteLength)
        {
            throw new InvalidDataException(
                $"{path}: buffer {index} holds {bytes.Length} bytes, {buffer.ByteLength} are said");
        }

        return bytes;
    }

    private static bool IsDataUri(string uri) => uri.StartsWith("data:", StringComparison.OrdinalIgnoreCase);

    private static byte[] DecodeDataUri(string uri, out string mimeType)
    {
        var comma = uri.IndexOf(',');
        if (comma < 0)
        {
            throw new InvalidDataException("a data URI has no comma");
        }

        var header = uri.AsSpan(5, comma - 5);
        var base64 = header.EndsWith(";base64", StringComparison.OrdinalIgnoreCase);
        mimeType = (base64 ? header[..^7] : header).ToString();
        var data = uri[(comma + 1)..];
        return base64 ? Convert.FromBase64String(data) : Encoding.UTF8.GetBytes(Uri.UnescapeDataString(data));
    }
}
