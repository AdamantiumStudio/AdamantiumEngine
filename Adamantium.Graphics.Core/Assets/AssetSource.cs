using System;
using System.Collections.Generic;
using System.IO;
using System.Security.Cryptography;

namespace Adamantium.Graphics.Core.Assets;

/// <summary>The file an asset was imported from and how, so it can be imported again.</summary>
public sealed class AssetSource
{
    /// <summary>The source file, relative to the asset file's folder in its content project (forward slashes); absolute
    /// when it lies on another drive.</summary>
    public string Path { get; set; }

    /// <summary>The source file's <see cref="ComputeHash">hash</see> when it was imported.</summary>
    public string Hash { get; set; }

    /// <summary>When the source file was last written, in UTC, as it was read for the import: while it and
    /// <see cref="Length"/> still match the file, its <see cref="Hash"/> is not computed again.</summary>
    public DateTime Written { get; set; }

    /// <summary>The source file's length in bytes as it was read for the import.</summary>
    public long Length { get; set; }

    /// <summary>The name of the importer that read it.</summary>
    public string Importer { get; set; }

    /// <summary>The import settings it was read with.</summary>
    public Dictionary<string, string> Parameters { get; set; } = new();

    /// <summary>The version of the engine that imported it.</summary>
    public string EngineVersion { get; set; }

    /// <summary>The files beside the source that the asset reads - a model's textures - relative to the source's folder;
    /// they go wherever the asset goes.</summary>
    public List<string> Companions { get; set; } = [];

    /// <summary>The SHA-256 of the file's bytes, in hexadecimal. The file is read without keeping others from writing,
    /// renaming or deleting it meanwhile.</summary>
    public static string ComputeHash(string path)
    {
        using var stream = new FileStream(path, FileMode.Open, FileAccess.Read,
            FileShare.ReadWrite | FileShare.Delete);
        return Convert.ToHexString(SHA256.HashData(stream));
    }
}
