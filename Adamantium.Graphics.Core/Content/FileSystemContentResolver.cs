using System;
using System.IO;

namespace Adamantium.Graphics.Core.Content;

/// <summary>Resolves an asset to its file: an absolute path as it is, a relative one from the application's folder,
/// where a content project's files are copied.</summary>
public class FileSystemContentResolver : IContentResolver
{
    public bool Exists(string assetPath)
    {
        return File.Exists(FullPath(assetPath));
    }

    public string Resolve(string assetPath)
    {
        return Exists(assetPath) ? FullPath(assetPath) : null;
    }

    private static string FullPath(string assetPath)
    {
        return Path.Combine(AppContext.BaseDirectory, assetPath.Replace('/', Path.DirectorySeparatorChar));
    }
}
