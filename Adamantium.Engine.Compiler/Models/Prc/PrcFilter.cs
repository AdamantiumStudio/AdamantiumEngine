using System.Collections.Generic;

namespace Adamantium.Engine.Compiler.Models.Prc;

/// <summary>A filter of entities (PRC_TYPE_ASM_Filter): which layers and which entities it keeps or takes away, and
/// whether it is the active one.</summary>
internal sealed class PrcFilter : PrcTreeEntity
{
    internal bool IsActive { get; init; }

    /// <summary>Whether the layers are the ones kept rather than the ones taken away.</summary>
    internal bool LayersInclusive { get; init; }

    internal List<uint> Layers { get; } = [];

    /// <summary>Whether the entities are the ones kept rather than the ones taken away.</summary>
    internal bool EntitiesInclusive { get; init; }

    internal List<PrcEntityReference> Entities { get; } = [];
}
