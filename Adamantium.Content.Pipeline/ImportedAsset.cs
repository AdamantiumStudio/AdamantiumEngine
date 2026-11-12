namespace Adamantium.Content.Pipeline;

/// <summary>An asset a build imported: its asset file, why it was imported, and whether the asset file it replaced had a
/// thumbnail - which the new one has not, as it would show what the asset held before.</summary>
public readonly struct ImportedAsset
{
    public ImportedAsset(string assetFile, ImportReason reason, bool hadThumbnail)
    {
        AssetFile = assetFile;
        Reason = reason;
        HadThumbnail = hadThumbnail;
    }

    /// <summary>The full path of the asset file written.</summary>
    public string AssetFile { get; }

    public ImportReason Reason { get; }

    /// <summary>Whether the asset file replaced had a thumbnail.</summary>
    public bool HadThumbnail { get; }
}
