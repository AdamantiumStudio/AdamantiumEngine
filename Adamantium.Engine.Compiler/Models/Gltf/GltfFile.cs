using System;
using System.Collections.Generic;
using System.IO;
using System.Text;
using System.Text.Json;

namespace Adamantium.Engine.Compiler.Models.Gltf;

/// <summary>A glTF 2.0 asset read whole: its JSON, every buffer's bytes - from files beside it, data URIs or the
/// binary chunk of a <c>.glb</c> - and where its images lie or what bytes they are.</summary>
public sealed class GltfFile
{
    private const uint GlbMagic = 0x46546C67;
    private const uint JsonChunk = 0x4E4F534A;
    private const uint BinaryChunk = 0x004E4942;

    private static readonly JsonSerializerOptions JsonOptions = new()
    {
        AllowTrailingCommas = true,
        ReadCommentHandling = JsonCommentHandling.Skip
    };

    private GltfFile(string path, GltfRoot root, IReadOnlyList<byte[]> buffers)
    {
        Path = path;
        Root = root;
        Buffers = buffers;
    }

    /// <summary>The file read.</summary>
    public string Path { get; }

    /// <summary>The asset's JSON.</summary>
    public GltfRoot Root { get; }

    /// <summary>Each of <see cref="GltfRoot.Buffers"/>, its bytes.</summary>
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
        "KHR_xmp_json_ld"
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

        var buffers = new byte[root.Buffers.Count][];
        for (int i = 0; i < buffers.Length; i++)
        {
            buffers[i] = LoadBuffer(path, root.Buffers[i], i, glbBinary);
        }

        return new GltfFile(path, root, buffers);
    }

    /// <summary>The bytes <paramref name="bufferView"/> covers.</summary>
    public ReadOnlySpan<byte> View(int bufferView)
    {
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
