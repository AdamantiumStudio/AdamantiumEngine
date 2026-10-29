namespace Adamantium.Content.Pipeline;

/// <summary>Summary of a content build.</summary>
public sealed class BuildResult
{
    /// <summary>Assets imported into their asset files, and copied to the output.</summary>
    public int Cooked { get; set; }

    /// <summary>Files copied to the output without being imported: an asset no importer takes, or an asset file already
    /// up to date beside its source.</summary>
    public int Copied { get; set; }

    /// <summary>Assets whose output was already up to date.</summary>
    public int Skipped { get; set; }

    /// <summary>Assets that could not be built; each is logged as an error.</summary>
    public int Failed { get; set; }

    public override string ToString() => $"cooked: {Cooked}, copied: {Copied}, up-to-date: {Skipped}, failed: {Failed}";
}
