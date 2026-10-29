using System.Collections.Generic;

namespace Adamantium.Graphics.Core.Assets;

/// <summary>The file an asset was imported from and how, so it can be imported again.</summary>
public sealed class AssetSource
{
    /// <summary>The source file, relative to the asset file's folder in its content project (forward slashes); absolute
    /// when it lies on another drive.</summary>
    public string Path { get; set; }

    /// <summary>The name of the importer that read it.</summary>
    public string Importer { get; set; }

    /// <summary>The import settings it was read with.</summary>
    public Dictionary<string, string> Parameters { get; set; } = new();
}
