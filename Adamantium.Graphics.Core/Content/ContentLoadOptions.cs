namespace Adamantium.Graphics.Core.Content;

/// <summary>How <see cref="IContentManager.Load{T}"/> takes an asset.</summary>
public class ContentLoadOptions
{
    /// <summary>Off, the asset is kept and every load of it gives the same object until it is unloaded or disposed;
    /// on - as with no options at all - each load reads it anew.</summary>
    public bool AllowDuplication { get; set; }

    /// <summary>The name is the asset's path as it is, not taken from the manager's root directory.</summary>
    public bool IgnoreRootDirectory { get; set; }
}
