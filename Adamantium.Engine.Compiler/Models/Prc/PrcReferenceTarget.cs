namespace Adamantium.Engine.Compiler.Models.Prc;

/// <summary>What a reference points at (ReferenceData): the type of the target, the file structure it lies in, and
/// its unique identifier there; or, for a reference on topology, a face of a body.</summary>
internal sealed class PrcReferenceTarget
{
    internal uint Type { get; init; }

    /// <summary>The file structure the target lies in; null for the referring entity's own.</summary>
    internal PrcUniqueId FileStructure { get; init; }

    internal uint UniqueId { get; init; }

    internal bool IsTopology { get; init; }
}
