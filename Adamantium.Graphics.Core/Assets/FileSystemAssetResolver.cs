using System;
using System.IO;

namespace Adamantium.Graphics.Core.Assets;

/// <summary>Resolves an asset to its file: an absolute path as it is, a relative one from the resolver's root - the
/// application's folder, where a content project's files are copied, unless it is given another, a content project's
/// own folder say.</summary>
public class FileSystemAssetResolver : IAssetResolver
{
    private readonly string root;

    public FileSystemAssetResolver(string root = null)
    {
        this.root = root ?? AppContext.BaseDirectory;
    }

    /// <summary>The folder a relative asset path is taken from.</summary>
    public string Root => root;

    public bool Exists(string assetPath)
    {
        return File.Exists(FullPath(assetPath));
    }

    public string Resolve(string assetPath)
    {
        return Exists(assetPath) ? FullPath(assetPath) : null;
    }

    private string FullPath(string assetPath)
    {
        return Path.Combine(root, assetPath.Replace('/', Path.DirectorySeparatorChar));
    }
}
