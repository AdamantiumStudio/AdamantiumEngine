using System;

namespace Adamantium.Graphics.Core.Content;

/// <summary>Finds the file an asset's path stands for.</summary>
public interface IContentResolver
{
    bool Exists(String assetPath);

    /// <summary>The file; null when this resolver does not find it.</summary>
    String Resolve(String assetPath);
}
