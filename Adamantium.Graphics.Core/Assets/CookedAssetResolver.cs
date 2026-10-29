using System;
using System.IO;

namespace Adamantium.Graphics.Core.Assets;

/// <summary>
/// Resolves a logical asset name to a baked artifact (<c>&lt;contentRoot&gt;/&lt;name&gt;.aemf</c>) when one
/// exists, so the runtime loads precompiled content instead of re-parsing the source file. Register this
/// ahead of <see cref="FileSystemAssetResolver"/> so cooked content wins, with raw source as fallback.
/// The root defaults to the application's folder, where a content project puts its cooked files.
/// </summary>
public class CookedAssetResolver : IAssetResolver
{
    private readonly string contentRoot;
    private readonly string cookedExtension;

    public CookedAssetResolver(string contentRoot = null, string cookedExtension = AssetFile.FileExtension)
    {
        this.contentRoot = contentRoot ?? AppContext.BaseDirectory;
        this.cookedExtension = cookedExtension;
    }

    public bool Exists(string assetPath)
    {
        return File.Exists(CookedPath(assetPath));
    }

    public string Resolve(string assetPath)
    {
        return Exists(assetPath) ? CookedPath(assetPath) : null;
    }

    private string CookedPath(string assetPath)
    {
        return Path.Combine(contentRoot, assetPath.Replace('/', Path.DirectorySeparatorChar) + cookedExtension);
    }
}
