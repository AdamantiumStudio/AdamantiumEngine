using System;

namespace Adamantium.Graphics.Core.Assets;

/// <summary>Finds the file an asset's path stands for.</summary>
public interface IAssetResolver
{
    bool Exists(String assetPath);

    /// <summary>The file; null when this resolver does not find it.</summary>
    String Resolve(String assetPath);
}
