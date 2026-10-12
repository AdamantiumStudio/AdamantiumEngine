using System.Collections.Generic;

namespace Adamantium.Content.Pipeline;

/// <summary>A single <see cref="BuildCache"/> record for one cooked asset.</summary>
public sealed class CacheEntry
{
    public string SourceHash { get; set; }

    public string ParametersHash { get; set; }

    public string Output { get; set; }

    /// <summary>The files put beside the output, relative to the output directory.</summary>
    public List<string> Companions { get; set; } = [];
}
