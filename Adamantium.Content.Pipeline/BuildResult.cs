namespace Adamantium.Content.Pipeline;

/// <summary>Summary of a content build.</summary>
public sealed class BuildResult
{
    public int Cooked { get; set; }

    /// <summary>Assets no importer takes, copied to the output as they are.</summary>
    public int Copied { get; set; }

    public int Skipped { get; set; }

    public int Failed { get; set; }

    public override string ToString() => $"cooked: {Cooked}, copied: {Copied}, up-to-date: {Skipped}, failed: {Failed}";
}
