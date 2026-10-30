namespace Adamantium.Graphics.Core.Assets;

/// <summary>How <see cref="IAssetService.Load{T}"/> takes an asset.</summary>
public class AssetLoadOptions
{
    /// <summary>Off, the asset is kept and every load of it gives the same object until it is unloaded or disposed;
    /// on - as with no options at all - each load reads it anew.</summary>
    public bool AllowDuplication { get; set; }

    /// <summary>The name is the asset's path as it is, not taken from the service's root directory.</summary>
    public bool IgnoreRootDirectory { get; set; }
}
